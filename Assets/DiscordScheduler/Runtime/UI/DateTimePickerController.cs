using System;
using UnityEngine.UIElements;

namespace DiscordScheduler
{
    public sealed class DateTimePickerController
    {
        private readonly Action<TextField> _showDatePicker;
        private readonly Action<TextField> _showTimePicker;
        private readonly Func<bool> _isModalVisible;

        public DateTimePickerController(
            Action<TextField> showDatePicker,
            Action<TextField> showTimePicker,
            Func<bool> isModalVisible)
        {
            _showDatePicker = showDatePicker ?? throw new ArgumentNullException(nameof(showDatePicker));
            _showTimePicker = showTimePicker ?? throw new ArgumentNullException(nameof(showTimePicker));
            _isModalVisible = isModalVisible ?? throw new ArgumentNullException(nameof(isModalVisible));
        }

        public void BindDateField(TextField field)
        {
            BindField(field, _showDatePicker);
        }

        public void BindTimeField(TextField field)
        {
            BindField(field, _showTimePicker);
        }

        private void BindField(TextField field, Action<TextField> openPicker)
        {
            if (field == null)
                return;

            field.isReadOnly = true;
            field.pickingMode = PickingMode.Position;

            void OpenPicker()
            {
                if (_isModalVisible())
                    return;

                openPicker(field);
            }

            void OnPointerDown(PointerDownEvent pointerDownEvent)
            {
                if (pointerDownEvent.button != 0)
                    return;

                OpenPicker();
                pointerDownEvent.StopPropagation();
            }

            field.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);

            var input =
                field.Q(className: "unity-text-field__input") ??
                field.Q(className: "unity-base-text-field__input") ??
                field.Q(className: "unity-text-field__input-field") ??
                field.Q(className: "unity-base-text-field__input-field");

            if (input == null)
                return;

            input.pickingMode = PickingMode.Position;
            input.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
        }
    }
}
