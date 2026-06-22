using System;
using UnityEngine.UIElements;

namespace DiscordScheduler
{
    public sealed class PostCreatePanelController
    {
        private readonly Action _schedulePost;
        private readonly Action _refreshAttachments;
        private readonly Action _updatePreview;
        private readonly Action _openAttachments;
        private readonly Func<string, string> _pickFile;
        private readonly Func<DropdownField, string> _getSelectedAttachmentPath;

        private Toggle _tgAttachMedia;
        private VisualElement _mediaOptionsNew;
        private Toggle _tgMediaIsImage;
        private Toggle _tgMediaIsVideo;
        private TextField _tfMediaPath;
        private VisualElement _mediaButtonsNew;
        private Button _btnMediaClear;
        private DropdownField _ddMediaPick;
        private Label _lblMediaHint;
        private VisualElement _mediaPreviewNew;

        public PostCreatePanelController(
            Action schedulePost,
            Action refreshAttachments,
            Action updatePreview,
            Action openAttachments,
            Func<string, string> pickFile,
            Func<DropdownField, string> getSelectedAttachmentPath)
        {
            _schedulePost = schedulePost ?? throw new ArgumentNullException(nameof(schedulePost));
            _refreshAttachments = refreshAttachments ?? throw new ArgumentNullException(nameof(refreshAttachments));
            _updatePreview = updatePreview ?? throw new ArgumentNullException(nameof(updatePreview));
            _openAttachments = openAttachments ?? throw new ArgumentNullException(nameof(openAttachments));
            _pickFile = pickFile ?? throw new ArgumentNullException(nameof(pickFile));
            _getSelectedAttachmentPath = getSelectedAttachmentPath ?? throw new ArgumentNullException(nameof(getSelectedAttachmentPath));
        }

        public void Bind(VisualElement root)
        {
            if (root == null)
                return;

            _tgAttachMedia = root.Q<Toggle>("tgAttachMedia");
            _mediaOptionsNew = root.Q<VisualElement>("mediaOptionsNew");
            _tgMediaIsImage = root.Q<Toggle>("tgMediaIsImage");
            _tgMediaIsVideo = root.Q<Toggle>("tgMediaIsVideo");
            _tfMediaPath = root.Q<TextField>("tfMediaPath");
            _mediaButtonsNew = root.Q<VisualElement>("mediaButtonsNew");
            _btnMediaClear = root.Q<Button>("btnMediaClear");
            _ddMediaPick = root.Q<DropdownField>("ddMediaPick");
            _lblMediaHint = root.Q<Label>("lblMediaHint");
            _mediaPreviewNew = root.Q<VisualElement>("mediaPreviewNew");

            var btnSchedule = root.Q<Button>("btnSchedule");
            if (btnSchedule != null)
                btnSchedule.clicked += _schedulePost;

            HideLegacyMediaKindControls();
            BindMediaControls(root);
        }

        private void HideLegacyMediaKindControls()
        {
            if (_mediaOptionsNew != null) _mediaOptionsNew.style.display = DisplayStyle.None;
            if (_tgMediaIsImage != null) _tgMediaIsImage.style.display = DisplayStyle.None;
            if (_tgMediaIsVideo != null) _tgMediaIsVideo.style.display = DisplayStyle.None;
        }

        private void BindMediaControls(VisualElement root)
        {
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
                        _updatePreview();
                    }
                    else
                    {
                        _refreshAttachments();
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
                    var path = _getSelectedAttachmentPath(_ddMediaPick);
                    if (!string.IsNullOrEmpty(path) && _tfMediaPath != null)
                        _tfMediaPath.value = path;
                    _updatePreview();
                });
            }

            var btnOpenAttachments = root.Q<Button>("btnMediaOpenAttachments");
            if (btnOpenAttachments != null)
                btnOpenAttachments.clicked += _openAttachments;

            var btnMediaRefresh = root.Q<Button>("btnMediaRefresh");
            if (btnMediaRefresh != null)
            {
                btnMediaRefresh.clicked += () =>
                {
                    _refreshAttachments();
                    _updatePreview();
                };
            }

            var btnMediaBrowse = root.Q<Button>("btnMediaBrowse");
            if (btnMediaBrowse != null)
            {
                btnMediaBrowse.clicked += () =>
                {
                    var filter = "Images/Videos (*.png;*.jpg;*.jpeg;*.webp;*.gif;*.mp4;*.webm;*.mov)|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.mp4;*.webm;*.mov|All files (*.*)|*.*";
                    var picked = _pickFile(filter);
                    if (!string.IsNullOrEmpty(picked) && _tfMediaPath != null)
                        _tfMediaPath.value = picked;
                    _updatePreview();
                };
            }
        }

        private void SetMediaUiVisible(bool visible)
        {
            var display = visible ? DisplayStyle.Flex : DisplayStyle.None;

            if (_mediaOptionsNew != null) _mediaOptionsNew.style.display = DisplayStyle.None;
            if (_tfMediaPath != null) _tfMediaPath.style.display = display;
            if (_mediaButtonsNew != null) _mediaButtonsNew.style.display = display;
            if (_ddMediaPick != null) _ddMediaPick.style.display = display;
            if (_lblMediaHint != null) _lblMediaHint.style.display = display;
            if (_mediaPreviewNew != null) _mediaPreviewNew.style.display = display;
        }
    }
}
