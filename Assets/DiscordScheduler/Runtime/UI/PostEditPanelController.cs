using System;
using UnityEngine.UIElements;

namespace DiscordScheduler
{
    public sealed class PostEditPanelController
    {
        private readonly Action _savePost;
        private readonly Action _deletePost;
        private readonly Action _forceSend;
        private readonly Action _deleteSentPosts;
        private readonly Action _markPending;
        private readonly Action _openAttachments;
        private readonly Action _refreshAttachments;
        private readonly Action _updatePreview;
        private readonly Func<DropdownField, string> _getSelectedAttachmentPath;

        private TextField _tfEditImagePath;
        private DropdownField _ddEditAttachmentPick;
        private Button _btnSavePost;
        private Button _btnDeletePost;
        private Button _btnForceSend;
        private Button _btnDeleteSentPosts;
        private Button _btnMarkPending;
        private Button _btnClearMedia;

        public PostEditPanelController(
            Action savePost,
            Action deletePost,
            Action forceSend,
            Action deleteSentPosts,
            Action markPending,
            Action openAttachments,
            Action refreshAttachments,
            Action updatePreview,
            Func<DropdownField, string> getSelectedAttachmentPath)
        {
            _savePost = savePost ?? throw new ArgumentNullException(nameof(savePost));
            _deletePost = deletePost ?? throw new ArgumentNullException(nameof(deletePost));
            _forceSend = forceSend ?? throw new ArgumentNullException(nameof(forceSend));
            _deleteSentPosts = deleteSentPosts ?? throw new ArgumentNullException(nameof(deleteSentPosts));
            _markPending = markPending ?? throw new ArgumentNullException(nameof(markPending));
            _openAttachments = openAttachments ?? throw new ArgumentNullException(nameof(openAttachments));
            _refreshAttachments = refreshAttachments ?? throw new ArgumentNullException(nameof(refreshAttachments));
            _updatePreview = updatePreview ?? throw new ArgumentNullException(nameof(updatePreview));
            _getSelectedAttachmentPath = getSelectedAttachmentPath ?? throw new ArgumentNullException(nameof(getSelectedAttachmentPath));
        }

        public void Bind(VisualElement root)
        {
            if (root == null)
                return;

            _tfEditImagePath = root.Q<TextField>("tfEditImagePath");
            _ddEditAttachmentPick = root.Q<DropdownField>("ddEditAttachmentPick");

            _btnSavePost = BindButton(root, "btnPostSave", _savePost);
            _btnDeletePost = BindButton(root, "btnPostDelete", _deletePost);
            _btnForceSend = BindButton(root, "btnPostForceSend", _forceSend);
            _btnDeleteSentPosts = BindButton(root, "btnPostDeleteSent", _deleteSentPosts);
            _btnMarkPending = BindButton(root, "btnPostMarkPending", _markPending);
            BindButton(root, "btnOpenAttachments2", _openAttachments);
            BindButton(root, "btnEditRefreshAttachments", _refreshAttachments);

            _btnClearMedia = root.Q<Button>("btnEditImageClear");
            if (_btnClearMedia != null)
            {
                _btnClearMedia.clicked += () =>
                {
                    if (_tfEditImagePath != null) _tfEditImagePath.value = "";
                    if (_ddEditAttachmentPick != null) _ddEditAttachmentPick.index = -1;
                };
            }

            if (_ddEditAttachmentPick != null)
            {
                _ddEditAttachmentPick.RegisterValueChangedCallback(_ =>
                {
                    var path = _getSelectedAttachmentPath(_ddEditAttachmentPick);
                    if (!string.IsNullOrEmpty(path) && _tfEditImagePath != null)
                        _tfEditImagePath.value = path;
                    _updatePreview();
                });
            }
        }

        public void SetActionButtonsEnabled(bool hasSelection, bool hasSentPosts)
        {
            UiActionState.SetEnabled(_btnSavePost, hasSelection);
            UiActionState.SetEnabled(_btnDeletePost, hasSelection);
            UiActionState.SetEnabled(_btnForceSend, hasSelection);
            UiActionState.SetEnabled(_btnMarkPending, hasSelection);
            UiActionState.SetEnabled(_btnClearMedia, hasSelection);
            UiActionState.SetEnabled(_btnDeleteSentPosts, hasSentPosts);
        }

        private static Button BindButton(VisualElement root, string name, Action callback)
        {
            var button = root.Q<Button>(name);
            if (button != null)
                button.clicked += callback;

            return button;
        }
    }
}
