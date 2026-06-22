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
        private WebhookUrlValidator _webhookUrlValidator;
        private IWebhookSecretResolver _webhookSecretResolver;
        private SchedulerService _scheduler;
        private SchedulePolicyService _schedulePolicy;
        private ITimeProvider _timeProvider;
        private PostStateMachine _postStateMachine;
        private AppLifecycleService _lifecycle;
        private SendQueueService _sendQueue;
        private TargetRevisionPolicy _targetRevisionPolicy;
        private MutationGuardService _mutationGuard;
        private ReviewQueueService _reviewQueueService;
        private PayloadTextNormalizer _payloadTextNormalizer;
        private PayloadPreviewService _payloadPreviewService;
        private PayloadPreviewPresenter _payloadPreviewPresenter;
        private QueueHealthPresenter _queueHealthPresenter;
        private RateLimitRegistry _rateLimitRegistry;
        private OperationalHealthService _operationalHealth;
        private PostFormValidator _postFormValidator;
        private PostMediaService _postMediaService;
        private PostApplicationService _postApp;
        private TargetApplicationService _targetApp;
        private SettingsApplicationService _settingsApp;
        private ISendAttemptJournal _sendAttemptJournal;
        private StorageLease _storageLease;
        private bool _storageWriteBlocked;
        private string _lastSaveWarning = "";
        private string _lastJournalWarning = "";
        private string _lastLockWarning = "";
        private string _lastRetentionWarning = "";

        private AppDatabase _db;

        private UIDocument _ui;
        private VisualElement _root;

        private Label _lblStatus;
        private Label _lblHealthState, _lblNavPending, _lblNavQueue, _lblNavReview;
        private Label _lblMetricTargets, _lblMetricPending, _lblMetricQueue, _lblMetricReview, _lblMetricWarnings;
        private Label _lblDashboardSummary, _lblQueueHealthSummary, _lblNextPost, _lblSafetySummary;
        private Label _lblReviewSummary, _lblReviewEmpty;
        private Label _lblReviewSelectedEvidence;
        private ListView _reviewList;
        private readonly List<ScheduledPost> _reviewPosts = new List<ScheduledPost>();
        private Button _btnReviewOpenPosts, _btnReviewRetry, _btnReviewDismiss;

        private Button _btnDashboard, _btnTargets, _btnNewPost, _btnPosts, _btnReview, _btnSettings, _btnLog;

        private VisualElement _viewDashboard, _viewTargets, _viewNew, _viewPosts, _viewReview, _viewSettings, _viewLog;

        private DropdownField _ddPostTarget;
        private readonly List<string> _postTargetIds = new List<string>();
        private TextField _tfPostTitle, _tfPostBody, _tfDate, _tfTime, _tfImagePath;
        private DropdownField _ddAttachmentPick;
        private Toggle _tgPostModeNormal, _tgPostModeEmbed;
        private Toggle _tgAllowUsers, _tgAllowRoles, _tgAllowEveryone;
        private TextField _tfMentionUserIds, _tfMentionRoleIds;
        private DropdownField _ddOffPolicy, _ddSleepPolicy;
        private Label _lblScheduleResult, _lblImageHint, _lblPostBodyCounter;
        private Label _lblPayloadPreviewHeadline, _lblPayloadPreviewDigest, _lblPayloadPreviewDetails, _lblPayloadPreviewPayload;
        private PayloadPreview _lastCreatePayloadPreview;

        private Toggle _tgAttachMedia;
        private VisualElement _mediaOptionsNew;
        private Toggle _tgMediaIsImage;
        private Toggle _tgMediaIsVideo;
        private TextField _tfMediaPath;
        private VisualElement _mediaButtonsNew;
        private DropdownField _ddMediaPick;
        private Label _lblMediaHint;
        private Image _imgMediaPreview;
        private VisualElement _mediaPreviewNew;
        private Texture2D _previewTexNew;
        private Coroutine _previewVideoCoNew;

        private ListView _postsList;
        private DropdownField _ddEditTarget, _ddEditAttachmentPick;
        private readonly List<string> _editTargetIds = new List<string>();
        private TextField _tfEditTitle, _tfEditBody, _tfEditDate, _tfEditTime, _tfEditImagePath;
        private Toggle _tgEditModeNormal, _tgEditModeEmbed;
        private Toggle _tgEditAllowUsers, _tgEditAllowRoles, _tgEditAllowEveryone;
        private TextField _tfEditMentionUserIds, _tfEditMentionRoleIds;
        private DropdownField _ddEditOffPolicy, _ddEditSleepPolicy;
        private Label _lblPostsEmpty, _lblPostSelectionHint;
        private Label _lblEditStatus, _lblEditResult, _lblEditBodyCounter;
        private Image _imgEditPreview;
        private Texture2D _previewTexEdit;
        private Coroutine _previewVideoCoEdit;

        private SettingsPanelController _settingsPanel;
        private LogPanelController _logPanel;
        private TargetPanelController _targetPanel;
        private PostCreatePanelController _postCreatePanel;
        private PostEditPanelController _postEditPanel;
        private DateTimePickerController _dateTimePicker;
        private MediaPreviewController _mediaPreview;

        private VisualElement _modalOverlay;
        private VisualElement _modalCard;
        private Label _modalTitle;
        private VisualElement _modalBody;
        private Button _btnModalClose;
        private Action _modalOnClose;

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
            _timeProvider = new SystemTimeProvider();
            _postStateMachine = new PostStateMachine(_timeProvider);
            _lifecycle = new AppLifecycleService(_postStateMachine);
            _sendQueue = new SendQueueService(_postStateMachine);
            _targetRevisionPolicy = new TargetRevisionPolicy();
            _mutationGuard = new MutationGuardService(_sendQueue, _targetRevisionPolicy);
            _payloadTextNormalizer = new PayloadTextNormalizer();
            FileUtil.EnsureFolders();
            _webhookSecretResolver = new WebhookSecretResolver(new DpapiSecretStore(Path.Combine(FileUtil.DataFolder, "secrets")));
            _payloadPreviewService = new PayloadPreviewService(
                textNormalizer: _payloadTextNormalizer,
                targetRevisionPolicy: _targetRevisionPolicy,
                webhookSecretResolver: _webhookSecretResolver);
            _payloadPreviewPresenter = new PayloadPreviewPresenter();
            _queueHealthPresenter = new QueueHealthPresenter();
            _rateLimitRegistry = new RateLimitRegistry();
            _operationalHealth = new OperationalHealthService();
            _storageLease = new AppInstanceLock(_timeProvider).TryAcquire(FileUtil.DataFolder);
            _storageWriteBlocked = _storageLease == null || !_storageLease.IsAcquired;
            if (_storageWriteBlocked)
            {
                _lastLockWarning = _storageLease?.result?.message ?? "Storage lock failed.";
                _log.Error(_lastLockWarning);
            }
            else if (_storageLease.result != null && _storageLease.result.staleRecovered)
            {
                _log.Warn("Recovered stale storage lock.");
            }

            _storage = new StorageService(_log);
            _settingsApp = new SettingsApplicationService();
            _settingsPanel = new SettingsPanelController(
                _settingsApp,
                () => _db,
                SaveDb,
                PolicyLabels,
                BuildHealthSnapshot,
                () => _log != null ? _log.Snapshot() : new List<string>());
            _logPanel = new LogPanelController(_log);
            _db = _storageWriteBlocked ? new AppDatabase() : _storage.LoadOrCreate();
            EnsureSettingsDefaults();

            _webhookUrlValidator = new WebhookUrlValidator();
            _reviewQueueService = new ReviewQueueService(_webhookUrlValidator, _webhookSecretResolver);
            _targetApp = new TargetApplicationService(_webhookUrlValidator, _sendQueue, _mutationGuard);
            _webhook = new DiscordWebhookClient(_log, new DiscordWebhookHttpTransport(), new WebhookRequestFactory(), _webhookSecretResolver);
            _targetPanel = new TargetPanelController(
                _targetApp,
                _webhookUrlValidator,
                _webhookSecretResolver,
                _webhook,
                _log,
                () => _db,
                SaveDb,
                RefreshTargetsDropdowns,
                RefreshPostsList,
                routine => StartCoroutine(routine));
            _scheduler = new SchedulerService(_db, _log);
            _schedulePolicy = new SchedulePolicyService();
            _postFormValidator = new PostFormValidator(new DiscordPayloadValidator(_payloadTextNormalizer));
            _postMediaService = new PostMediaService();
            _postApp = new PostApplicationService(_postFormValidator, _postMediaService, _timeProvider, _mutationGuard);
            _dateTimePicker = new DateTimePickerController(
                ShowDatePicker,
                ShowTimePicker,
                () => _modalOverlay != null && _modalOverlay.resolvedStyle.display != DisplayStyle.None);
            _mediaPreview = new MediaPreviewController(
                UpdateMediaPreviewNew,
                UpdateMediaPreviewEdit,
                CleanupAllPreviewResources);
            _postCreatePanel = new PostCreatePanelController(
                ScheduleNewPost,
                RefreshAttachmentsDropdownNew,
                _mediaPreview.UpdateNew,
                () => OpenFolder(FileUtil.AttachmentsFolder),
                TryPickFileWindows,
                GetSelectedAttachmentPath);
            _postEditPanel = new PostEditPanelController(
                SaveEditedPost,
                DeleteSelectedPost,
                ForceSendSelectedPost,
                DeleteSentPosts,
                MarkSelectedPostPending,
                () => OpenFolder(FileUtil.AttachmentsFolder),
                RefreshAttachmentsDropdownEdit,
                _mediaPreview.UpdateEdit,
                GetSelectedAttachmentPath);

            _sendAttemptJournal = new SendAttemptJournal(FileUtil.DataFolder);

            RecoverStaleSendingPosts();

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
            _mediaPreview?.Cleanup();
        }

        private void OnDestroy()
        {
            _mediaPreview?.Cleanup();
            _storageLease?.Dispose();
        }

        private void OnApplicationQuit()
        {
            MarkActiveSendAmbiguousOnShutdown();
            _storageLease?.Dispose();
        }

        private void CleanupAllPreviewResources()
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
            _settingsApp?.Normalize(_db);
        }

        private void RecoverStaleSendingPosts()
        {
            if (_db?.posts == null || _lifecycle == null)
                return;

            int recovered = _lifecycle.RecoverStaleSendingPosts(_db, "Recovered stale Sending state on startup; manual review required.");

            if (recovered <= 0)
                return;

            _log.Warn($"Recovered {recovered} stale Sending post(s) to NeedsReview.");
            SaveDb();
        }

        private void Start()
        {
            if (!BindUI())
                return;

            RefreshAllUI();

            // startup missed handling (off/app not running)
            _scheduler.OnStartupHandleMissed(HandleDuePost);

            if (!_storageWriteBlocked)
                SaveDb();
            else if (_lblStatus != null)
                _lblStatus.text = "Storage locked by another app instance. Writes and sends are blocked.";

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

            if (_storageWriteBlocked)
                return;

            _scheduler.Tick(HandleDuePost);

            if (_sendQueue.TryDequeueNext(_db, out var queuedPost))
            {
                var canStart = _lifecycle.CanStartSend();
                if (!canStart.ok)
                {
                    _postStateMachine.RecoverStaleSending(queuedPost, canStart.error);
                    SaveDb();
                    RefreshPostsList();
                    return;
                }

                if (!TryApplyRateLimitGuard(queuedPost))
                    return;

                _sendQueue.MarkActive(queuedPost.id);
                StartCoroutine(SendPostCoroutine(queuedPost));
            }
        }

        private bool BindUI()
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
                return false;
            }

            _root = _ui.rootVisualElement;
            if (!AuditRequiredUiBindings())
            {
                enabled = false;
                return false;
            }

            // navigation
            _btnDashboard = _root.Q<Button>("btnDashboard");
            _btnTargets = _root.Q<Button>("btnTargets");
            _btnNewPost = _root.Q<Button>("btnNewPost");
            _btnPosts = _root.Q<Button>("btnPosts");
            _btnReview = _root.Q<Button>("btnReview");
            _btnSettings = _root.Q<Button>("btnSettings");
            _btnLog = _root.Q<Button>("btnLog");

            _lblStatus = _root.Q<Label>("lblStatus");
            _lblHealthState = _root.Q<Label>("lblHealthState");
            _lblNavPending = _root.Q<Label>("lblNavPending");
            _lblNavQueue = _root.Q<Label>("lblNavQueue");
            _lblNavReview = _root.Q<Label>("lblNavReview");

            _lblMetricTargets = _root.Q<Label>("lblMetricTargets");
            _lblMetricPending = _root.Q<Label>("lblMetricPending");
            _lblMetricQueue = _root.Q<Label>("lblMetricQueue");
            _lblMetricReview = _root.Q<Label>("lblMetricReview");
            _lblMetricWarnings = _root.Q<Label>("lblMetricWarnings");
            _lblDashboardSummary = _root.Q<Label>("lblDashboardSummary");
            _lblQueueHealthSummary = _root.Q<Label>("lblQueueHealthSummary");
            _lblNextPost = _root.Q<Label>("lblNextPost");
            _lblSafetySummary = _root.Q<Label>("lblSafetySummary");
            _lblReviewSummary = _root.Q<Label>("lblReviewSummary");
            _lblReviewEmpty = _root.Q<Label>("lblReviewEmpty");
            _lblReviewSelectedEvidence = _root.Q<Label>("lblReviewSelectedEvidence");

            // views
            _viewDashboard = _root.Q<VisualElement>("viewDashboard");
            _viewTargets = _root.Q<VisualElement>("viewTargets");
            _viewNew = _root.Q<VisualElement>("viewNewPost");
            _viewPosts = _root.Q<VisualElement>("viewPosts");
            _viewReview = _root.Q<VisualElement>("viewReview");
            _viewSettings = _root.Q<VisualElement>("viewSettings");
            _viewLog = _root.Q<VisualElement>("viewLog");

            BindNavigation();

            _targetPanel.Bind(_root);

            // new post refs
            _ddPostTarget = _root.Q<DropdownField>("ddPostTarget");
            _tfPostTitle = _root.Q<TextField>("tfPostTitle");
            _tfPostBody = _root.Q<TextField>("tfPostBody");
            _tfDate = _root.Q<TextField>("tfDate");
            _tfTime = _root.Q<TextField>("tfTime");

            // pickers instead of manual typing
            _dateTimePicker.BindDateField(_tfDate);
            _dateTimePicker.BindTimeField(_tfTime);

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
            _lblPayloadPreviewHeadline = _root.Q<Label>("lblPayloadPreviewHeadline");
            _lblPayloadPreviewDigest = _root.Q<Label>("lblPayloadPreviewDigest");
            _lblPayloadPreviewDetails = _root.Q<Label>("lblPayloadPreviewDetails");
            _lblPayloadPreviewPayload = _root.Q<Label>("lblPayloadPreviewPayload");

            // media (new post)
            _tgAttachMedia = _root.Q<Toggle>("tgAttachMedia");
            _mediaOptionsNew = _root.Q<VisualElement>("mediaOptionsNew");
            _tgMediaIsImage = _root.Q<Toggle>("tgMediaIsImage");
            _tgMediaIsVideo = _root.Q<Toggle>("tgMediaIsVideo");
            _tfMediaPath = _root.Q<TextField>("tfMediaPath");
            _mediaButtonsNew = _root.Q<VisualElement>("mediaButtonsNew");
            _ddMediaPick = _root.Q<DropdownField>("ddMediaPick");
            _lblMediaHint = _root.Q<Label>("lblMediaHint");
            _imgMediaPreview = _root.Q<Image>("imgMediaPreview");
            _mediaPreviewNew = _root.Q<VisualElement>("mediaPreviewNew");

            _postCreatePanel.Bind(_root);

            // posts refs
            _postsList = _root.Q<ListView>("postsList");
            _lblPostsEmpty = _root.Q<Label>("lblPostsEmpty");
            _lblPostSelectionHint = _root.Q<Label>("lblPostSelectionHint");
            _ddEditTarget = _root.Q<DropdownField>("ddEditTarget");
            _tfEditTitle = _root.Q<TextField>("tfEditTitle");
            _tfEditBody = _root.Q<TextField>("tfEditBody");
            _tfEditDate = _root.Q<TextField>("tfEditDate");
            _tfEditTime = _root.Q<TextField>("tfEditTime");
            _dateTimePicker.BindDateField(_tfEditDate);
            _dateTimePicker.BindTimeField(_tfEditTime);

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

            _postEditPanel.Bind(_root);
            _root.Q<Button>("btnOpenDataFolder").clicked += () => OpenFolder(FileUtil.DataFolder);

            _settingsPanel.Bind(_root);
            _logPanel.Bind(_root);

            SetupListViews();
            SetupDropdowns();
            BindCreatePayloadPreview();

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

            ShowView(_viewDashboard);
            RenderCreatePayloadPreview(null);
            return true;
        }

        private void BindNavigation()
        {
            if (_btnDashboard != null)
                _btnDashboard.clicked += () => ShowView(_viewDashboard);

            if (_btnTargets != null)
                _btnTargets.clicked += () => ShowView(_viewTargets);

            if (_btnNewPost != null)
            {
                _btnNewPost.clicked += () =>
                {
                    ShowView(_viewNew);
                    RefreshAttachmentsDropdownNew();
                    _mediaPreview.UpdateNew();
                };
            }

            if (_btnPosts != null)
            {
                _btnPosts.clicked += () =>
                {
                    ShowView(_viewPosts);
                    RefreshPostsList();
                    RefreshAttachmentsDropdownEdit();
                    LoadSelectedPostIntoEditor();
                };
            }

            if (_btnReview != null)
            {
                _btnReview.clicked += () =>
                {
                    RefreshReviewList();
                    ShowView(_viewReview);
                };
            }

            if (_btnSettings != null)
            {
                _btnSettings.clicked += () =>
                {
                    ShowView(_viewSettings);
                    _settingsPanel.Refresh();
                };
            }

            if (_btnLog != null)
                _btnLog.clicked += () => { ShowView(_viewLog); _logPanel.Refresh(); };

            var btnDashboardNewPost = _root.Q<Button>("btnDashboardNewPost");
            if (btnDashboardNewPost != null)
                btnDashboardNewPost.clicked += () =>
                {
                    ShowView(_viewNew);
                    RefreshAttachmentsDropdownNew();
                    _mediaPreview.UpdateNew();
                    UpdateCreatePayloadPreviewStaleState();
                };

            var btnDashboardTargets = _root.Q<Button>("btnDashboardTargets");
            if (btnDashboardTargets != null)
                btnDashboardTargets.clicked += () => ShowView(_viewTargets);

            var btnDashboardReview = _root.Q<Button>("btnDashboardReview");
            if (btnDashboardReview != null)
                btnDashboardReview.clicked += () =>
                {
                    RefreshReviewList();
                    ShowView(_viewReview);
                };

            var btnReviewRefresh = _root.Q<Button>("btnReviewRefresh");
            if (btnReviewRefresh != null)
                btnReviewRefresh.clicked += RefreshReviewList;

            _btnReviewOpenPosts = _root.Q<Button>("btnReviewOpenPosts");
            if (_btnReviewOpenPosts != null)
                _btnReviewOpenPosts.clicked += OpenSelectedReviewPostInPosts;

            _btnReviewRetry = _root.Q<Button>("btnReviewRetry");
            if (_btnReviewRetry != null)
                _btnReviewRetry.clicked += RetrySelectedReviewPost;

            _btnReviewDismiss = _root.Q<Button>("btnReviewDismiss");
            if (_btnReviewDismiss != null)
                _btnReviewDismiss.clicked += DismissSelectedReviewPost;

            SetReviewActionButtonsEnabled(false);
        }

        private bool AuditRequiredUiBindings()
        {
            if (_root == null)
                return false;

            var report = new UiBindingAudit().Run(_root);
            if (report.ok)
            {
                var warnings = report.WarningSummaries();
                for (int i = 0; i < warnings.Count; i++)
                    _log?.Warn("UI binding audit warning: " + warnings[i]);

                return true;
            }

            var message = "UI binding audit failed. Required UXML control issue(s): " + string.Join(", ", report.BlockingSummaries());
            Debug.LogError(message);
            _log?.Error(message);
            return false;
        }

        private T RequireUi<T>(string name, List<string> missing) where T : VisualElement
        {
            var element = _root.Q<T>(name);
            if (element == null)
                missing.Add($"{typeof(T).Name}:{name}");

            return element;
        }

        private void SetupDropdowns()
        {
            _ddOffPolicy.choices = PolicyLabels;
            _ddSleepPolicy.choices = PolicyLabels;
            _ddEditOffPolicy.choices = PolicyLabels;
            _ddEditSleepPolicy.choices = PolicyLabels;
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

            foreach (var controlName in hideNames)
            {
                var visualElement = _root?.Q<VisualElement>(controlName);
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
            catch (Exception exception) { Debug.LogWarning("Modal onClose error: " + exception.Message); }

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
                for (int dayOfMonth = 1; dayOfMonth <= daysInMonth; dayOfMonth++)
                {
                    var date = new DateTime(shown.Year, shown.Month, dayOfMonth);

                    var dayButton = new Button(() => onSelected?.Invoke(date)) { text = dayOfMonth.ToString() };
                    dayButton.style.width = cellW;
                    dayButton.style.height = cellH;

                    if (date.Date == current.Date)
                        dayButton.style.backgroundColor = new Color(0.20f, 0.40f, 0.85f, 1f);

                    grid.Add(dayButton);
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

            var timeText = (targetField?.value ?? "").Trim();
            if (DateTime.TryParseExact(timeText, "HH:mm", System.Globalization.CultureInfo.InvariantCulture,
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
                for (int hourIndex = 0; hourIndex <= 23; hourIndex++)
                {
                    int hourValue = hourIndex;
                    var b = new Button(() =>
                    {
                        selHour = hourValue;
                        preview.text = $"{selHour:00}:{selMin:00}";
                        RebuildHours();
                    })
                    { text = hourValue.ToString("00") };

                    b.style.width = 64;
                    b.style.height = 30;

                    if (hourValue == selHour)
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
                for (int minuteIndex = 0; minuteIndex <= 55; minuteIndex += 5)
                {
                    int minuteValue = minuteIndex;
                    var minuteButton = new Button(() =>
                    {
                        selMin = minuteValue;
                        preview.text = $"{selHour:00}:{selMin:00}";
                        RebuildMinutes();
                    })
                    { text = minuteValue.ToString("00") };

                    minuteButton.style.width = 52;
                    minuteButton.style.height = 30;

                    if (minuteValue == selMin && (selMin % 5 == 0))
                        minuteButton.style.backgroundColor = new Color(0.20f, 0.40f, 0.85f, 1f);

                    minsQuick.Add(minuteButton);
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
            // posts listview
            _postsList.makeItem = CreatePostRow;
            _postsList.bindItem = (e, i) =>
            {
                if (i < 0 || _db?.posts == null || i >= _db.posts.Count) return;
                BindPostRow(e, _db.posts[i]);
            };
            _postsList.selectionType = SelectionType.Single;
            _postsList.selectionChanged += _ => LoadSelectedPostIntoEditor();

            _reviewList = _root.Q<ListView>("reviewList");
            if (_reviewList != null)
            {
                _reviewList.makeItem = CreatePostRow;
                _reviewList.bindItem = (e, i) =>
                {
                    if (i < 0 || i >= _reviewPosts.Count) return;
                    BindPostRow(e, _reviewPosts[i]);
                };
                _reviewList.selectionType = SelectionType.Single;
                _reviewList.selectionChanged += _ => RefreshReviewSelectionDetails();
            }
        }

        private static VisualElement CreatePostRow()
        {
            var row = new VisualElement();
            row.AddToClassList("postRow");

            var status = new Label { name = "rowStatus", text = "Status" };
            status.AddToClassList("statusPill");
            row.Add(status);

            var textStack = new VisualElement { name = "rowText" };
            textStack.AddToClassList("postRowText");

            var title = new Label { name = "rowTitle", text = "Post" };
            title.AddToClassList("postRowTitle");
            textStack.Add(title);

            var meta = new Label { name = "rowMeta", text = "" };
            meta.AddToClassList("postRowMeta");
            textStack.Add(meta);

            row.Add(textStack);
            return row;
        }

        private void BindPostRow(VisualElement row, ScheduledPost post)
        {
            if (row == null || post == null)
                return;

            var status = row.Q<Label>("rowStatus");
            var title = row.Q<Label>("rowTitle");
            var meta = row.Q<Label>("rowMeta");

            if (status != null)
            {
                status.text = post.status.ToString();
                ApplyPostStatusClass(status, post.status);
            }

            if (title != null)
                title.text = BuildPostTitle(post);

            if (meta != null)
                meta.text = BuildPostMeta(post);
        }

        private void RefreshAllUI()
        {
            RefreshTargetsDropdowns();
            RefreshTargetsList();
            RefreshPostsList();
            RefreshAttachmentsDropdownNew();
            RefreshAttachmentsDropdownEdit();
            _settingsPanel.Refresh();
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

            ShowView(_viewDashboard);
        }

        private void RefreshTargetsList()
        {
            _targetPanel.RefreshList();
        }

        private void RefreshPostsList()
        {
            var selectedId = (_postsList?.selectedItem as ScheduledPost)?.id ?? "";
            SortPostsByDate(); // ensure list is date-ordered
            var hasPosts = _db?.posts != null && _db.posts.Count > 0;
            _postsList.itemsSource = _db.posts;
            _postsList.Rebuild();
            _postsList.style.display = hasPosts ? DisplayStyle.Flex : DisplayStyle.None;
            if (_lblPostsEmpty != null)
                _lblPostsEmpty.style.display = hasPosts ? DisplayStyle.None : DisplayStyle.Flex;
            if (!hasPosts)
                SetPostEditActionButtonsEnabled(false);
            RestoreSelectedPost(selectedId);
            if (hasPosts && _postsList != null && _postsList.selectedIndex < 0)
                _postsList.selectedIndex = 0;
            RefreshReviewList();
            RefreshHealthUi();
        }

        private void RestoreSelectedPost(string postId)
        {
            if (_postsList == null || string.IsNullOrWhiteSpace(postId) || _db?.posts == null)
                return;

            var index = _db.posts.FindIndex(post => post != null && post.id == postId);
            if (index >= 0)
                _postsList.selectedIndex = index;
        }

        private void RefreshReviewList()
        {
            var selectedId = (_reviewList?.selectedItem as ScheduledPost)?.id ?? "";
            _reviewPosts.Clear();

            if (_db?.posts != null)
            {
                _reviewPosts.AddRange(_db.posts
                    .Where(post => post != null && post.status == PostStatus.NeedsReview)
                    .OrderBy(post => post.lastAttemptAtUtcIso)
                    .ThenBy(post => post.scheduledAtUtcIso));
            }

            if (_reviewList != null)
            {
                _reviewList.itemsSource = _reviewPosts;
                _reviewList.Rebuild();
                _reviewList.style.display = _reviewPosts.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                _reviewList.selectedIndex = FindReviewPostIndex(selectedId);
            }

            if (_lblReviewEmpty != null)
                _lblReviewEmpty.style.display = _reviewPosts.Count > 0 ? DisplayStyle.None : DisplayStyle.Flex;

            SetText(_lblReviewSummary, _reviewPosts.Count == 0
                ? "No posts need manual review."
                : $"{_reviewPosts.Count} post(s) need manual review. Discord may already have received an ambiguous send.");

            RefreshReviewSelectionDetails();
            RefreshHealthUi();
        }

        private void RefreshReviewSelectionDetails()
        {
            var post = GetSelectedReviewPost();
            if (post == null)
            {
                SetReviewActionButtonsEnabled(false);
                SetText(_lblReviewSelectedEvidence, "Select a NeedsReview post to inspect retry evidence.");
                return;
            }

            var item = _reviewQueueService.Build(_db, null, _sendQueue)
                .FirstOrDefault(reviewItem => string.Equals(reviewItem.postId, post.id, StringComparison.Ordinal));

            if (item == null)
            {
                SetReviewActionButtonsEnabled(false);
                SetText(_lblReviewSelectedEvidence, "Selected post is no longer in NeedsReview.");
                return;
            }

            SetReviewActionButtonsEnabled(true);
            var retry = item.retryAllowed ? "Retry allowed" : "Retry blocked: " + item.retryBlockedReason;
            var messageId = string.IsNullOrWhiteSpace(item.evidence.lastDiscordMessageId)
                ? "No Discord message id"
                : "Message id: " + item.evidence.lastDiscordMessageId;
            SetText(_lblReviewSelectedEvidence, $"{retry}. Attempts: {item.evidence.retries}. {messageId}. Reason: {item.reason}");
        }

        private ScheduledPost GetSelectedReviewPost()
        {
            var post = _reviewList?.selectedItem as ScheduledPost;
            return post;
        }

        private int FindReviewPostIndex(string postId)
        {
            if (string.IsNullOrWhiteSpace(postId))
                return -1;

            return _reviewPosts.FindIndex(post => post != null && post.id == postId);
        }

        private void SetReviewActionButtonsEnabled(bool enabled)
        {
            UiActionState.SetEnabled(_btnReviewOpenPosts, enabled);
            UiActionState.SetEnabled(_btnReviewRetry, enabled);
            UiActionState.SetEnabled(_btnReviewDismiss, enabled);
        }

        private void RetrySelectedReviewPost()
        {
            var post = GetSelectedReviewPost();
            var plan = _reviewQueueService.EvaluateAction(_db, post?.id, ReviewAction.Retry, _sendQueue);
            if (!plan.allowed)
            {
                SetText(_lblReviewSelectedEvidence, plan.message);
                return;
            }

            ShowReviewDecisionModal(
                "Retry selected post",
                plan.message + " Discord may already have received the previous ambiguous send.",
                () =>
                {
                    var selected = _db.posts.FirstOrDefault(item => item != null && string.Equals(item.id, plan.postId, StringComparison.Ordinal));
                    var pending = _postStateMachine.TryMarkPendingManual(selected, resetRetries: true);
                    if (!pending.ok)
                    {
                        SetText(_lblReviewSelectedEvidence, pending.error);
                        return;
                    }

                    var save = SaveDb();
                    if (!save.ok)
                    {
                        SetText(_lblReviewSelectedEvidence, save.error);
                        return;
                    }

                    var queued = EnqueueSend(selected);
                    SetText(_lblReviewSelectedEvidence, queued ? "Retry queued." : "Retry could not be queued. Check queue/backoff status.");
                    RefreshPostsList();
                });
        }

        private void DismissSelectedReviewPost()
        {
            var post = GetSelectedReviewPost();
            var plan = _reviewQueueService.EvaluateAction(_db, post?.id, ReviewAction.Dismiss, _sendQueue);
            if (!plan.allowed)
            {
                SetText(_lblReviewSelectedEvidence, plan.message);
                return;
            }

            ShowReviewDecisionModal(
                "Dismiss selected review",
                "Dismiss keeps the post record and marks it failed. It will not retry automatically.",
                () =>
                {
                    var selected = _db.posts.FirstOrDefault(item => item != null && string.Equals(item.id, plan.postId, StringComparison.Ordinal));
                    var dismiss = _postStateMachine.DismissNeedsReview(selected, "Dismissed from review by user.");
                    if (!dismiss.ok)
                    {
                        SetText(_lblReviewSelectedEvidence, dismiss.error);
                        return;
                    }

                    var save = SaveDb();
                    SetText(_lblReviewSelectedEvidence, save.ok ? "Review dismissed." : save.error);
                    RefreshPostsList();
                });
        }

        private void ShowReviewDecisionModal(string title, string message, Action onConfirm)
        {
            EnsureModalUi();
            var body = new VisualElement();
            body.Add(new Label(message ?? ""));

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.FlexEnd;
            row.style.marginTop = 12;

            var cancel = new Button(HideModal) { text = "Cancel" };
            cancel.style.marginRight = 8;
            var confirm = new Button(() =>
            {
                HideModal();
                onConfirm?.Invoke();
            }) { text = "Confirm" };
            confirm.AddToClassList("primary");

            row.Add(cancel);
            row.Add(confirm);
            body.Add(row);
            ShowModal(title, body);
        }

        private void OpenSelectedReviewPostInPosts()
        {
            var post = _reviewList?.selectedItem as ScheduledPost;
            if (post == null && _reviewPosts.Count > 0)
                post = _reviewPosts[0];

            if (post == null || _db?.posts == null)
                return;

            var postId = post.id;

            ShowView(_viewPosts);
            RefreshPostsList();
            var index = _db.posts.FindIndex(item => item != null && item.id == postId);
            if (index < 0)
                return;

            _postsList.selectedIndex = index;
            LoadSelectedPostIntoEditor();
        }

        private HealthSnapshot BuildHealthSnapshot()
        {
            return _operationalHealth.BuildSnapshot(
                _db,
                _sendQueue,
                _rateLimitRegistry,
                _timeProvider.UtcNow,
                _lastSaveWarning,
                _lastJournalWarning,
                _lastLockWarning,
                _lastRetentionWarning);
        }

        private void RefreshHealthUi()
        {
            if (_db == null || _operationalHealth == null)
                return;

            var health = BuildHealthSnapshot();
            var stateClass = "statusReady";
            var stateText = "Ready";

            if (health.hasBlocker)
            {
                stateText = "Blocked";
                stateClass = "statusBlocked";
            }
            else if (health.needsReviewCount > 0)
            {
                stateText = "Needs review";
                stateClass = "statusReview";
            }
            else if (health.warningCount > 0)
            {
                stateText = "Warnings";
                stateClass = "statusReview";
            }

            SetText(_lblStatus, health.summary);
            SetText(_lblHealthState, stateText);
            ApplyStatusClass(_lblHealthState, stateClass);
            SetText(_lblNavPending, $"Pending {health.pendingCount}");
            SetText(_lblNavQueue, $"Queue {health.queuedCount + health.activeSendCount}");
            SetText(_lblNavReview, $"Review {health.needsReviewCount}");

            SetText(_lblMetricTargets, health.targetCount.ToString());
            SetText(_lblMetricPending, health.pendingCount.ToString());
            SetText(_lblMetricQueue, (health.queuedCount + health.activeSendCount).ToString());
            SetText(_lblMetricReview, health.needsReviewCount.ToString());
            SetText(_lblMetricWarnings, health.warningCount.ToString());
            SetText(_lblDashboardSummary, health.summary);
            SetText(_lblQueueHealthSummary, BuildQueueHealthSummary());
            SetText(_lblNextPost, BuildNextPostSummary());
            SetText(_lblSafetySummary, BuildSafetySummary(health));
        }

        private string BuildQueueHealthSummary()
        {
            var viewModel = _queueHealthPresenter?.Build(_db, _sendQueue, _rateLimitRegistry, _timeProvider.UtcNow);
            if (viewModel == null)
                return "Queue health is unavailable.";

            if (!viewModel.hasBackoff)
                return viewModel.headline + ". " + viewModel.summary;

            var wait = string.IsNullOrWhiteSpace(viewModel.nextAttemptUtcIso)
                ? ""
                : " Next retry: " + viewModel.nextAttemptUtcIso + ".";
            var reason = string.IsNullOrWhiteSpace(viewModel.waitReason)
                ? ""
                : " Reason: " + viewModel.waitReason;

            return viewModel.headline + ". " + viewModel.summary + wait + reason;
        }

        private string BuildNextPostSummary()
        {
            if (_db?.posts == null)
                return "No pending posts.";

            ScheduledPost nextPost = null;
            DateTime nextUtc = DateTime.MaxValue;

            foreach (var post in _db.posts)
            {
                if (post == null || post.status != PostStatus.Pending)
                    continue;

                if (!TimeUtil.TryParseIsoUtc(post.scheduledAtUtcIso, out var scheduledUtc))
                    continue;

                if (scheduledUtc < nextUtc)
                {
                    nextUtc = scheduledUtc;
                    nextPost = post;
                }
            }

            if (nextPost == null)
                return "No pending posts.";

            var (dateYmd, timeHm) = TimeUtil.UtcToBudapestFields(nextUtc);
            return $"{dateYmd} {timeHm} - {FindTargetLabel(nextPost.targetId)} - {BuildPostTitle(nextPost)}";
        }

        private string BuildSafetySummary(HealthSnapshot health)
        {
            if (health == null)
                return "Webhook URLs stay local. Review ambiguous sends before retrying.";

            if (health.hasBlocker)
                return "Writes or sends are blocked. Check storage and instance warnings before scheduling.";

            if (health.needsReviewCount > 0)
                return "Review ambiguous sends before retrying. Discord may already have received them.";

            if (health.activeBackoffCount > 0)
                return "Backoff is active. Retry waits for the persisted next attempt time.";

            return "Webhook URLs stay local. Mentions default to safe settings unless explicitly enabled.";
        }

        private string BuildPostTitle(ScheduledPost post)
        {
            if (post == null)
                return "(missing post)";

            if (!string.IsNullOrWhiteSpace(post.title))
                return TrimForDisplay(post.title, 72);

            if (!string.IsNullOrWhiteSpace(post.body))
                return TrimForDisplay(post.body, 72);

            return "(no title)";
        }

        private string BuildPostMeta(ScheduledPost post)
        {
            if (post == null)
                return "";

            var schedule = "Invalid schedule";
            if (TimeUtil.TryParseIsoUtc(post.scheduledAtUtcIso, out var scheduledUtc))
            {
                var (dateYmd, timeHm) = TimeUtil.UtcToBudapestFields(scheduledUtc);
                schedule = $"{dateYmd} {timeHm}";
            }

            var target = FindTargetLabel(post.targetId);
            var mediaKind = post.EffectiveMediaKind();
            var media = mediaKind == MediaKind.None ? "No media" : mediaKind.ToString();
            var nextAttempt = string.IsNullOrWhiteSpace(post.nextAttemptAtUtcIso)
                ? ""
                : " | next attempt " + post.nextAttemptAtUtcIso;

            return $"{schedule} | {target} | {media}{nextAttempt}";
        }

        private string FindTargetLabel(string targetId)
        {
            if (_db?.targets == null || string.IsNullOrWhiteSpace(targetId))
                return "No target";

            var target = _db.targets.FirstOrDefault(item => item != null && item.id == targetId);
            if (target == null)
                return "Missing target";

            if (!string.IsNullOrWhiteSpace(target.name))
                return target.name;

            var server = target.serverLabel ?? "";
            var channel = target.channelLabel ?? "";
            var label = (server + " / " + channel).Trim(' ', '/');
            return string.IsNullOrWhiteSpace(label) ? "Unnamed target" : label;
        }

        private static string TrimForDisplay(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            value = value.Replace("\r", " ").Replace("\n", " ").Trim();
            if (value.Length <= maxLength)
                return value;

            return value.Substring(0, Math.Max(0, maxLength - 3)) + "...";
        }

        private void ApplyPostStatusClass(Label label, PostStatus status)
        {
            var statusClass = "statusDraft";
            switch (status)
            {
                case PostStatus.Pending:
                    statusClass = "statusPending";
                    break;
                case PostStatus.Sending:
                    statusClass = "statusSending";
                    break;
                case PostStatus.Sent:
                    statusClass = "statusSent";
                    break;
                case PostStatus.Failed:
                    statusClass = "statusFailed";
                    break;
                case PostStatus.Missed:
                    statusClass = "statusMissed";
                    break;
                case PostStatus.NeedsReview:
                    statusClass = "statusReview";
                    break;
            }

            ApplyStatusClass(label, statusClass);
        }

        private static void ApplyStatusClass(Label label, string statusClass)
        {
            if (label == null)
                return;

            label.AddToClassList("statusPill");
            label.RemoveFromClassList("statusReady");
            label.RemoveFromClassList("statusQueued");
            label.RemoveFromClassList("statusSent");
            label.RemoveFromClassList("statusPending");
            label.RemoveFromClassList("statusSending");
            label.RemoveFromClassList("statusBackoff");
            label.RemoveFromClassList("statusReview");
            label.RemoveFromClassList("statusMissed");
            label.RemoveFromClassList("statusFailed");
            label.RemoveFromClassList("statusBlocked");
            label.RemoveFromClassList("statusDraft");

            if (!string.IsNullOrWhiteSpace(statusClass))
                label.AddToClassList(statusClass);
        }

        private static void SetText(Label label, string value)
        {
            if (label != null)
                label.text = value ?? "";
        }

        private void RefreshTargetsDropdowns()
        {
            var previousPostTargetId = GetSelectedTargetId(_ddPostTarget, _postTargetIds);
            var previousEditTargetId = GetSelectedTargetId(_ddEditTarget, _editTargetIds);

            BuildTargetDropdownData(out var labels, out var ids);

            ApplyTargetDropdown(_ddPostTarget, _postTargetIds, labels, ids, previousPostTargetId, true);
            ApplyTargetDropdown(_ddEditTarget, _editTargetIds, labels, ids, previousEditTargetId, true);
        }

        private void BuildTargetDropdownData(out List<string> labels, out List<string> ids)
        {
            labels = new List<string>();
            ids = new List<string>();

            if (_db?.targets == null)
                return;

            var baseLabels = _db.targets.Select(BuildTargetDropdownLabel).ToList();
            var duplicateLabels = new HashSet<string>(
                baseLabels
                    .GroupBy(label => label)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key));

            for (var i = 0; i < _db.targets.Count; i++)
            {
                var target = _db.targets[i];
                var label = baseLabels[i];
                if (duplicateLabels.Contains(label))
                    label = $"{label} [{ShortTargetId(target?.id)}]";

                labels.Add(label);
                ids.Add(target?.id ?? "");
            }
        }

        private static string BuildTargetDropdownLabel(Target target)
        {
            var label = target?.PrettyLabel();
            return string.IsNullOrWhiteSpace(label) ? "(unnamed target)" : label.Trim();
        }

        private static string ShortTargetId(string id)
        {
            var normalized = (id ?? "").Trim();
            if (normalized.Length == 0)
                return "no-id";

            return normalized.Length <= 8 ? normalized : normalized.Substring(0, 8);
        }

        private static string GetSelectedTargetId(DropdownField dropdownField, List<string> targetIds)
        {
            if (dropdownField == null || targetIds == null)
                return null;

            return dropdownField.index >= 0 && dropdownField.index < targetIds.Count
                ? targetIds[dropdownField.index]
                : null;
        }

        private static void ApplyTargetDropdown(
            DropdownField dropdownField,
            List<string> targetIds,
            List<string> labels,
            List<string> ids,
            string preferredTargetId,
            bool selectFirstWhenMissing)
        {
            if (dropdownField == null || targetIds == null)
                return;

            var previousIndex = dropdownField.index;

            targetIds.Clear();
            targetIds.AddRange(ids);
            dropdownField.choices = new List<string>(labels);

            if (ids.Count == 0)
            {
                dropdownField.index = -1;
                return;
            }

            var preferredIndex = !string.IsNullOrWhiteSpace(preferredTargetId)
                ? ids.IndexOf(preferredTargetId)
                : -1;

            if (preferredIndex >= 0)
            {
                dropdownField.index = preferredIndex;
                return;
            }

            if (previousIndex >= 0 && previousIndex < ids.Count)
            {
                dropdownField.index = previousIndex;
                return;
            }

            dropdownField.index = selectFirstWhenMissing ? 0 : -1;
        }

        private static void SelectTargetDropdownById(DropdownField dropdownField, List<string> targetIds, string targetId)
        {
            if (dropdownField == null || targetIds == null)
                return;

            if (string.IsNullOrWhiteSpace(targetId))
            {
                dropdownField.index = -1;
                return;
            }

            var idx = targetIds.IndexOf(targetId);
            dropdownField.index = idx >= 0 ? idx : -1;
        }

        private string SafeWebhookError(string error, string webhookUrl)
        {
            var safe = SecretRedactor.Redact(string.IsNullOrWhiteSpace(error) ? "Webhook request failed." : error);
            if (!string.IsNullOrWhiteSpace(webhookUrl))
                safe = safe.Replace(webhookUrl.Trim(), _webhookUrlValidator.Mask(webhookUrl));

            const int maxLength = 240;
            if (safe.Length > maxLength)
                safe = safe.Substring(0, maxLength) + "...";

            return safe;
        }

        private Target GetTargetByDropdown(DropdownField dropdownField)
        {
            if (dropdownField == null) return null; // guard: missing dropdown binding
            if (dropdownField.index < 0 || dropdownField.choices == null || dropdownField.index >= dropdownField.choices.Count) return null; // Guard: no selection

            var targetIds = dropdownField == _ddEditTarget ? _editTargetIds : _postTargetIds;
            var targetId = GetSelectedTargetId(dropdownField, targetIds);
            if (string.IsNullOrWhiteSpace(targetId)) return null;

            return _db.targets.FirstOrDefault(target => target.id == targetId);
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

            var draft = BuildCreatePostDraft(target);
            var preview = _payloadPreviewService.Build(target, draft);
            _lastCreatePayloadPreview = preview;
            var previewViewModel = _payloadPreviewPresenter.Build(preview);
            RenderCreatePayloadPreview(previewViewModel);
            if (!preview.canSend)
            {
                _lblScheduleResult.text = string.IsNullOrWhiteSpace(previewViewModel.disabledReason)
                    ? "Preview blocked scheduling."
                    : previewViewModel.disabledReason;
                return;
            }

            var createResult = _postApp.CreatePost(draft, _db, out var scheduledPost);
            if (!createResult.ok)
            {
                _lblScheduleResult.text = createResult.error;
                return;
            }

            _log.Info($"Scheduled '{scheduledPost.title}' ({scheduledPost.id}) for {scheduledPost.scheduledAtUtcIso} -> {target.name}");
            SaveDb();
            RefreshPostsList();

            _lblScheduleResult.text = "Scheduled.";
            _lastCreatePayloadPreview = null;
            RenderCreatePayloadPreview(null);
        }

        private void BindCreatePayloadPreview()
        {
            var btnPreviewRefresh = _root.Q<Button>("btnPayloadPreviewRefresh");
            if (btnPreviewRefresh != null)
                btnPreviewRefresh.clicked += RefreshCreatePayloadPreview;

            RegisterCreatePreviewInvalidators();
        }

        private void RefreshCreatePayloadPreview()
        {
            var target = GetTargetByDropdown(_ddPostTarget);
            var draft = BuildCreatePostDraft(target);
            _lastCreatePayloadPreview = _payloadPreviewService.Build(target, draft);
            RenderCreatePayloadPreview(_payloadPreviewPresenter.Build(_lastCreatePayloadPreview));
        }

        private void UpdateCreatePayloadPreviewStaleState()
        {
            if (_lastCreatePayloadPreview == null)
                return;

            var target = GetTargetByDropdown(_ddPostTarget);
            var draft = BuildCreatePostDraft(target);
            var currentFingerprint = _payloadPreviewService.BuildFingerprint(target, draft);
            RenderCreatePayloadPreview(_payloadPreviewPresenter.Build(_lastCreatePayloadPreview, currentFingerprint));
        }

        private void RenderCreatePayloadPreview(PayloadPreviewViewModel viewModel)
        {
            if (viewModel == null)
            {
                SetText(_lblPayloadPreviewHeadline, "Preview has not been generated yet.");
                SetText(_lblPayloadPreviewDigest, "No Discord request has been built.");
                SetText(_lblPayloadPreviewDetails, "Preview uses the same local normalizer and validator as sending.");
                SetText(_lblPayloadPreviewPayload, "");
                return;
            }

            SetText(_lblPayloadPreviewHeadline, viewModel.headline);
            SetText(_lblPayloadPreviewDigest, viewModel.previewDigest);

            var details = string.IsNullOrWhiteSpace(viewModel.disabledReason)
                ? viewModel.readiness + " - " + viewModel.primaryAction
                : viewModel.readiness + " - " + viewModel.disabledReason;
            SetText(_lblPayloadPreviewDetails, details);

            var payload = viewModel.technicalDetailsAvailable
                ? "Redacted payload: " + viewModel.redactedPayloadJson
                : "";
            SetText(_lblPayloadPreviewPayload, payload);
        }

        private void RegisterCreatePreviewInvalidators()
        {
            if (_ddPostTarget != null) _ddPostTarget.RegisterValueChangedCallback(_ => UpdateCreatePayloadPreviewStaleState());
            if (_tfPostTitle != null) _tfPostTitle.RegisterValueChangedCallback(_ => UpdateCreatePayloadPreviewStaleState());
            if (_tfPostBody != null) _tfPostBody.RegisterValueChangedCallback(_ => UpdateCreatePayloadPreviewStaleState());
            if (_tfDate != null) _tfDate.RegisterValueChangedCallback(_ => UpdateCreatePayloadPreviewStaleState());
            if (_tfTime != null) _tfTime.RegisterValueChangedCallback(_ => UpdateCreatePayloadPreviewStaleState());
            if (_tfMediaPath != null) _tfMediaPath.RegisterValueChangedCallback(_ => UpdateCreatePayloadPreviewStaleState());
            if (_tgPostModeNormal != null) _tgPostModeNormal.RegisterValueChangedCallback(_ => UpdateCreatePayloadPreviewStaleState());
            if (_tgPostModeEmbed != null) _tgPostModeEmbed.RegisterValueChangedCallback(_ => UpdateCreatePayloadPreviewStaleState());
            if (_tgAttachMedia != null) _tgAttachMedia.RegisterValueChangedCallback(_ => UpdateCreatePayloadPreviewStaleState());
            if (_tgAllowUsers != null) _tgAllowUsers.RegisterValueChangedCallback(_ => UpdateCreatePayloadPreviewStaleState());
            if (_tgAllowRoles != null) _tgAllowRoles.RegisterValueChangedCallback(_ => UpdateCreatePayloadPreviewStaleState());
            if (_tgAllowEveryone != null) _tgAllowEveryone.RegisterValueChangedCallback(_ => UpdateCreatePayloadPreviewStaleState());
            if (_tfMentionUserIds != null) _tfMentionUserIds.RegisterValueChangedCallback(_ => UpdateCreatePayloadPreviewStaleState());
            if (_tfMentionRoleIds != null) _tfMentionRoleIds.RegisterValueChangedCallback(_ => UpdateCreatePayloadPreviewStaleState());
            if (_ddOffPolicy != null) _ddOffPolicy.RegisterValueChangedCallback(_ => UpdateCreatePayloadPreviewStaleState());
            if (_ddSleepPolicy != null) _ddSleepPolicy.RegisterValueChangedCallback(_ => UpdateCreatePayloadPreviewStaleState());
        }



        private void LoadSelectedPostIntoEditor()
        {
            _lblEditResult.text = "";

            var selectedIndex = _postsList.selectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _db.posts.Count)
            {
                if (_lblPostSelectionHint != null)
                    _lblPostSelectionHint.style.display = DisplayStyle.Flex;

                SetPostEditActionButtonsEnabled(false);
                _tfEditTitle.value = "";
                _tfEditBody.value = "";
                _tfEditDate.value = "";
                _tfEditTime.value = "";
                _tfEditImagePath.value = "";
                if (_ddEditAttachmentPick != null) _ddEditAttachmentPick.index = -1;
                _lblEditStatus.text = "";
                RefreshBodyCounter(_tfEditBody, _lblEditBodyCounter);
                _mediaPreview.UpdateEdit();
                return; // early return: no selection
            }

            if (_lblPostSelectionHint != null)
                _lblPostSelectionHint.style.display = DisplayStyle.None;

            SetPostEditActionButtonsEnabled(true);
            var scheduledPost = _db.posts[selectedIndex];
            SelectTargetDropdownById(_ddEditTarget, _editTargetIds, scheduledPost.targetId);

            _tfEditTitle.value = scheduledPost.title ?? "";
            _tfEditBody.value = scheduledPost.body ?? "";

            if (TimeUtil.TryParseIsoUtc(scheduledPost.scheduledAtUtcIso, out var scheduledUtc))
            {
                var (dateYmd, timeHm) = TimeUtil.UtcToBudapestFields(scheduledUtc);
                _tfEditDate.value = dateYmd;
                _tfEditTime.value = timeHm;
            }
            else
            {
                _tfEditDate.value = "";
                _tfEditTime.value = "";
                _lblEditResult.text = "Invalid saved schedule. Pick a new date and time.";
            }

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

            _mediaPreview.UpdateEdit();
        }

        private void SetPostEditActionButtonsEnabled(bool hasSelection)
        {
            var hasSentPosts = _db?.posts != null && _db.posts.Any(post => post != null && post.status == PostStatus.Sent);
            _postEditPanel.SetActionButtonsEnabled(hasSelection, hasSentPosts);
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

            var draft = BuildEditPostDraft(scheduledPost, target);
            var updateResult = _postApp.UpdatePost(scheduledPost, draft, _db);
            if (!updateResult.ok)
            {
                _lblEditResult.text = updateResult.error;
                return;
            }

            SyncEditAttachmentDropdownToPath(scheduledPost.EffectiveMediaPath());
            _mediaPreview.UpdateEdit();

            SaveDb();
            RefreshPostsList();
            _lblEditResult.text = "Save.";
            LoadSelectedPostIntoEditor();
        }

        private void DeleteSelectedPost()
        {
            var idx = _postsList.selectedIndex;
            if (idx < 0 || idx >= _db.posts.Count) return;

            var post = _db.posts[idx];
            var deleteGuard = _mutationGuard.CanDeletePost(post);
            if (!deleteGuard.ok)
            {
                _lblEditResult.text = deleteGuard.error;
                return;
            }

            if (post != null && _sendQueue.CancelQueued(post.id))
                _log.Warn($"Cancelled queued send for deleted post: {post.id}");

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

            var selectedPost = _db.posts[idx];
            _log.Info($"Force send requested: {selectedPost.id} ({selectedPost.title})");
            _lblEditResult.text = EnqueueSend(selectedPost) ? "Queued for sending." : "Post cannot be queued.";
        }

        private void MarkSelectedPostPending()
        {
            var idx = _postsList.selectedIndex;
            if (idx < 0 || idx >= _db.posts.Count)
            {
                _lblEditResult.text = "No post selected.";
                return;
            }

            var selectedPost = _db.posts[idx];
            var pendingResult = _postStateMachine.TryMarkPendingManual(selectedPost, resetRetries: true);
            if (!pendingResult.ok)
            {
                _lblEditResult.text = pendingResult.error;
                return;
            }

            SaveDb();
            RefreshPostsList();
            _lblEditResult.text = "Set to pending.";
            LoadSelectedPostIntoEditor();
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
                    var mediaResult = _postMediaService.TryPrepareMedia(current, tempId, MediaKind.None, out _, out var newPath);
                    if (mediaResult.ok)
                    {
                        _tfMediaPath.value = newPath;
                        preferredPath = newPath;
                        _lblMediaHint.text = "";
                        _mediaPreview.UpdateNew();
                    }
                    else
                    {
                        _lblMediaHint.text = mediaResult.error;
                    }
                }
                else
                {
                    preferredPath = current;
                }
            }

            RefreshAttachmentsDropdown(_ddMediaPick, MediaKind.None, preferredPath);
            _mediaPreview.UpdateNew();
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

        private PostDraft BuildCreatePostDraft(Target target)
        {
            return new PostDraft
            {
                targetId = target?.id ?? "",
                title = (_tfPostTitle?.value ?? "").Trim(),
                body = (_tfPostBody?.value ?? "").Trim(),
                dateYmd = (_tfDate?.value ?? "").Trim(),
                timeHm = (_tfTime?.value ?? "").Trim(),
                mediaPath = GetCreateDraftMediaPath(),
                sendAsEmbed = _tgPostModeEmbed != null && _tgPostModeEmbed.value,
                allowedMentions = new AllowedMentions
                {
                    allowUsers = _tgAllowUsers != null && _tgAllowUsers.value,
                    allowRoles = _tgAllowRoles != null && _tgAllowRoles.value,
                    allowEveryone = _tgAllowEveryone != null && _tgAllowEveryone.value,
                    userIdsCsv = _tfMentionUserIds?.value ?? "",
                    roleIdsCsv = _tfMentionRoleIds?.value ?? ""
                },
                missedPolicyIfOff = (MissedPolicy)Mathf.Clamp(_ddOffPolicy.index, 0, 2),
                missedPolicyIfSleep = (MissedPolicy)Mathf.Clamp(_ddSleepPolicy.index, 0, 2)
            };
        }

        private PostDraft BuildEditPostDraft(ScheduledPost scheduledPost, Target target)
        {
            return new PostDraft
            {
                id = scheduledPost?.id ?? "",
                targetId = target?.id ?? "",
                title = (_tfEditTitle?.value ?? "").Trim(),
                body = (_tfEditBody?.value ?? "").Trim(),
                dateYmd = (_tfEditDate?.value ?? "").Trim(),
                timeHm = (_tfEditTime?.value ?? "").Trim(),
                mediaPath = (_tfEditImagePath?.value ?? "").Trim(),
                sendAsEmbed = _tgEditModeEmbed != null && _tgEditModeEmbed.value,
                allowedMentions = new AllowedMentions
                {
                    allowUsers = _tgEditAllowUsers != null && _tgEditAllowUsers.value,
                    allowRoles = _tgEditAllowRoles != null && _tgEditAllowRoles.value,
                    allowEveryone = _tgEditAllowEveryone != null && _tgEditAllowEveryone.value,
                    userIdsCsv = _tfEditMentionUserIds?.value ?? "",
                    roleIdsCsv = _tfEditMentionRoleIds?.value ?? ""
                },
                missedPolicyIfOff = (MissedPolicy)Mathf.Clamp(_ddEditOffPolicy.index, 0, 2),
                missedPolicyIfSleep = (MissedPolicy)Mathf.Clamp(_ddEditSleepPolicy.index, 0, 2)
            };
        }

        private string GetCreateDraftMediaPath()
        {
            if (_tgAttachMedia != null && _tgAttachMedia.value)
                return (_tfMediaPath?.value ?? "").Trim();

            return (_tfImagePath?.value ?? "").Trim();
        }

        private void HandleDuePost(DuePost duePost)
        {
            var decision = _schedulePolicy.Decide(duePost);
            if (duePost == null || duePost.post == null)
            {
                _log.Warn($"Schedule decision ignored: {decision.reason}");
                return;
            }

            ApplyScheduleDecision(duePost.post, decision);
        }

        private void ApplyScheduleDecision(ScheduledPost scheduledPost, ScheduleDecision decision)
        {
            if (scheduledPost == null) return;

            decision = decision ?? ScheduleDecision.Ignore("Missing schedule decision.");

            if (decision.kind == ScheduleDecisionKind.Enqueue)
            {
                _log.Info($"Schedule decision enqueue: {scheduledPost.id} ({scheduledPost.title}) reason: {decision.reason}");
                EnqueueSend(scheduledPost);
                return;
            }

            if (decision.kind == ScheduleDecisionKind.MarkMissed)
            {
                var transition = _postStateMachine.MarkMissed(scheduledPost, decision.reason);
                if (!transition.ok)
                {
                    _log.Warn($"Mark missed rejected for {scheduledPost.id} ({scheduledPost.title}) reason: {transition.error}");
                    return;
                }

                _log.Warn($"Marked missed: {scheduledPost.id} ({scheduledPost.title}) reason: {decision.reason}");
                SaveDb();
                RefreshPostsList();
                return;
            }

            if (decision.kind == ScheduleDecisionKind.MarkFailed)
            {
                var transition = _postStateMachine.MarkFailedByPolicy(scheduledPost, $"Missed (policy: Failed). {decision.reason}");
                if (!transition.ok)
                {
                    _log.Warn($"Mark failed rejected for {scheduledPost.id} ({scheduledPost.title}) reason: {transition.error}");
                    return;
                }

                _log.Warn($"Marked failed (missed): {scheduledPost.id} ({scheduledPost.title}) reason: {decision.reason}");
                SaveDb();
                RefreshPostsList();
                return;
            }

            _log.Warn($"Schedule decision ignored for {scheduledPost.id} ({scheduledPost.title}) reason: {decision.reason}");
        }

        private bool EnqueueSend(ScheduledPost p)
        {
            if (p == null) return false;
            if (_storageWriteBlocked)
            {
                _log.Warn("Queue send rejected: storage is locked by another app instance.");
                return false;
            }

            var canStart = _lifecycle.CanStartSend();
            if (!canStart.ok)
            {
                _log.Warn($"Queue send rejected: {p.id} ({p.title}) reason: {canStart.error}");
                return false;
            }

            // if allowed mentions UI was hidden, optionally apply defaults if user hasn't set explicit values.
            // (we keep the stored values as-is; defaults are used at creation time already in ScheduledPost.)

            var previousState = _postStateMachine.Capture(p);
            if (!TryApplyRateLimitGuard(p))
                return false;

            var enqueue = _sendQueue.TryEnqueue(p);
            if (!enqueue.ok)
            {
                if (!string.Equals(enqueue.error, "Post retry is not due yet.", StringComparison.Ordinal))
                    _log.Warn($"Queue send rejected: {p.id} ({p.title}) reason: {enqueue.error}");
                return false;
            }

            _log.Info($"Queue send: {p.id} ({p.title}) scheduled {p.scheduledAtUtcIso}");

            var saveResult = SaveDb();
            if (!saveResult.ok)
            {
                _sendQueue.CancelQueued(p.id);
                _postStateMachine.Restore(p, previousState);
                _log.Error("Queue send aborted because the Sending state could not be persisted.");
                RefreshPostsList();
                return false;
            }

            RefreshPostsList();
            return true;
        }

        private IEnumerator SendPostCoroutine(ScheduledPost post)
        {
            var target = _db.targets.FirstOrDefault(t => t.id == post.targetId);
            var attemptId = Guid.NewGuid().ToString("N");
            var startedAtUtcIso = TimeUtil.ToIsoUtc(_timeProvider.UtcNow);

            if (target == null)
            {
                var missingTargetResult = new WebhookSendResult
                {
                    ok = false,
                    statusCode = 0,
                    outcome = SendOutcomeKind.NonRetryable,
                    shortError = "Missing target (deleted?)."
                };
                _postStateMachine.MarkFailedByPolicy(post, missingTargetResult.shortError);
                AppendSendAttempt(post, null, attemptId, startedAtUtcIso, missingTargetResult);
                _log.Error($"Send failed: missing target for post {post.id}");
                SaveDb();
                RefreshPostsList();
                _sendQueue.MarkFinished(post.id);
                yield break;
            }

            var snapshot = _targetRevisionPolicy.CreateSnapshot(post, target, attemptId, startedAtUtcIso);
            _sendQueue.SetActiveSnapshot(snapshot);

            _log.Info($"Send start: {post.id} ({post.title}) -> {target.name}");
            bool done = false;
            WebhookSendResult result = null;

            yield return _webhook.Send(target, post, sendResult =>
            {
                result = sendResult;
                done = true;
            });

            while (!done) yield return null;

            if (result == null)
            {
                result = new WebhookSendResult
                {
                    ok = false,
                    statusCode = 0,
                    outcome = SendOutcomeKind.Ambiguous,
                    shortError = "Webhook result was missing."
                };
            }

            if (result.outcome == SendOutcomeKind.RateLimited)
            {
                if (result.rateLimitGlobal ||
                    string.Equals(result.rateLimitScope ?? "", "global", StringComparison.OrdinalIgnoreCase))
                {
                    _rateLimitRegistry.RecordGlobalBackoff(_timeProvider.UtcNow, result.retryAfterSeconds, result.shortError);
                }
                else
                {
                    _rateLimitRegistry.RecordTargetBackoff(target.id, _timeProvider.UtcNow, result.retryAfterSeconds, result.shortError);
                }
            }

            if (!_sendQueue.IsActiveAttempt(post.id, attemptId))
            {
                _log.Warn($"Ignored stale send callback for post {post.id}; active attempt changed.");
                yield break;
            }

            if (!_targetRevisionPolicy.Matches(snapshot, post, target, attemptId))
            {
                result = new WebhookSendResult
                {
                    ok = false,
                    statusCode = result.statusCode,
                    outcome = SendOutcomeKind.Ambiguous,
                    shortError = "Send snapshot changed during active send; manual review required."
                };
            }

            if (result.ok)
            {
                var transition = _postStateMachine.MarkSent(post, result);
                if (!transition.ok)
                    _log.Warn($"Send success transition rejected for {post.id}: {transition.error}");
                _log.Info($"Send ok: {post.id} -> {target.name}");
            }
            else
            {
                var transition = _postStateMachine.MarkSendFailedOrRetry(post, result);
                if (!transition.ok)
                    _log.Warn($"Send failure transition rejected for {post.id}: {transition.error}");

                _log.Warn($"Send failed: {post.id} -> {target.name} status={post.status} retry={post.retries} err={result.shortError}");
            }

            AppendSendAttempt(post, target, attemptId, startedAtUtcIso, result);
            SaveDb();
            RefreshPostsList();
            LoadSelectedPostIntoEditor();

            _sendQueue.MarkFinished(post.id, attemptId);
        }

        private void AppendSendAttempt(ScheduledPost post, Target target, string attemptId, string startedAtUtcIso, WebhookSendResult result)
        {
            if (_sendAttemptJournal == null || post == null || result == null)
                return;

            var append = _sendAttemptJournal.Append(new SendAttemptRecord
            {
                attemptId = attemptId,
                postId = post.id,
                targetId = target?.id ?? post.targetId,
                startedAtUtcIso = startedAtUtcIso,
                finishedAtUtcIso = TimeUtil.ToIsoUtc(_timeProvider.UtcNow),
                statusCode = result.statusCode,
                outcome = result.outcome,
                discordMessageId = result.discordMessageId,
                shortError = result.shortError
            });

            if (!append.ok)
            {
                _lastJournalWarning = SecretRedactor.Redact(append.error);
                _log.Warn(_lastJournalWarning);
            }
            else
            {
                _lastJournalWarning = "";
            }
        }

        private ValidationResult SaveDb()
        {
            if (_storageWriteBlocked)
            {
                var error = "Storage is locked by another app instance. Save blocked.";
                _lastSaveWarning = error;
                _log.Error(error);
                if (_lblStatus != null)
                    _lblStatus.text = error;
                return ValidationResult.Fail(error);
            }

            var result = _storage.TrySave(_db);
            if (!result.ok)
            {
                _lastSaveWarning = result.error;
                _log.Error(result.error);
                if (_lblStatus != null)
                    _lblStatus.text = "Save failed: " + result.error;
            }
            else
            {
                _lastSaveWarning = "";
            }

            return result;
        }

        private bool TryApplyRateLimitGuard(ScheduledPost post)
        {
            if (post == null)
                return false;

            var targetId = post.targetId ?? "";
            var rate = _rateLimitRegistry.CanSend(targetId, _timeProvider.UtcNow);
            if (rate.ok)
                return true;

            var window = _rateLimitRegistry.GetTargetBackoff(targetId, _timeProvider.UtcNow);
            if (window != null && TimeUtil.TryParseIsoUtc(window.untilUtcIso, out var untilUtc))
            {
                var defer = _postStateMachine.DeferPendingUntil(post, untilUtc, rate.error);
                if (!defer.ok)
                    _log.Warn($"Rate-limit defer rejected for {post.id}: {defer.error}");
                else
                    _log.Warn($"Rate-limit deferred post {post.id} until {window.untilUtcIso}.");

                SaveDb();
                RefreshPostsList();
                return false;
            }

            _log.Warn($"Queue send rejected: {post.id} ({post.title}) reason: {rate.error}");
            return false;
        }

        private void MarkActiveSendAmbiguousOnShutdown()
        {
            if (_lifecycle == null)
                return;

            _lifecycle.MarkShutdownStarted();

            var activeId = _sendQueue?.ActivePostId ?? "";
            if (string.IsNullOrWhiteSpace(activeId) || _db?.posts == null)
                return;

            var post = _db.posts.FirstOrDefault(p => p != null && p.id == activeId);
            if (post == null)
                return;

            var result = _lifecycle.MarkActiveSendAmbiguousOnShutdown(post, "Application quit during active send; manual review required.");
            if (result.ok)
            {
                _log.Warn($"Active send marked NeedsReview during shutdown: {post.id}");
                SaveDb();
            }
            else
            {
                _log.Warn("Active send shutdown recovery skipped: " + result.error);
            }
        }

        private void ShowView(VisualElement view)
        {
            if (_viewDashboard != null) _viewDashboard.style.display = DisplayStyle.None;
            if (_viewTargets != null) _viewTargets.style.display = DisplayStyle.None;
            if (_viewNew != null) _viewNew.style.display = DisplayStyle.None;
            if (_viewPosts != null) _viewPosts.style.display = DisplayStyle.None;
            if (_viewReview != null) _viewReview.style.display = DisplayStyle.None;
            if (_viewSettings != null) _viewSettings.style.display = DisplayStyle.None;
            if (_viewLog != null) _viewLog.style.display = DisplayStyle.None;

            if (view != null) view.style.display = DisplayStyle.Flex;
            SetActiveNavigation(view);

            if (view == _viewReview)
                RefreshReviewList();

            RefreshHealthUi();
        }

        private void SetActiveNavigation(VisualElement view)
        {
            ClearActiveNavigation(_btnDashboard);
            ClearActiveNavigation(_btnTargets);
            ClearActiveNavigation(_btnNewPost);
            ClearActiveNavigation(_btnPosts);
            ClearActiveNavigation(_btnReview);
            ClearActiveNavigation(_btnSettings);
            ClearActiveNavigation(_btnLog);

            if (view == _viewDashboard) SetActiveNavigation(_btnDashboard);
            else if (view == _viewTargets) SetActiveNavigation(_btnTargets);
            else if (view == _viewNew) SetActiveNavigation(_btnNewPost);
            else if (view == _viewPosts) SetActiveNavigation(_btnPosts);
            else if (view == _viewReview) SetActiveNavigation(_btnReview);
            else if (view == _viewSettings) SetActiveNavigation(_btnSettings);
            else if (view == _viewLog) SetActiveNavigation(_btnLog);
        }

        private static void ClearActiveNavigation(Button button)
        {
            if (button != null)
                button.RemoveFromClassList("navBtnActive");
        }

        private static void SetActiveNavigation(Button button)
        {
            if (button != null)
                button.AddToClassList("navBtnActive");
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
                    var len = _payloadTextNormalizer.GetNormalizedLength(field.value ?? string.Empty);
                    var remain = Mathf.Max(0, BodyMaxChars - len);
                    counter.text = $"{len} / {BodyMaxChars}  (remain: {remain})";
                }
            }

            field.RegisterValueChangedCallback(evt =>
            {
                var txt = evt.newValue ?? string.Empty;
                if (_payloadTextNormalizer.GetNormalizedLength(txt) > BodyMaxChars && txt.Length > BodyMaxChars)
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
            var len = _payloadTextNormalizer.GetNormalizedLength(field.value ?? string.Empty);
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

            if (MediaAttachmentRules.IsAllowedImagePath(path))
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

            if (MediaAttachmentRules.IsAllowedVideoPath(path))
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

                int frameWidth = Mathf.Max(2, videoPlayer.width > 0 ? (int)videoPlayer.width : 320);
                int frameHeight = Mathf.Max(2, videoPlayer.height > 0 ? (int)videoPlayer.height : 180);

                renderTexture = new RenderTexture(frameWidth, frameHeight, 0, RenderTextureFormat.ARGB32);
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
                catch (Exception exception)
                {
                    if (tex != null)
                    {
                        UnityEngine.Object.Destroy(tex);
                        tex = null;
                    }

                    Debug.LogWarning("Video preview: failed to capture frame: " + exception.Message);
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
                DateTime leftUtc = default;
                DateTime rightUtc = default;
                var leftOk = left != null && TimeUtil.TryParseIsoUtc(left.scheduledAtUtcIso, out leftUtc);
                var rightOk = right != null && TimeUtil.TryParseIsoUtc(right.scheduledAtUtcIso, out rightUtc);

                if (leftOk && rightOk)
                    return DateTime.Compare(leftUtc, rightUtc);

                if (leftOk) return -1;
                if (rightOk) return 1;

                return string.Compare(left?.id, right?.id, StringComparison.Ordinal);
            });
        }

    }
}
