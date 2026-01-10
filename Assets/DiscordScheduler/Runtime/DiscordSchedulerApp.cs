using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DiscordScheduler
{
    public class DiscordSchedulerApp : MonoBehaviour
    {
        private LogService _log;
        private StorageService _storage;
        private DiscordWebhookClient _webhook;
        private SchedulerService _scheduler;

        private AppDatabase _db;

        private UIDocument _ui;
        private VisualElement _root;

        private Label _lblStatus;

        private VisualElement _viewTargets, _viewNew, _viewPosts, _viewSettings, _viewLog;

        private ListView _targetsList;
        private TextField _tfTargetName, _tfTargetServer, _tfTargetChannel, _tfTargetWebhook, _tfTargetUsername, _tfTargetAvatar;
        private Label _lblTargetHint;

        private DropdownField _ddPostTarget;
        private TextField _tfPostTitle, _tfPostBody, _tfDate, _tfTime, _tfImagePath;
        private DropdownField _ddAttachmentPick;
        private Toggle _tgPostModeNormal, _tgPostModeEmbed;
        private Toggle _tgAllowUsers, _tgAllowRoles, _tgAllowEveryone;
        private TextField _tfMentionUserIds, _tfMentionRoleIds;
        private DropdownField _ddOffPolicy, _ddSleepPolicy;
        private Label _lblScheduleResult, _lblImageHint, _lblPostBodyCounter;
        private Button _btnSchedule;

        private Toggle _tgAttachMedia;
        private VisualElement _mediaOptionsNew;
        private Toggle _tgMediaIsImage;
        private Toggle _tgMediaIsVideo;
        private TextField _tfMediaPath;
        private VisualElement _mediaButtonsNew;
        private Button _btnMediaClear;
        private DropdownField _ddMediaPick;
        private Label _lblMediaHint;
        private Image _imgMediaPreview;
        private VisualElement _mediaPreviewNew;
        private Texture2D _previewTexNew;
        private Coroutine _previewVideoCoNew;

        private ListView _postsList;
        private DropdownField _ddEditTarget, _ddEditAttachmentPick;
        private TextField _tfEditTitle, _tfEditBody, _tfEditDate, _tfEditTime, _tfEditImagePath;
        private Toggle _tgEditModeNormal, _tgEditModeEmbed;
        private Toggle _tgEditAllowUsers, _tgEditAllowRoles, _tgEditAllowEveryone;
        private TextField _tfEditMentionUserIds, _tfEditMentionRoleIds;
        private DropdownField _ddEditOffPolicy, _ddEditSleepPolicy;
        private Label _lblEditStatus, _lblEditResult, _lblEditBodyCounter;
        private Image _imgEditPreview;
        private Texture2D _previewTexEdit;
        private Coroutine _previewVideoCoEdit;

        private IntegerField _ifSleepThreshold;
        private Toggle _tgDefaultAllowUsers, _tgDefaultAllowRoles, _tgDefaultAllowEveryone;
        private TextField _tfDefaultUserIds, _tfDefaultRoleIds;
        private DropdownField _ddDefaultOffPolicy, _ddDefaultSleepPolicy;
        private Label _lblPaths, _lblSettingsResult;

        private ScrollView _logScroll;

        private VisualElement _modalOverlay;
        private VisualElement _modalCard;
        private Label _modalTitle;
        private VisualElement _modalBody;
        private Button _btnModalClose;
        private Action _modalOnClose;

        private readonly Queue<string> _sendQueuePostIds = new Queue<string>();
        private bool _isSending;

        private const int BodyMaxChars = 1980;

        private static readonly List<string> PolicyLabels = new List<string>
        {
            "Send it (next run)",
            "Mark Missed (don't send)",
            "Mark Failed (don't send)"
        };

        private float _lastHeartbeatRealtime;
        private float _lastHeartbeatLogRealtime;

        private void Awake()
        {
            // must keep running while minimized / unfocused
            Application.runInBackground = true;

#if !UNITY_EDITOR
            // Windows build: start windowed 1280x720
            Screen.fullScreenMode = FullScreenMode.Windowed;
            Screen.SetResolution(1280, 720, false);
#endif

            _log = new LogService();
            _storage = new StorageService(_log);
            _db = _storage.LoadOrCreate();
            EnsureSettingsDefaults();

            _webhook = new DiscordWebhookClient(_log);
            _scheduler = new SchedulerService(_db, _log);

            FileUtil.EnsureFolders();

            _log.Info("App Awake.");
            _lastHeartbeatRealtime = Time.realtimeSinceStartup;
            _lastHeartbeatLogRealtime = Time.realtimeSinceStartup;
        }

        private bool _hasFocus;

        private void OnApplicationFocus(bool hasFocus)
        {
            _hasFocus = hasFocus;

            if (_log == null) return;
            _log.Info("App focus: " + (hasFocus ? "gained" : "lost"));
        }

        private void OnApplicationPause(bool paused)
        {
            if (_log == null) return;
            _log.Info("App pause: " + (paused ? "paused" : "resumed"));
        }

        private void OnDisable()
        {
            CleanupPreviewResources(ref _previewTexNew, ref _previewVideoCoNew);
            CleanupPreviewResources(ref _previewTexEdit, ref _previewVideoCoEdit);
        }

        private void OnDestroy()
        {
            CleanupPreviewResources(ref _previewTexNew, ref _previewVideoCoNew);
            CleanupPreviewResources(ref _previewTexEdit, ref _previewVideoCoEdit);
        }

        private void CleanupPreviewResources(ref Texture2D tex, ref Coroutine videoCo)
        {
            if (videoCo != null)
            {
                StopCoroutine(videoCo);
                videoCo = null;
            }

            if (tex != null)
            {
                UnityEngine.Object.Destroy(tex);
                tex = null;
            }
        }

        private void EnsureSettingsDefaults()
        {
            // default both missed policies to "SendOnNextRun" (index 0) if unset or invalid
            if (_db.settings == null) return;

            if ((int)_db.settings.defaultOffPolicy < 0 || (int)_db.settings.defaultOffPolicy > 2)
                _db.settings.defaultOffPolicy = MissedPolicy.SendOnNextRun;

            if ((int)_db.settings.defaultSleepPolicy < 0 || (int)_db.settings.defaultSleepPolicy > 2)
                _db.settings.defaultSleepPolicy = MissedPolicy.SendOnNextRun;
        }

        private void Start()
        {
            BindUI();
            RefreshAllUI();

            // startup missed handling (off/app not running)
            _scheduler.OnStartupHandleMissed((p, isOffMissed, isSleepMissed) =>
            {
                ApplyMissedPolicyAndMaybeEnqueue(p, isOffMissed, isSleepMissed);
            });

            SaveDb();
            RefreshPostsList();
        }

        private void Update()
        {
            _lastHeartbeatRealtime = Time.realtimeSinceStartup;

            if (_log != null && (Time.realtimeSinceStartup - _lastHeartbeatLogRealtime) >= 10f)
            {
                _log.Info("Heartbeat: alive.");
                _lastHeartbeatLogRealtime = Time.realtimeSinceStartup;
            }

            _scheduler.Tick((p, isOffMissed, isSleepMissed) =>
            {
                ApplyMissedPolicyAndMaybeEnqueue(p, isOffMissed, isSleepMissed);
            });

            if (!_isSending && _sendQueuePostIds.Count > 0)
            {
                var id = _sendQueuePostIds.Dequeue();
                var post = _db.posts.FirstOrDefault(x => x.id == id);
                if (post != null)
                    StartCoroutine(SendPostCoroutine(post));
            }
        }

        private void BindUI()
        {

#if UNITY_2023_2_OR_NEWER
            _ui = FindFirstObjectByType<UIDocument>();
#else
            _ui = FindObjectOfType<UIDocument>();
#endif

            if (_ui == null)
            {
                Debug.LogError("Can't find any UIDocument in scene");
                enabled = false;
                return;
            }

            _root = _ui.rootVisualElement;

            // navigation
            _root.Q<Button>("btnTargets").clicked += () => ShowView(_viewTargets);
            _root.Q<Button>("btnNewPost").clicked += () =>
            {
                ShowView(_viewNew);
                RefreshAttachmentsDropdownNew();
                UpdateMediaPreviewNew();
            };
            _root.Q<Button>("btnPosts").clicked += () =>
            {
                ShowView(_viewPosts);
                RefreshPostsList();
                RefreshAttachmentsDropdownEdit();
                LoadSelectedPostIntoEditor();
            };
            _root.Q<Button>("btnSettings").clicked += () =>
            {
                ShowView(_viewSettings);
                LoadSettingsIntoUI();
            };
            _root.Q<Button>("btnLog").clicked += () => { ShowView(_viewLog); RefreshLog(); };
            _lblStatus = _root.Q<Label>("lblStatus");

            // views
            _viewTargets = _root.Q<VisualElement>("viewTargets");
            _viewNew = _root.Q<VisualElement>("viewNewPost");
            _viewPosts = _root.Q<VisualElement>("viewPosts");
            _viewSettings = _root.Q<VisualElement>("viewSettings");
            _viewLog = _root.Q<VisualElement>("viewLog");

            // targets refs
            _targetsList = _root.Q<ListView>("targetsList");
            _tfTargetName = _root.Q<TextField>("tfTargetName");
            _tfTargetServer = _root.Q<TextField>("tfTargetServer");
            _tfTargetChannel = _root.Q<TextField>("tfTargetChannel");
            _tfTargetWebhook = _root.Q<TextField>("tfTargetWebhook");
            _tfTargetUsername = _root.Q<TextField>("tfTargetUsername");
            _tfTargetAvatar = _root.Q<TextField>("tfTargetAvatar");
            _lblTargetHint = _root.Q<Label>("lblTargetHint");

            _root.Q<Button>("btnTargetAdd").clicked += AddTarget;
            _root.Q<Button>("btnTargetSave").clicked += SaveTargetFromFields;
            _root.Q<Button>("btnTargetDelete").clicked += DeleteSelectedTarget;
            _root.Q<Button>("btnTargetTest").clicked += TestSelectedTarget;

            // new post refs
            _ddPostTarget = _root.Q<DropdownField>("ddPostTarget");
            _tfPostTitle = _root.Q<TextField>("tfPostTitle");
            _tfPostBody = _root.Q<TextField>("tfPostBody");
            _tfDate = _root.Q<TextField>("tfDate");
            _tfTime = _root.Q<TextField>("tfTime");

            // pickers instead of manual typing
            SetReadOnlyPickerField(_tfDate, PickerKind.Date);
            SetReadOnlyPickerField(_tfTime, PickerKind.Time);

            _tfImagePath = _root.Q<TextField>("tfImagePath");
            _ddAttachmentPick = _root.Q<DropdownField>("ddAttachmentPick");
            _tgPostModeNormal = _root.Q<Toggle>("tgPostModeNormal");
            _tgPostModeEmbed = _root.Q<Toggle>("tgPostModeEmbed");

            _tgAllowUsers = _root.Q<Toggle>("tgAllowUsers");
            _tgAllowRoles = _root.Q<Toggle>("tgAllowRoles");
            _tgAllowEveryone = _root.Q<Toggle>("tgAllowEveryone");
            _tfMentionUserIds = _root.Q<TextField>("tfMentionUserIds");
            _tfMentionRoleIds = _root.Q<TextField>("tfMentionRoleIds");

            _ddOffPolicy = _root.Q<DropdownField>("ddOffPolicy");
            _ddSleepPolicy = _root.Q<DropdownField>("ddSleepPolicy");

            _lblScheduleResult = _root.Q<Label>("lblScheduleResult");
            _lblImageHint = _root.Q<Label>("lblImageHint");
            _lblPostBodyCounter = _root.Q<Label>("lblPostBodyCounter");
            _root.Q<Button>("btnSchedule").clicked += ScheduleNewPost;

            // media (new post)
            _tgAttachMedia = _root.Q<Toggle>("tgAttachMedia");
            _mediaOptionsNew = _root.Q<VisualElement>("mediaOptionsNew");
            _tgMediaIsImage = _root.Q<Toggle>("tgMediaIsImage");
            _tgMediaIsVideo = _root.Q<Toggle>("tgMediaIsVideo");
            _tfMediaPath = _root.Q<TextField>("tfMediaPath");
            _mediaButtonsNew = _root.Q<VisualElement>("mediaButtonsNew");
            _btnMediaClear = _root.Q<Button>("btnMediaClear");
            _ddMediaPick = _root.Q<DropdownField>("ddMediaPick");
            _lblMediaHint = _root.Q<Label>("lblMediaHint");
            _imgMediaPreview = _root.Q<Image>("imgMediaPreview");
            _mediaPreviewNew = _root.Q<VisualElement>("mediaPreviewNew");

            // hide image/video radio, autodetection is great
            if (_mediaOptionsNew != null) _mediaOptionsNew.style.display = DisplayStyle.None;
            if (_tgMediaIsImage != null) _tgMediaIsImage.style.display = DisplayStyle.None;
            if (_tgMediaIsVideo != null) _tgMediaIsVideo.style.display = DisplayStyle.None;

            void SetMediaUiVisible(bool visible)
            {
                var ds = visible ? DisplayStyle.Flex : DisplayStyle.None;

                if (_mediaOptionsNew != null) _mediaOptionsNew.style.display = DisplayStyle.None; // stay hide
                if (_tfMediaPath != null) _tfMediaPath.style.display = ds;
                if (_mediaButtonsNew != null) _mediaButtonsNew.style.display = ds;
                if (_ddMediaPick != null) _ddMediaPick.style.display = ds;
                if (_lblMediaHint != null) _lblMediaHint.style.display = ds;
                if (_mediaPreviewNew != null) _mediaPreviewNew.style.display = ds;
            }

            if (_tgAttachMedia != null)
            {
                SetMediaUiVisible(_tgAttachMedia.value);
                _tgAttachMedia.RegisterValueChangedCallback(evt =>
                {
                    SetMediaUiVisible(evt.newValue);

                    if (!evt.newValue)
                    {
                        if (_tfMediaPath != null) _tfMediaPath.value = "";
                        if (_ddMediaPick != null) _ddMediaPick.index = -1;
                        UpdateMediaPreviewNew();
                    }
                    else
                    {
                        RefreshAttachmentsDropdownNew();
                    }
                });
            }

            if (_btnMediaClear != null)
            {
                _btnMediaClear.clicked += () =>
                {
                    if (_tfMediaPath != null) _tfMediaPath.value = "";
                    if (_ddMediaPick != null) _ddMediaPick.index = -1;
                };
            }

            if (_ddMediaPick != null)
            {
                _ddMediaPick.RegisterValueChangedCallback(_ =>
                {
                    var path = GetSelectedAttachmentPath(_ddMediaPick);
                    if (!string.IsNullOrEmpty(path) && _tfMediaPath != null)
                        _tfMediaPath.value = path;
                    UpdateMediaPreviewNew();
                });
            }

            // media folder/refresh buttons (new post)
            var btnMediaOpenAttachments = _root.Q<Button>("btnMediaOpenAttachments");
            if (btnMediaOpenAttachments != null)
                btnMediaOpenAttachments.clicked += () => OpenFolder(FileUtil.AttachmentsFolder);

            var btnMediaRefresh = _root.Q<Button>("btnMediaRefresh");
            if (btnMediaRefresh != null)
                btnMediaRefresh.clicked += () =>
                {
                    RefreshAttachmentsDropdownNew();
                    UpdateMediaPreviewNew();
                };

            var btnMediaBrowse = _root.Q<Button>("btnMediaBrowse");
            if (btnMediaBrowse != null)
            {
                btnMediaBrowse.clicked += () =>
                {
                    // both types are allowed, decide based on the file extension
                    var filter = "Images/Videos (*.png;*.jpg;*.jpeg;*.webp;*.gif;*.mp4;*.webm;*.mov)|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.mp4;*.webm;*.mov|All files (*.*)|*.*";
                    var picked = TryPickFileWindows(filter);
                    if (!string.IsNullOrEmpty(picked) && _tfMediaPath != null)
                        _tfMediaPath.value = picked;
                    UpdateMediaPreviewNew();
                };
            }


            // posts refs
            _postsList = _root.Q<ListView>("postsList");
            _ddEditTarget = _root.Q<DropdownField>("ddEditTarget");
            _tfEditTitle = _root.Q<TextField>("tfEditTitle");
            _tfEditBody = _root.Q<TextField>("tfEditBody");
            _tfEditDate = _root.Q<TextField>("tfEditDate");
            _tfEditTime = _root.Q<TextField>("tfEditTime");
            SetReadOnlyPickerField(_tfEditDate, PickerKind.Date);
            SetReadOnlyPickerField(_tfEditTime, PickerKind.Time);

            _tfEditImagePath = _root.Q<TextField>("tfEditImagePath");
            _ddEditAttachmentPick = _root.Q<DropdownField>("ddEditAttachmentPick");
            _imgEditPreview = _root.Q<Image>("imgEditPreview");
            _tgEditModeNormal = _root.Q<Toggle>("tgEditModeNormal");
            _tgEditModeEmbed = _root.Q<Toggle>("tgEditModeEmbed");

            _tgEditAllowUsers = _root.Q<Toggle>("tgEditAllowUsers");
            _tgEditAllowRoles = _root.Q<Toggle>("tgEditAllowRoles");
            _tgEditAllowEveryone = _root.Q<Toggle>("tgEditAllowEveryone");
            _tfEditMentionUserIds = _root.Q<TextField>("tfEditMentionUserIds");
            _tfEditMentionRoleIds = _root.Q<TextField>("tfEditMentionRoleIds");

            _ddEditOffPolicy = _root.Q<DropdownField>("ddEditOffPolicy");
            _ddEditSleepPolicy = _root.Q<DropdownField>("ddEditSleepPolicy");

            _lblEditStatus = _root.Q<Label>("lblEditStatus");
            _lblEditResult = _root.Q<Label>("lblEditResult");
            _lblEditBodyCounter = _root.Q<Label>("lblEditBodyCounter");

            _root.Q<Button>("btnPostSave").clicked += SaveEditedPost;
            _root.Q<Button>("btnPostDelete").clicked += DeleteSelectedPost;
            _root.Q<Button>("btnPostForceSend").clicked += ForceSendSelectedPost;
            _root.Q<Button>("btnPostDeleteSent").clicked += DeleteSentPosts;
            _root.Q<Button>("btnPostMarkPending").clicked += MarkSelectedPostPending;
            _root.Q<Button>("btnEditImageClear").clicked += () => { _tfEditImagePath.value = ""; _ddEditAttachmentPick.index = -1; };
            _root.Q<Button>("btnOpenAttachments2").clicked += () => OpenFolder(FileUtil.AttachmentsFolder);
            _root.Q<Button>("btnEditRefreshAttachments").clicked += RefreshAttachmentsDropdownEdit;

            _root.Q<Button>("btnPostSave").clicked += SaveEditedPost;
            BindOpenFolderButtons("btnOpenAttachments2", FileUtil.AttachmentsFolder);
            BindOpenFolderButtons("btnOpenDataFolder", FileUtil.DataFolder);



            _ddEditAttachmentPick.RegisterValueChangedCallback(_ =>
            {
                var path = GetSelectedAttachmentPath(_ddEditAttachmentPick);
                if (!string.IsNullOrEmpty(path))
                    _tfEditImagePath.value = path;
                UpdateMediaPreviewEdit();
            });

            // settings refs
            _ifSleepThreshold = _root.Q<IntegerField>("ifSleepThreshold");
            _tgDefaultAllowUsers = _root.Q<Toggle>("tgDefaultAllowUsers");
            _tgDefaultAllowRoles = _root.Q<Toggle>("tgDefaultAllowRoles");
            _tgDefaultAllowEveryone = _root.Q<Toggle>("tgDefaultAllowEveryone");
            _tfDefaultUserIds = _root.Q<TextField>("tfDefaultUserIds");
            _tfDefaultRoleIds = _root.Q<TextField>("tfDefaultRoleIds");
            _ddDefaultOffPolicy = _root.Q<DropdownField>("ddDefaultOffPolicy");
            _ddDefaultSleepPolicy = _root.Q<DropdownField>("ddDefaultSleepPolicy");
            _lblPaths = _root.Q<Label>("lblPaths");
            _lblSettingsResult = _root.Q<Label>("lblSettingsResult");

            _root.Q<Button>("btnSaveSettings").clicked += SaveSettingsFromUI;
            _root.Q<Button>("btnOpenDataFolder").clicked += () => OpenFolder(FileUtil.DataFolder);

            // log refs
            _logScroll = _root.Q<ScrollView>("logScroll");
            _root.Q<Button>("btnLogClear").clicked += () => { _log.Clear(); RefreshLog(); };
            _root.Q<Button>("btnLogRefresh").clicked += RefreshLog;

            SetupListViews();
            SetupDropdowns();

            // body fields: hide labels, enforce limit, counters
            HideLabelAndExpandInput(_tfPostBody);
            HideLabelAndExpandInput(_tfEditBody);
            ConfigureBodyField(_tfPostBody, _lblPostBodyCounter);
            ConfigureBodyField(_tfEditBody, _lblEditBodyCounter);
            ConfigurePostModeRadios(_tgPostModeNormal, _tgPostModeEmbed);
            ConfigurePostModeRadios(_tgEditModeNormal, _tgEditModeEmbed);

            // UI only: hide ping/role related controls for now
            HideAllowedMentionsUI();

            // date/time picker modals
            EnsureModalUi();

            ShowView(_viewTargets);
        }

        private void SetupDropdowns()
        {
            _ddOffPolicy.choices = PolicyLabels;
            _ddSleepPolicy.choices = PolicyLabels;
            _ddEditOffPolicy.choices = PolicyLabels;
            _ddEditSleepPolicy.choices = PolicyLabels;

            _ddDefaultOffPolicy.choices = PolicyLabels;
            _ddDefaultSleepPolicy.choices = PolicyLabels;
        }

        private enum PickerKind { Date, Time }

        private void SetReadOnlyPickerField(TextField field, PickerKind kind)
        {
            if (field == null) return;

            field.isReadOnly = true;

            // ensure wrapper receives picking too (helps in some versions)
            field.pickingMode = PickingMode.Position;

            void OpenPicker()
            {
                if (kind == PickerKind.Date)
                    ShowDatePicker(field);
                else
                    ShowTimePicker(field);
            }

            void OnPointerDown(PointerDownEvent pointerDownEvent)
            {
                if (pointerDownEvent.button != 0) return;

                // prevent double-open if something triggers twice
                if (_modalOverlay != null && _modalOverlay.resolvedStyle.display != DisplayStyle.None)
                    return;

                OpenPicker();
                pointerDownEvent.StopPropagation();
            }

            // register on the field (capture helps when event originates from child)
            field.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);

            VisualElement input =
                field.Q(className: "unity-text-field__input") ??
                field.Q(className: "unity-base-text-field__input") ??
                field.Q(className: "unity-text-field__input-field") ??
                field.Q(className: "unity-base-text-field__input-field");

            if (input != null)
            {
                input.pickingMode = PickingMode.Position;
                input.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            }
        }

        private void HideAllowedMentionsUI()
        {
            // hide all ping/role-related UI elements (logic stays for later)
            string[] hideNames =
            {
                "tgAllowUsers","tgAllowRoles","tgAllowEveryone",
                "tfMentionUserIds","tfMentionRoleIds",

                "tgEditAllowUsers","tgEditAllowRoles","tgEditAllowEveryone",
                "tfEditMentionUserIds","tfEditMentionRoleIds",

                "tgDefaultAllowUsers","tgDefaultAllowRoles","tgDefaultAllowEveryone",
                "tfDefaultUserIds","tfDefaultRoleIds",
            };

            foreach (var n in hideNames)
            {
                var visualElement = _root?.Q<VisualElement>(n);
                if (visualElement != null)
                    visualElement.style.display = DisplayStyle.None;
            }

            // hide section headers if present
            if (_root != null)
            {
                foreach (var label in _root.Query<Label>().Build())
                {
                    var t = (label.text ?? "").ToLowerInvariant();
                    if (t.Contains("allowed mentions") || t.Contains("ping szabály") || t.Contains("ping"))
                    {
                        // be conservative: only hide the "Allowed mentions" header labels
                        if (t.Contains("allowed mentions") || t.Contains("ping szabály"))
                            label.style.display = DisplayStyle.None;
                    }
                }
            }
        }

        private void EnsureModalUi()
        {
            if (_root == null || _modalOverlay != null) return;

            _modalOverlay = new VisualElement();
            _modalOverlay.name = "modalOverlay";
            _modalOverlay.AddToClassList("modalOverlay");

            _modalOverlay.style.position = Position.Absolute;
            _modalOverlay.style.left = 0;
            _modalOverlay.style.right = 0;
            _modalOverlay.style.top = 0;
            _modalOverlay.style.bottom = 0;

            // keep layout inline, but allow USS to control colors
            _modalOverlay.style.alignItems = Align.Center;
            _modalOverlay.style.justifyContent = Justify.Center;
            _modalOverlay.style.display = DisplayStyle.None;

            _modalOverlay.RegisterCallback<PointerDownEvent>(pointerDownEvent =>
            {
                var visualElement = pointerDownEvent.target as VisualElement;
                if (_modalCard != null && visualElement != null && _modalCard.Contains(visualElement)) return;

                HideModal();
                pointerDownEvent.StopPropagation();
            });

            _modalCard = new VisualElement();
            _modalCard.name = "modalCard";
            _modalCard.AddToClassList("modalCard");

            _modalCard.style.paddingLeft = 14;
            _modalCard.style.paddingRight = 14;
            _modalCard.style.paddingTop = 12;
            _modalCard.style.paddingBottom = 12;
            _modalCard.style.minWidth = 520;
            _modalCard.style.maxWidth = 860;

            var header = new VisualElement();
            header.AddToClassList("modalHeader");
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;

            _modalTitle = new Label("Picker");
            _modalTitle.AddToClassList("modalTitle");
            _modalTitle.style.flexGrow = 1;

            _btnModalClose = new Button(HideModal) { text = "✕" };
            _btnModalClose.AddToClassList("modalClose");
            _btnModalClose.style.width = 34;
            _btnModalClose.style.height = 26;

            header.Add(_modalTitle);
            header.Add(_btnModalClose);

            _modalBody = new VisualElement();
            _modalBody.AddToClassList("modalBody");
            _modalBody.style.marginTop = 10;

            _modalCard.Add(header);
            _modalCard.Add(_modalBody);
            _modalOverlay.Add(_modalCard);
            _root.Add(_modalOverlay);
        }

        private void ShowModal(string title, VisualElement body, Action onClose = null)
        {
            EnsureModalUi();

            _modalOnClose = onClose;
            _modalTitle.text = title ?? "Picker";

            _modalBody.Clear();
            if (body != null) _modalBody.Add(body);

            _modalOverlay.style.display = DisplayStyle.Flex;
        }

        private void HideModal()
        {
            if (_modalOverlay == null) return;

            _modalOverlay.style.display = DisplayStyle.None;

            try { _modalOnClose?.Invoke(); }
            catch (Exception e) { Debug.LogWarning("Modal onClose error: " + e.Message); }

            _modalOnClose = null;
            _modalBody?.Clear();
        }

        private void ShowDatePicker(TextField targetField)
        {
            var tz = TimeUtil.GetBudapestTimeZone();
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;

            DateTime current = nowLocal;
            if (DateTime.TryParseExact((targetField?.value ?? "").Trim(), "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsed))
            {
                current = parsed.Date;
            }

            var content = BuildDatePicker(current, selected =>
            {
                if (targetField != null)
                    targetField.value = selected.ToString("yyyy-MM-dd");
                HideModal();
            });

            ShowModal("Select date", content);
        }

        private VisualElement BuildDatePicker(DateTime current, Action<DateTime> onSelected)
        {
            DateTime shown = new DateTime(current.Year, current.Month, 1);

            var root = new VisualElement();

            const int cellW = 72;
            const int cellH = 34;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 10;

            var lbl = new Label();
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;

            var grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;

            // modal height stable across months (max 5 rows)
            grid.style.height = (cellH * 5) + 8;
            grid.style.overflow = Overflow.Hidden;

            void Rebuild()
            {
                lbl.text = shown.ToString("yyyy MMMM", System.Globalization.CultureInfo.InvariantCulture);

                grid.Clear();

                int daysInMonth = DateTime.DaysInMonth(shown.Year, shown.Month);
                for (int d = 1; d <= daysInMonth; d++)
                {
                    var date = new DateTime(shown.Year, shown.Month, d);

                    var b = new Button(() => onSelected?.Invoke(date)) { text = d.ToString() };
                    b.style.width = cellW;
                    b.style.height = cellH;

                    if (date.Date == current.Date)
                        b.style.backgroundColor = new Color(0.20f, 0.40f, 0.85f, 1f);

                    grid.Add(b);
                }
            }

            var btnPrev = new Button(() =>
            {
                shown = shown.AddMonths(-1);
                Rebuild();
            })
            { text = "◀" };
            btnPrev.style.width = 44;

            var btnNext = new Button(() =>
            {
                shown = shown.AddMonths(1);
                Rebuild();
            })
            { text = "▶" };
            btnNext.style.width = 44;

            header.Add(btnPrev);
            header.Add(lbl);
            header.Add(btnNext);

            Rebuild();

            root.Add(header);
            root.Add(grid);

            return root;
        }

        private void ShowTimePicker(TextField targetField)
        {
            int hour = 12;
            int minute = 0;

            var v = (targetField?.value ?? "").Trim();
            if (DateTime.TryParseExact(v, "HH:mm", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsed))
            {
                hour = parsed.Hour;
                minute = parsed.Minute;
            }

            var content = BuildTimePicker(hour, minute, (h, m) =>
            {
                if (targetField != null)
                    targetField.value = $"{Mathf.Clamp(h, 0, 23):00}:{Mathf.Clamp(m, 0, 59):00}";
                HideModal();
            });

            ShowModal("Select time", content);
        }

        private VisualElement BuildTimePicker(int startHour, int startMinute, Action<int, int> onSelected)
        {
            int selHour = Mathf.Clamp(startHour, 0, 23);
            int selMin = Mathf.Clamp(startMinute, 0, 59);

            var root = new VisualElement();

            var preview = new Label($"{selHour:00}:{selMin:00}");
            preview.style.unityFontStyleAndWeight = FontStyle.Bold;
            preview.style.fontSize = 18;
            preview.style.marginBottom = 10;

            root.Add(preview);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.NoWrap;

            // hours
            var hoursCol = new VisualElement();
            var hoursTitle = new Label("Hour (0–23)");
            hoursTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            hoursTitle.style.marginBottom = 6;

            var hoursGrid = new VisualElement();
            hoursGrid.style.flexDirection = FlexDirection.Row;
            hoursGrid.style.flexWrap = Wrap.Wrap;

            void RebuildHours()
            {
                hoursGrid.Clear();
                for (int h = 0; h <= 23; h++)
                {
                    int hh = h;
                    var b = new Button(() =>
                    {
                        selHour = hh;
                        preview.text = $"{selHour:00}:{selMin:00}";
                        RebuildHours();
                    })
                    { text = h.ToString("00") };

                    b.style.width = 64;
                    b.style.height = 30;

                    if (hh == selHour)
                        b.style.backgroundColor = new Color(0.20f, 0.40f, 0.85f, 1f);

                    hoursGrid.Add(b);
                }
            }

            RebuildHours();
            hoursCol.Add(hoursTitle);
            hoursCol.Add(hoursGrid);

            // minutes
            var minsCol = new VisualElement();
            minsCol.style.marginLeft = 14;

            var minsTitle = new Label("Minute (0–59)");
            minsTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            minsTitle.style.marginBottom = 6;

            var minsQuick = new VisualElement();
            minsQuick.style.flexDirection = FlexDirection.Row;
            minsQuick.style.flexWrap = Wrap.Wrap;

            void RebuildMinutes()
            {
                minsQuick.Clear();
                for (int m = 0; m <= 55; m += 5)
                {
                    int mm = m;
                    var b = new Button(() =>
                    {
                        selMin = mm;
                        preview.text = $"{selHour:00}:{selMin:00}";
                        RebuildMinutes();
                    })
                    { text = m.ToString("00") };

                    b.style.width = 52;
                    b.style.height = 30;

                    if (mm == selMin && (selMin % 5 == 0))
                        b.style.backgroundColor = new Color(0.20f, 0.40f, 0.85f, 1f);

                    minsQuick.Add(b);
                }
            }

            RebuildMinutes();

            var exact = new IntegerField("Specific minute");
            exact.value = selMin;
            exact.RegisterValueChangedCallback(evt =>
            {
                selMin = Mathf.Clamp(evt.newValue, 0, 59);
                preview.text = $"{selHour:00}:{selMin:00}";
            });

            minsCol.Add(minsTitle);
            minsCol.Add(minsQuick);
            minsCol.Add(exact);

            row.Add(hoursCol);
            row.Add(minsCol);

            root.Add(row);

            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.justifyContent = Justify.FlexEnd;
            footer.style.marginTop = 12;

            var btnCancel = new Button(HideModal) { text = "Cancel" };
            btnCancel.style.marginRight = 8;

            var btnOk = new Button(() => onSelected?.Invoke(selHour, selMin)) { text = "OK" };
            btnOk.style.unityFontStyleAndWeight = FontStyle.Bold;

            footer.Add(btnCancel);
            footer.Add(btnOk);

            root.Add(footer);

            return root;
        }

        private void SetupListViews()
        {
            // targets listview
            _targetsList.makeItem = () => new Label();
            _targetsList.bindItem = (e, i) =>
            {
                var lbl = e as Label;
                if (lbl == null) return;
                if (i < 0 || i >= _db.targets.Count) return;
                var t = _db.targets[i];
                lbl.text = $"{t.name}  —  {t.serverLabel} / {t.channelLabel}";
            };
            _targetsList.selectionType = SelectionType.Single;
            _targetsList.selectionChanged += _ => LoadSelectedTargetIntoFields();

            // posts listview
            _postsList.makeItem = () => new Label();
            _postsList.bindItem = (e, i) =>
            {
                var lbl = e as Label;
                if (lbl == null) return;
                if (i < 0 || i >= _db.posts.Count) return;
                var p = _db.posts[i];
                var local = TimeUtil.ParseIsoUtc(p.scheduledAtUtcIso);
                var (dateYmd, timeHm) = TimeUtil.UtcToBudapestFields(local);
                lbl.text = $"{dateYmd} {timeHm}  –  {p.status}  –  {p.title}";
            };
            _postsList.selectionType = SelectionType.Single;
            _postsList.selectionChanged += _ => LoadSelectedPostIntoEditor();
        }

        private void RefreshAllUI()
        {
            RefreshTargetsDropdowns();
            RefreshTargetsList();
            RefreshPostsList();
            RefreshAttachmentsDropdownNew();
            RefreshAttachmentsDropdownEdit();
            LoadSettingsIntoUI();
            RefreshBodyCounter(_tfPostBody, _lblPostBodyCounter);
            RefreshBodyCounter(_tfEditBody, _lblEditBodyCounter);
            SetRadio(_tgPostModeNormal, _tgPostModeEmbed, false); // default: Normal
            SetRadio(_tgEditModeNormal, _tgEditModeEmbed, false); // default: Normal

            // set default missed-policy selections for NEW post UI from settings
            if (_db?.settings != null)
            {
                _ddOffPolicy.index = Mathf.Clamp((int)_db.settings.defaultOffPolicy, 0, 2);
                _ddSleepPolicy.index = Mathf.Clamp((int)_db.settings.defaultSleepPolicy, 0, 2);
            }

            // set default date/time for NEW post UI (local Budapest, now +10 minutes), handling day rollover
            var tz = TimeUtil.GetBudapestTimeZone();
            var localPlus10 = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).AddMinutes(10);
            _tfDate.value = localPlus10.ToString("yyyy-MM-dd");
            _tfTime.value = localPlus10.ToString("HH:mm");

            // UI-only: hide ping/role related controls for now (logic stays)
            HideAllowedMentionsUI();

            // Date/Time picker modals
            EnsureModalUi();

            ShowView(_viewTargets);
        }

        private void RefreshTargetsList()
        {
            _targetsList.itemsSource = _db.targets;
            _targetsList.Rebuild();
        }

        private void RefreshPostsList()
        {
            SortPostsByDate(); // ensure list is date-ordered
            _postsList.itemsSource = _db.posts;
            _postsList.Rebuild();

            // LINQ: count pending posts for status label
            _lblStatus.text = $"Targets: {_db.targets.Count} | Posts: {_db.posts.Count} | Pending: {_db.posts.Count(post => post.status == PostStatus.Pending)}";
        }

        private void RefreshTargetsDropdowns()
        {
            // LINQ: project target names for dropdown choices
            var targetNames = _db.targets.Select(target => target.name).ToList();

            _ddPostTarget.choices = targetNames;
            _ddEditTarget.choices = targetNames;

            if (targetNames.Count > 0)
            {
                if (_ddPostTarget.index < 0) _ddPostTarget.index = 0;
                if (_ddEditTarget.index < 0) _ddEditTarget.index = 0;
            }
            else
            {
                _ddPostTarget.index = -1;
                _ddEditTarget.index = -1;
            }
        }

        private void AddTarget()
        {
            var t = Target.CreateNew();
            _db.targets.Add(t);
            SaveDb();
            RefreshTargetsDropdowns();
            RefreshTargetsList();
            _targetsList.selectedIndex = _db.targets.Count - 1;
            LoadSelectedTargetIntoFields();
        }

        private void SaveTargetFromFields()
        {
            var idx = _targetsList.selectedIndex;
            if (idx < 0 || idx >= _db.targets.Count)
            {
                _lblTargetHint.text = "Target not selected.";
                return;
            }

            var t = _db.targets[idx];

            t.name = (_tfTargetName.value ?? "").Trim();
            t.serverLabel = (_tfTargetServer.value ?? "").Trim();
            t.channelLabel = (_tfTargetChannel.value ?? "").Trim();
            t.webhookUrl = (_tfTargetWebhook.value ?? "").Trim();
            t.overrideUsername = (_tfTargetUsername.value ?? "").Trim();
            t.overrideAvatarUrl = (_tfTargetAvatar.value ?? "").Trim();

            if (string.IsNullOrWhiteSpace(t.name))
            {
                _lblTargetHint.text = "Musthave target name.";
                return;
            }

            if (string.IsNullOrWhiteSpace(t.webhookUrl) || !t.webhookUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                _lblTargetHint.text = "Musthave Webhook URL (http/https).";
                return;
            }

            SaveDb();
            RefreshTargetsDropdowns();
            RefreshTargetsList();
            _lblTargetHint.text = "Saved.";
        }

        private void DeleteSelectedTarget()
        {
            var idx = _targetsList.selectedIndex;
            if (idx < 0 || idx >= _db.targets.Count) return;

            var id = _db.targets[idx].id;

            // also remove posts linked to this target
            _db.posts.RemoveAll(p => p.targetId == id);

            _db.targets.RemoveAt(idx);

            SaveDb();
            RefreshTargetsDropdowns();
            RefreshTargetsList();
            RefreshPostsList();
            _lblTargetHint.text = "Deleted.";
        }

        private void LoadSelectedTargetIntoFields()
        {
            var idx = _targetsList.selectedIndex;
            if (idx < 0 || idx >= _db.targets.Count)
            {
                _tfTargetName.value = "";
                _tfTargetServer.value = "";
                _tfTargetChannel.value = "";
                _tfTargetWebhook.value = "";
                _tfTargetUsername.value = "";
                _tfTargetAvatar.value = "";
                return;
            }

            var t = _db.targets[idx];
            _tfTargetName.value = t.name ?? "";
            _tfTargetServer.value = t.serverLabel ?? "";
            _tfTargetChannel.value = t.channelLabel ?? "";
            _tfTargetWebhook.value = t.webhookUrl ?? "";
            _tfTargetUsername.value = t.overrideUsername ?? "";
            _tfTargetAvatar.value = t.overrideAvatarUrl ?? "";
        }

        private void TestSelectedTarget()
        {
            var idx = _targetsList.selectedIndex;
            if (idx < 0 || idx >= _db.targets.Count)
            {
                _lblTargetHint.text = "No target Selected.";
                return;
            }

            var t = _db.targets[idx];

            var temp = ScheduledPost.CreateNew(t.id);
            temp.title = "Test message";
            temp.body = "Test message from DisDoveOnTime.";
            temp.SetScheduledAtUtc(DateTime.UtcNow); 

            StartCoroutine(_webhook.Send(t, temp, (ok, err) =>
            {
                _lblTargetHint.text = ok ? "Test sent." : $"Error: {err}";
            }));
        }

        private Target GetTargetByDropdown(DropdownField dropdownField)
        {
            if (dropdownField == null) return null; // guard: missing dropdown binding
            if (dropdownField.index < 0 || dropdownField.choices == null || dropdownField.index >= dropdownField.choices.Count) return null; // Guard: no selection

            var targetName = dropdownField.choices[dropdownField.index];
            return _db.targets.FirstOrDefault(target => target.name == targetName); // LINQ: pick the first target matching the selected name
        }


        
        private void ScheduleNewPost()
        {
            _lblScheduleResult.text = "";

            if (_ddPostTarget == null || _tfDate == null || _tfTime == null)
            {
                _lblScheduleResult.text = "UI error: missing fields (target/date/time).";
                return;
            }

            if (_ddOffPolicy == null || _ddSleepPolicy == null)
            {
                _lblScheduleResult.text = "UI error: missing delay policy.";
                return;
            }

            if (_db == null)
            {
                _lblScheduleResult.text = "Error: no database.";
                return;
            }

            if (_db.targets == null || _db.targets.Count == 0)
            {
                _lblScheduleResult.text = "First create a Target (webhook).";
                return;
            }

            var target = GetTargetByDropdown(_ddPostTarget);
            if (target == null)
            {
                _lblScheduleResult.text = "No target selected";
                return;
            }

            var scheduledUtc = TimeUtil.LocalBudapestToUtc((_tfDate.value ?? "").Trim(), (_tfTime.value ?? "").Trim(), out var errorMessage);
            if (!string.IsNullOrEmpty(errorMessage))
            {
                _lblScheduleResult.text = errorMessage;
                return;
            }

            var scheduledPost = ScheduledPost.CreateNew(target.id);
            scheduledPost.title = (_tfPostTitle?.value ?? "").Trim();
            scheduledPost.body = (_tfPostBody?.value ?? "").Trim();
            scheduledPost.sendAsEmbed = _tgPostModeEmbed != null && _tgPostModeEmbed.value;
            scheduledPost.SetScheduledAtUtc(scheduledUtc); // <- FIX

            // allowed mentions from UI
            scheduledPost.allowedMentions.allowUsers = _tgAllowUsers != null && _tgAllowUsers.value;
            scheduledPost.allowedMentions.allowRoles = _tgAllowRoles != null && _tgAllowRoles.value;
            scheduledPost.allowedMentions.allowEveryone = _tgAllowEveryone != null && _tgAllowEveryone.value;
            scheduledPost.allowedMentions.userIdsCsv = _tfMentionUserIds?.value ?? "";
            scheduledPost.allowedMentions.roleIdsCsv = _tfMentionRoleIds?.value ?? "";

            scheduledPost.missedPolicyIfOff = (MissedPolicy)Mathf.Clamp(_ddOffPolicy.index, 0, 2);
            scheduledPost.missedPolicyIfSleep = (MissedPolicy)Mathf.Clamp(_ddSleepPolicy.index, 0, 2);

            // copy image to attachments folder
            string imagePath = (_tfImagePath?.value ?? "").Trim();
            if (!string.IsNullOrEmpty(imagePath))
            {
                if (!FileUtil.TryCopyToAttachments(imagePath, scheduledPost.id, out var newPath, out var copyError))
                {
                    _lblScheduleResult.text = copyError;
                    return; // copy failed
                }

                if (!string.IsNullOrEmpty(newPath))
                {
                    long sizeBytes = FileUtil.GetFileSizeBytes(newPath);
                    if (sizeBytes > FileUtil.MaxAttachmentBytes)
                    {
                        _lblScheduleResult.text = "Image too large (>10MB). Non-Nitro limit.";
                        return; // oversized image
                    }

                    scheduledPost.imagePath = newPath;
                }
            }

            // media: copy into attachments folder if selected
            scheduledPost.SetMedia(MediaKind.None, "");

            if (_tgAttachMedia != null && _tgAttachMedia.value)
            {
                var mediaPath = (_tfMediaPath?.value ?? "").Trim();

                if (!string.IsNullOrEmpty(mediaPath))
                {
                    if (!FileUtil.TryCopyMediaToAttachments(mediaPath, scheduledPost.id, MediaKind.None, out var newPath, out var copyError))
                    {
                        _lblScheduleResult.text = copyError;
                        return; // early return: media copy failed
                    }

                    if (!string.IsNullOrEmpty(newPath))
                    {
                        var mediaKind = FileUtil.GuessKindFromExt((Path.GetExtension(newPath) ?? "").ToLowerInvariant());
                        scheduledPost.SetMedia(mediaKind, newPath);
                    }
                }
            }

            // basic validations
            if (string.IsNullOrWhiteSpace(scheduledPost.title) && string.IsNullOrWhiteSpace(scheduledPost.body))
            {
                _lblScheduleResult.text = "Title or message is required.";
                return; // early return: nothing to send
            }

            _db.posts.Add(scheduledPost);
            _log.Info($"Scheduled '{scheduledPost.title}' ({scheduledPost.id}) for {scheduledPost.scheduledAtUtcIso} -> {target.name}");
            SaveDb();
            RefreshPostsList();

            _lblScheduleResult.text = "Scheduled.";
        }



        private void LoadSelectedPostIntoEditor()
        {
            _lblEditResult.text = "";

            var selectedIndex = _postsList.selectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _db.posts.Count)
            {
                _tfEditTitle.value = "";
                _tfEditBody.value = "";
                _tfEditDate.value = "";
                _tfEditTime.value = "";
                _tfEditImagePath.value = "";
                if (_ddEditAttachmentPick != null) _ddEditAttachmentPick.index = -1;
                _lblEditStatus.text = "";
                RefreshBodyCounter(_tfEditBody, _lblEditBodyCounter);
                UpdateMediaPreviewEdit();
                return; // early return: no selection
            }

            var scheduledPost = _db.posts[selectedIndex];

            _tfEditTitle.value = scheduledPost.title ?? "";
            _tfEditBody.value = scheduledPost.body ?? "";

            var scheduledUtc = TimeUtil.ParseIsoUtc(scheduledPost.scheduledAtUtcIso);
            var (dateYmd, timeHm) = TimeUtil.UtcToBudapestFields(scheduledUtc);
            _tfEditDate.value = dateYmd;
            _tfEditTime.value = timeHm;

            // media path: prefer "new" media system if present, otherwise fallback to legacy imagePath
            var effectiveMediaPath = (scheduledPost.EffectiveMediaPath() ?? "").Trim();
            var legacyImagePath = (scheduledPost.imagePath ?? "").Trim();

            var editPath = !string.IsNullOrWhiteSpace(effectiveMediaPath) ? effectiveMediaPath : legacyImagePath;

            _tfEditImagePath.value = editPath;
            SyncEditAttachmentDropdownToPath(editPath);

            _tgEditAllowUsers.value = scheduledPost.allowedMentions.allowUsers;
            _tgEditAllowRoles.value = scheduledPost.allowedMentions.allowRoles;
            _tgEditAllowEveryone.value = scheduledPost.allowedMentions.allowEveryone;
            _tfEditMentionUserIds.value = scheduledPost.allowedMentions.userIdsCsv ?? "";
            _tfEditMentionRoleIds.value = scheduledPost.allowedMentions.roleIdsCsv ?? "";

            _ddEditOffPolicy.index = Mathf.Clamp((int)scheduledPost.missedPolicyIfOff, 0, 2);
            _ddEditSleepPolicy.index = Mathf.Clamp((int)scheduledPost.missedPolicyIfSleep, 0, 2);

            _lblEditStatus.text = $"Status: {scheduledPost.status} | Retries: {scheduledPost.retries} | LastError: {scheduledPost.lastError}";
            RefreshBodyCounter(_tfEditBody, _lblEditBodyCounter);
            SetRadio(_tgEditModeNormal, _tgEditModeEmbed, scheduledPost.sendAsEmbed);

            UpdateMediaPreviewEdit();
        }

        private void SyncEditAttachmentDropdownToPath(string path)
        {
            if (_ddEditAttachmentPick == null)
                return;

            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith(FileUtil.AttachmentsFolder, StringComparison.OrdinalIgnoreCase))
            {
                _ddEditAttachmentPick.index = -1;
                return;
            }

            var fileName = Path.GetFileName(path);
            var idx = _ddEditAttachmentPick.choices != null ? _ddEditAttachmentPick.choices.IndexOf(fileName) : -1;
            _ddEditAttachmentPick.index = idx >= 0 ? idx : -1;
        }

        private void SaveEditedPost()
        {
            _lblEditResult.text = "";

            var selectedIndex = _postsList.selectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _db.posts.Count)
            {
                _lblEditResult.text = "No post selected.";
                return; // early return: no selection
            }

            var scheduledPost = _db.posts[selectedIndex];

            var target = GetTargetByDropdown(_ddEditTarget);
            if (target == null)
            {
                _lblEditResult.text = "No target selected.";
                return; // early return: invalid target
            }

            var scheduledAt = TimeUtil.LocalBudapestToUtc(_tfEditDate.value, _tfEditTime.value, out var errorMessage);
            if (!string.IsNullOrEmpty(errorMessage))
            {
                _lblEditResult.text = errorMessage;
                return; // early return: invalid date/time
            }

            scheduledPost.targetId = target.id;
            scheduledPost.title = (_tfEditTitle.value ?? "").Trim();
            scheduledPost.body = (_tfEditBody.value ?? "").Trim();
            scheduledPost.sendAsEmbed = _tgEditModeEmbed != null && _tgEditModeEmbed.value;
            scheduledPost.SetScheduledAtUtc(scheduledAt);

            scheduledPost.allowedMentions.allowUsers = _tgEditAllowUsers.value;
            scheduledPost.allowedMentions.allowRoles = _tgEditAllowRoles.value;
            scheduledPost.allowedMentions.allowEveryone = _tgEditAllowEveryone.value;
            scheduledPost.allowedMentions.userIdsCsv = _tfEditMentionUserIds.value ?? "";
            scheduledPost.allowedMentions.roleIdsCsv = _tfEditMentionRoleIds.value ?? "";

            scheduledPost.missedPolicyIfOff = (MissedPolicy)Mathf.Clamp(_ddEditOffPolicy.index, 0, 2);
            scheduledPost.missedPolicyIfSleep = (MissedPolicy)Mathf.Clamp(_ddEditSleepPolicy.index, 0, 2);

            // image copy if changed
            string imagePath = (_tfEditImagePath.value ?? "").Trim();
            if (string.IsNullOrEmpty(imagePath))
            {
                scheduledPost.imagePath = "";
                SyncEditAttachmentDropdownToPath("");
                UpdateMediaPreviewEdit();
            }
            else
            {
                // if its already in attachments folder, accept as-is
                if (!imagePath.StartsWith(FileUtil.AttachmentsFolder, StringComparison.OrdinalIgnoreCase))
                {
                    if (!FileUtil.TryCopyToAttachments(imagePath, scheduledPost.id, out var newPath, out var copyError))
                    {
                        _lblEditResult.text = copyError;
                        return; // Early return: copy failed
                    }

                    if (!string.IsNullOrEmpty(newPath))
                    {
                        long sizeBytes = FileUtil.GetFileSizeBytes(newPath);
                        if (sizeBytes > FileUtil.MaxAttachmentBytes)
                        {
                            _lblEditResult.text = "Image too large (>10 MB). Non-Nitro limit.";
                            return; // early return: oversized image
                        }

                        scheduledPost.imagePath = newPath;
                        SyncEditAttachmentDropdownToPath(newPath);
                        UpdateMediaPreviewEdit();
                    }
                }
                else
                {
                    // still validate size
                    if (File.Exists(imagePath))
                    {
                        long sizeBytes = FileUtil.GetFileSizeBytes(imagePath);
                        if (sizeBytes > FileUtil.MaxAttachmentBytes)
                        {
                            _lblEditResult.text = "Image too large (>10 MB). Non-Nitro limit.";
                            return; // early return: oversized image
                        }
                    }

                    scheduledPost.imagePath = imagePath;
                    SyncEditAttachmentDropdownToPath(imagePath);
                    UpdateMediaPreviewEdit();
                }
            }

            // Basic validations
            if (string.IsNullOrWhiteSpace(scheduledPost.title) && string.IsNullOrWhiteSpace(scheduledPost.body))
            {
                _lblEditResult.text = "Please enter a title or text.";
                return; // Early return: nothing to send
            }

            scheduledPost.updatedAtUtcIso = TimeUtil.ToIsoUtc(DateTime.UtcNow);

            SaveDb();
            RefreshPostsList();
            _lblEditResult.text = "Save.";
            LoadSelectedPostIntoEditor();
        }

        private void DeleteSelectedPost()
        {
            var idx = _postsList.selectedIndex;
            if (idx < 0 || idx >= _db.posts.Count) return;

            _db.posts.RemoveAt(idx);
            SaveDb();
            RefreshPostsList();
            _lblEditResult.text = "Deleted.";
        }

        private void DeleteSentPosts()
        {
            int removed = _db.posts.RemoveAll(p => p.status == PostStatus.Sent);
            if (removed > 0)
                _log.Info($"Deleted {removed} sent post(s).");
            SaveDb();
            RefreshPostsList();
            _lblEditResult.text = removed > 0 ? $"Removed: {removed}" : "There are no sent posts in database.";
        }

        private void ForceSendSelectedPost()
        {
            var idx = _postsList.selectedIndex;
            if (idx < 0 || idx >= _db.posts.Count)
            {
                _lblEditResult.text = "No post selected.";
                return;
            }

            var p = _db.posts[idx];
            _log.Info($"Force send requested: {p.id} ({p.title})");
            EnqueueSend(p);
            _lblEditResult.text = "Queued for sending.";
        }

        private void MarkSelectedPostPending()
        {
            var idx = _postsList.selectedIndex;
            if (idx < 0 || idx >= _db.posts.Count)
            {
                _lblEditResult.text = "No post selected.";
                return;
            }

            var p = _db.posts[idx];
            p.status = PostStatus.Pending;
            p.lastError = "";
            p.retries = 0;
            p.updatedAtUtcIso = TimeUtil.ToIsoUtc(DateTime.UtcNow);

            SaveDb();
            RefreshPostsList();
            _lblEditResult.text = "Set to pending.";
            LoadSelectedPostIntoEditor();
        }

        private void LoadSettingsIntoUI()
        {
            _ifSleepThreshold.value = Mathf.Clamp(_db.settings.sleepThresholdMinutes, 1, 999999);

            _tgDefaultAllowUsers.value = _db.settings.defaultAllowedMentions.allowUsers;
            _tgDefaultAllowRoles.value = _db.settings.defaultAllowedMentions.allowRoles;
            _tgDefaultAllowEveryone.value = _db.settings.defaultAllowedMentions.allowEveryone;
            _tfDefaultUserIds.value = _db.settings.defaultAllowedMentions.userIdsCsv ?? "";
            _tfDefaultRoleIds.value = _db.settings.defaultAllowedMentions.roleIdsCsv ?? "";

            _ddDefaultOffPolicy.index = Mathf.Clamp((int)_db.settings.defaultOffPolicy, 0, 2);
            _ddDefaultSleepPolicy.index = Mathf.Clamp((int)_db.settings.defaultSleepPolicy, 0, 2);

            _lblPaths.text = $"Data: {FileUtil.DataFolder}\nAttachments: {FileUtil.AttachmentsFolder}";
        }

        private void SaveSettingsFromUI()
        {
            _lblSettingsResult.text = "";

            _db.settings.sleepThresholdMinutes = Mathf.Clamp(_ifSleepThreshold.value, 1, 999999);

            _db.settings.defaultAllowedMentions.allowUsers = _tgDefaultAllowUsers.value;
            _db.settings.defaultAllowedMentions.allowRoles = _tgDefaultAllowRoles.value;
            _db.settings.defaultAllowedMentions.allowEveryone = _tgDefaultAllowEveryone.value;
            _db.settings.defaultAllowedMentions.userIdsCsv = _tfDefaultUserIds.value ?? "";
            _db.settings.defaultAllowedMentions.roleIdsCsv = _tfDefaultRoleIds.value ?? "";

            _db.settings.defaultOffPolicy = (MissedPolicy)Mathf.Clamp(_ddDefaultOffPolicy.index, 0, 2);
            _db.settings.defaultSleepPolicy = (MissedPolicy)Mathf.Clamp(_ddDefaultSleepPolicy.index, 0, 2);

            SaveDb();
            _lblSettingsResult.text = "Saved.";
        }

        private void RefreshAttachmentsDropdownNew()
        {
            string preferredPath = null;

            // avoid blocking IO during focus transitions / when app isn't focused.
            if (!_hasFocus)
            {
                RefreshAttachmentsDropdown(_ddMediaPick, MediaKind.None, (_tfMediaPath?.value ?? "").Trim());
                return;
            }

            // if the current media path is outside attachments folder, try to copy it in
            if (_tfMediaPath != null)
            {
                var current = (_tfMediaPath.value ?? "").Trim();
                if (!string.IsNullOrEmpty(current) &&
                    !current.StartsWith(FileUtil.AttachmentsFolder, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(current))
                {
                    var tempId = "temp-" + Guid.NewGuid().ToString("N");
                    if (FileUtil.TryCopyMediaToAttachments(current, tempId, MediaKind.None, out var newPath, out var copyErr))
                    {
                        _tfMediaPath.value = newPath;
                        preferredPath = newPath;
                        _lblMediaHint.text = "";
                        UpdateMediaPreviewNew();
                    }
                    else
                    {
                        _lblMediaHint.text = copyErr;
                    }
                }
                else
                {
                    preferredPath = current;
                }
            }

            RefreshAttachmentsDropdown(_ddMediaPick, MediaKind.None, preferredPath);
            UpdateMediaPreviewNew();
        }
        private void RefreshAttachmentsDropdownEdit()
        {
            // edit view: allow both image and video files to be selectable for preview
            RefreshAttachmentsDropdown(_ddEditAttachmentPick, MediaKind.None);
        }

        private void RefreshAttachmentsDropdown(DropdownField dd, MediaKind kindFilter, string preferredPath = null)
        {
            if (dd == null) return;

            FileUtil.EnsureFolders();

            var files = Directory.GetFiles(FileUtil.AttachmentsFolder)
                .Where(f =>
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (kindFilter == MediaKind.Video)
                        return ext == ".mp4" || ext == ".webm" || ext == ".mov";
                    if (kindFilter == MediaKind.Image)
                        return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".webp" || ext == ".gif";

                    // MediaKind.None -> both images and videos
                    return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".webp" || ext == ".gif" ||
                           ext == ".mp4" || ext == ".webm" || ext == ".mov";
                })
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .ToList();

            var labels = files.Select(Path.GetFileName).ToList();
            dd.choices = labels;

            if (!string.IsNullOrEmpty(preferredPath))
            {
                var name = Path.GetFileName(preferredPath);
                var idx = labels.IndexOf(name);
                dd.index = idx >= 0 ? idx : (labels.Count > 0 ? 0 : -1);
            }
            else
            {
                dd.index = labels.Count > 0 ? 0 : -1;
            }
        }

        private string GetSelectedAttachmentPath(DropdownField dd)
        {
            if (dd.index < 0 || dd.choices == null || dd.index >= dd.choices.Count) return null;
            var file = dd.choices[dd.index];
            if (string.IsNullOrWhiteSpace(file)) return null;
            return Path.Combine(FileUtil.AttachmentsFolder, file);
        }

        private void ApplyMissedPolicyAndMaybeEnqueue(ScheduledPost scheduledPost, bool isOffMissed, bool isSleepMissed)
        {
            // determine policy
            MissedPolicy policy;
            if (isOffMissed)
            {
                policy = scheduledPost.missedPolicyIfOff;
            }
            else
            {
                policy = scheduledPost.missedPolicyIfSleep;
            }

            if (policy == MissedPolicy.SendOnNextRun)
            {
                _log.Info($"Missed -> enqueue {scheduledPost.id} ({scheduledPost.title}) policy: SendOnNextRun off:{isOffMissed} sleep:{isSleepMissed}");
                EnqueueSend(scheduledPost);
                return; // early return: will enqueue
            }

            if (policy == MissedPolicy.MarkMissed)
            {
                scheduledPost.status = PostStatus.Missed;
                scheduledPost.updatedAtUtcIso = TimeUtil.ToIsoUtc(DateTime.UtcNow);
                _log.Warn($"Marked missed: {scheduledPost.id} ({scheduledPost.title})");
                SaveDb();
                RefreshPostsList();
                return; // early return: marked missed
            }

            if (policy == MissedPolicy.MarkFailed)
            {
                scheduledPost.status = PostStatus.Failed;
                scheduledPost.lastError = "Missed (policy: Failed).";
                scheduledPost.updatedAtUtcIso = TimeUtil.ToIsoUtc(DateTime.UtcNow);
                _log.Warn($"Marked failed (missed): {scheduledPost.id} ({scheduledPost.title})");
                SaveDb();
                RefreshPostsList();
                return;
            }
        }

        private void EnqueueSend(ScheduledPost p)
        {
            if (p == null) return;

            if (p.status == PostStatus.Sent) return;

            // if allowed mentions UI was hidden, optionally apply defaults if user hasn't set explicit values.
            // (we keep the stored values as-is; defaults are used at creation time already in ScheduledPost.)

            p.status = PostStatus.Sending;
            p.updatedAtUtcIso = TimeUtil.ToIsoUtc(DateTime.UtcNow);
            _log.Info($"Queue send: {p.id} ({p.title}) scheduled {p.scheduledAtUtcIso}");

            SaveDb();
            RefreshPostsList();

            _sendQueuePostIds.Enqueue(p.id);
        }

        private IEnumerator SendPostCoroutine(ScheduledPost post)
        {
            _isSending = true;
            var target = _db.targets.FirstOrDefault(t => t.id == post.targetId);

            if (target == null)
            {
                post.status = PostStatus.Failed;
                post.lastError = "Missing target (deleted?).";
                post.updatedAtUtcIso = TimeUtil.ToIsoUtc(DateTime.UtcNow);
                _log.Error($"Send failed: missing target for post {post.id}");
                SaveDb();
                RefreshPostsList();
                _isSending = false;
                yield break;
            }

            _log.Info($"Send start: {post.id} ({post.title}) -> {target.name}");
            bool done = false;
            bool ok = false;
            string err = "";

            yield return _webhook.Send(target, post, (success, error) =>
            {
                ok = success;
                err = error ?? "";
                done = true;
            });

            while (!done) yield return null;

            if (ok)
            {
                post.status = PostStatus.Sent;
                post.lastError = "";
                post.updatedAtUtcIso = TimeUtil.ToIsoUtc(DateTime.UtcNow);
                _log.Info($"Send ok: {post.id} -> {target.name}");
            }
            else
            {
                post.retries++;
                post.lastError = err;
                post.updatedAtUtcIso = TimeUtil.ToIsoUtc(DateTime.UtcNow);

                // retry policy: simple - mark failed after 10 attempts
                if (post.retries >= 10)
                    post.status = PostStatus.Failed;
                else
                    post.status = PostStatus.Pending;

                _log.Warn($"Send failed: {post.id} -> {target.name} (retry {post.retries}) err={err}");
            }

            SaveDb();
            RefreshPostsList();
            LoadSelectedPostIntoEditor();

            _isSending = false;
        }

        private void SaveDb()
        {
            _storage.Save(_db);
        }

        private void ShowView(VisualElement view)
        {
            if (_viewTargets != null) _viewTargets.style.display = DisplayStyle.None;
            if (_viewNew != null) _viewNew.style.display = DisplayStyle.None;
            if (_viewPosts != null) _viewPosts.style.display = DisplayStyle.None;
            if (_viewSettings != null) _viewSettings.style.display = DisplayStyle.None;
            if (_viewLog != null) _viewLog.style.display = DisplayStyle.None;

            if (view != null) view.style.display = DisplayStyle.Flex;
        }

        private void RefreshLog()
        {
            if (_logScroll == null) return;

            _logScroll.Clear();
            Label lastLabel = null;
            foreach (var line in _log.Snapshot())
            {
                var lbl = new Label(line);
                _logScroll.Add(lbl);
                lastLabel = lbl;
            }

            if (lastLabel != null)
            {
                _logScroll.ScrollTo(lastLabel);
            }
        }

        private void OpenFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                FileUtil.EnsureFolders();
                var fullPath = Path.GetFullPath(path);
                if (!Directory.Exists(fullPath))
                    Directory.CreateDirectory(fullPath);

            #if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = fullPath,
                    UseShellExecute = true,
                    Verb = "open"
                });

            #elif UNITY_STANDALONE_OSX

                System.Diagnostics.Process.Start("open", $"\"{fullPath}\"");

            #else

                System.Diagnostics.Process.Start("xdg-open", fullPath);

            #endif
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Failed to open the folder: " + exception.Message);
            }
        }

        private static string TryPickFileWindows(string filter)
        {
        #if UNITY_EDITOR

            var path = EditorUtility.OpenFilePanel("Select file", "", "*");
            return string.IsNullOrEmpty(path) ? null : path;

        #elif UNITY_STANDALONE_WIN

        // ignore the old WinForms "filter" string and use a native Win32 dialog instead.

        var nativeFilter = NativeFileDialogWin.BuildFilter(
            ("Images/Videos", "*.png;*.jpg;*.jpeg;*.webp;*.gif;*.mp4;*.webm;*.mov"),
            ("All files", "*.*"));

        return NativeFileDialogWin.OpenFile(nativeFilter);

        #else

            return null;

        #endif
        }

        private void ConfigureBodyField(TextField field, Label counter)
        {
            if (field == null) return;

            field.multiline = true;
            field.maxLength = BodyMaxChars;
            field.style.height = 240;
            field.style.minHeight = 200;

            void UpdateCounter()
            {
                if (counter != null)
                {
                    var len = (field.value ?? string.Empty).Length;
                    var remain = Mathf.Max(0, BodyMaxChars - len);
                    counter.text = $"{len} / {BodyMaxChars}  (remain: {remain})";
                }
            }

            field.RegisterValueChangedCallback(evt =>
            {
                var txt = evt.newValue ?? string.Empty;
                if (txt.Length > BodyMaxChars)
                {
                    field.SetValueWithoutNotify(txt.Substring(0, BodyMaxChars));
                }
                UpdateCounter();
            });

            UpdateCounter();
        }

        private void RefreshBodyCounter(TextField field, Label counter)
        {
            if (field == null || counter == null) return;
            var len = (field.value ?? string.Empty).Length;
            var remain = Mathf.Max(0, BodyMaxChars - len);
            counter.text = $"{len} / {BodyMaxChars}  (remain: {remain})";
        }

        private void HideLabelAndExpandInput(TextField field)
        {
            if (field == null) return;

            var lbl = field.labelElement;
            if (lbl != null) lbl.style.display = DisplayStyle.None;

            field.style.marginLeft = 0;
        }

        private void UpdateMediaPreviewNew()
        {
            var fromDropdown = GetSelectedAttachmentPath(_ddMediaPick);
            var path = string.IsNullOrWhiteSpace(fromDropdown)
                ? (_tfMediaPath?.value ?? string.Empty).Trim()
                : fromDropdown;

            UpdateMediaPreview(
                path,
                _imgMediaPreview,
                () => _previewTexNew,
                tex => _previewTexNew = tex,
                ref _previewVideoCoNew);
        }

        private void UpdateMediaPreviewEdit()
        {
            var fromDropdown = GetSelectedAttachmentPath(_ddEditAttachmentPick);
            var path = string.IsNullOrWhiteSpace(fromDropdown)
                ? (_tfEditImagePath?.value ?? string.Empty).Trim()
                : fromDropdown;

            UpdateMediaPreview(
                path,
                _imgEditPreview,
                () => _previewTexEdit,
                tex => _previewTexEdit = tex,
                ref _previewVideoCoEdit);
        }

        private void UpdateMediaPreview(string path, Image img, Func<Texture2D> getTex, Action<Texture2D> setTex, ref Coroutine videoCo)
        {
            if (img == null) return;

            if (videoCo != null)
            {
                StopCoroutine(videoCo);
                videoCo = null;
            }

            var currentTex = getTex != null ? getTex() : null;
            if (currentTex != null)
            {
                UnityEngine.Object.Destroy(currentTex);
                setTex?.Invoke(null);
            }

            img.image = null;
            img.style.display = DisplayStyle.None;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            if (FileUtil.IsAllowedImagePath(path))
            {
                try
                {
                    var bytes = File.ReadAllBytes(path);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (tex.LoadImage(bytes, false))
                    {
                        setTex?.Invoke(tex);
                        img.image = tex;
                        img.scaleMode = ScaleMode.ScaleToFit;
                        img.style.display = DisplayStyle.Flex;
                    }
                    else
                    {
                        UnityEngine.Object.Destroy(tex);
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("Failed to load the image preview: " + exception.Message);
                }
                return;
            }

            if (FileUtil.IsAllowedVideoPath(path))
            {
                img.style.display = DisplayStyle.Flex;

                videoCo = StartCoroutine(LoadVideoFrame(path, img, tex =>
                {
                    // Replace the stored preview texture atomically and destroy the previous one safely.
                    var prev = getTex != null ? getTex() : null;
                    setTex?.Invoke(tex);
                    if (prev != null) UnityEngine.Object.Destroy(prev);
                }));
            }
        }

        private IEnumerator LoadVideoFrame(string path, Image img, Action<Texture2D> setTex)
        {
            if (string.IsNullOrWhiteSpace(path) || img == null)
                yield break;

            if (!File.Exists(path))
                yield break;

            var videoPreviewGameObject = new GameObject("MediaPreviewVideo");
            var videoPlayer = videoPreviewGameObject.AddComponent<VideoPlayer>();
            videoPlayer.playOnAwake = false;
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            videoPlayer.source = VideoSource.Url;
            videoPlayer.url = new Uri(path).AbsoluteUri;
            videoPlayer.isLooping = false;
            videoPlayer.audioOutputMode = VideoAudioOutputMode.None;

            RenderTexture renderTexture = null;
            Texture2D tex = null;
            var prevActive = RenderTexture.active;

            try
            {
                videoPlayer.Prepare();

                float wait = 0f;
                const float prepareTimeout = 3f;

                while (!videoPlayer.isPrepared && wait < prepareTimeout)
                {
                    if (!Application.isFocused)
                        yield break;

                    wait += Time.unscaledDeltaTime;
                    yield return null;
                }

                if (!videoPlayer.isPrepared)
                {
                    Debug.LogWarning($"Video preview: prepare failed or timed out ({path})");
                    yield break;
                }

                int w = Mathf.Max(2, videoPlayer.width > 0 ? (int)videoPlayer.width : 320);
                int h = Mathf.Max(2, videoPlayer.height > 0 ? (int)videoPlayer.height : 180);

                renderTexture = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
                videoPlayer.targetTexture = renderTexture;

                videoPlayer.Play();

                float frameWait = 0f;
                const float firstFrameTimeout = 1.0f;

                while (videoPlayer.isPlaying && videoPlayer.frame <= 0 && frameWait < firstFrameTimeout)
                {
                    if (!Application.isFocused)
                        yield break;

                    frameWait += Time.unscaledDeltaTime;
                    yield return null;
                }

                if (!Application.isFocused)
                    yield break;

                videoPlayer.Pause();
                yield return new WaitForEndOfFrame();

                try
                {
                    tex = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.ARGB32, false);

                    prevActive = RenderTexture.active;
                    RenderTexture.active = renderTexture;

                    tex.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
                    tex.Apply(false, false);
                }
                catch (Exception e)
                {
                    if (tex != null)
                    {
                        UnityEngine.Object.Destroy(tex);
                        tex = null;
                    }

                    Debug.LogWarning("Video preview: failed to capture frame: " + e.Message);
                    yield break;
                }
                finally
                {
                    RenderTexture.active = prevActive;
                }

                setTex?.Invoke(tex);
                img.image = tex;
                img.scaleMode = ScaleMode.ScaleToFit;

                tex = null;
            }
            finally
            {
                if (tex != null)
                    UnityEngine.Object.Destroy(tex);

                RenderTexture.active = prevActive;
                CleanupVideoPreview(videoPlayer, renderTexture, videoPreviewGameObject);
            }
        }

        private static void CleanupVideoPreview(VideoPlayer videoPlayer, RenderTexture renderTexture, GameObject videoPreviewGameObject)
        {
            if (videoPlayer != null) videoPlayer.targetTexture = null;

            if (renderTexture != null)
            {
                renderTexture.Release();
                UnityEngine.Object.Destroy(renderTexture);
            }

            if (videoPreviewGameObject != null) UnityEngine.Object.Destroy(videoPreviewGameObject);
        }

        private void ConfigurePostModeRadios(Toggle normal, Toggle embed)
        {
            if (normal == null || embed == null) return;

            normal.value = true;
            normal.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue) embed.value = false;
                else if (!embed.value) normal.value = true; // keep one selected
            });

            embed.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue) normal.value = false;
                else if (!normal.value) embed.value = true; // keep one selected
            });
        }

        private void SetRadio(Toggle normal, Toggle embed, bool sendAsEmbed)
        {
            if (normal == null || embed == null) return;
            embed.value = sendAsEmbed;
            normal.value = !sendAsEmbed;
        }

        private void SortPostsByDate()
        {
            if (_db?.posts == null) return; // Guard: no posts to sort

            _db.posts.Sort((left, right) =>
            {
                var leftUtc = TimeUtil.ParseIsoUtc(left.scheduledAtUtcIso);
                var rightUtc = TimeUtil.ParseIsoUtc(right.scheduledAtUtcIso);
                return DateTime.Compare(leftUtc, rightUtc);
            });
        }

        private void BindOpenFolderButtons(string buttonName, string path)
        {
            if (_root == null || string.IsNullOrWhiteSpace(buttonName)) return;

            foreach (var button in _root.Query<Button>(buttonName).Build())
                button.clicked += () => OpenFolder(path);
        }
    }
}
