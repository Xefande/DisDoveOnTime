using UnityEngine.UIElements;

namespace DiscordScheduler
{
    public static class UiActionState
    {
        private const string DisabledClass = "actionDisabled";
        private const float DisabledOpacity = 0.55f;

        public static void SetEnabled(Button button, bool enabled)
        {
            if (button == null)
                return;

            button.SetEnabled(enabled);
            button.EnableInClassList(DisabledClass, !enabled);
            button.style.opacity = enabled ? 1f : DisabledOpacity;
        }
    }
}
