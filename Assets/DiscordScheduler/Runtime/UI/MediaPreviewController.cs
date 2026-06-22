using System;

namespace DiscordScheduler
{
    public sealed class MediaPreviewController
    {
        private readonly Action _updateNew;
        private readonly Action _updateEdit;
        private readonly Action _cleanup;

        public MediaPreviewController(Action updateNew, Action updateEdit, Action cleanup)
        {
            _updateNew = updateNew ?? throw new ArgumentNullException(nameof(updateNew));
            _updateEdit = updateEdit ?? throw new ArgumentNullException(nameof(updateEdit));
            _cleanup = cleanup ?? throw new ArgumentNullException(nameof(cleanup));
        }

        public void UpdateNew()
        {
            _updateNew();
        }

        public void UpdateEdit()
        {
            _updateEdit();
        }

        public void Cleanup()
        {
            _cleanup();
        }
    }
}
