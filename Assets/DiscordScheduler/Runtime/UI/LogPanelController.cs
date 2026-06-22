using UnityEngine.UIElements;

namespace DiscordScheduler
{
    public sealed class LogPanelController
    {
        private readonly LogService _log;
        private ScrollView _logScroll;
        private Label _lblLogEmpty;

        public LogPanelController(LogService log)
        {
            _log = log;
        }

        public void Bind(VisualElement root)
        {
            if (root == null)
                return;

            _logScroll = root.Q<ScrollView>("logScroll");
            _lblLogEmpty = root.Q<Label>("lblLogEmpty");

            var btnClear = root.Q<Button>("btnLogClear");
            if (btnClear != null)
                btnClear.clicked += ClearAndRefresh;

            var btnRefresh = root.Q<Button>("btnLogRefresh");
            if (btnRefresh != null)
                btnRefresh.clicked += Refresh;
        }

        public void Refresh()
        {
            if (_logScroll == null || _log == null)
                return;

            var lines = _log.Snapshot();
            var hasLines = lines.Count > 0;

            _logScroll.Clear();
            Label lastLabel = null;
            foreach (var line in lines)
            {
                var label = new Label(line);
                label.AddToClassList("logLine");
                _logScroll.Add(label);
                lastLabel = label;
            }

            _logScroll.style.display = hasLines ? DisplayStyle.Flex : DisplayStyle.None;
            if (_lblLogEmpty != null)
                _lblLogEmpty.style.display = hasLines ? DisplayStyle.None : DisplayStyle.Flex;

            if (lastLabel != null)
                _logScroll.ScrollTo(lastLabel);
        }

        private void ClearAndRefresh()
        {
            _log?.Clear();
            Refresh();
        }
    }
}
