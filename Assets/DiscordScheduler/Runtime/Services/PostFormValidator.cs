using System.Linq;

namespace DiscordScheduler
{
    public sealed class PostFormValidator
    {
        private readonly DiscordPayloadValidator _payloadValidator;

        public PostFormValidator(DiscordPayloadValidator payloadValidator = null)
        {
            _payloadValidator = payloadValidator ?? new DiscordPayloadValidator();
        }

        public ValidationResult Validate(PostDraft draft, AppDatabase db)
        {
            if (draft == null)
                return ValidationResult.Fail("Missing post draft.");

            if (string.IsNullOrWhiteSpace(draft.targetId))
                return ValidationResult.Fail("Target is required.");

            var target = db?.targets?.FirstOrDefault(t => t != null && t.id == draft.targetId);
            if (target == null)
                return ValidationResult.Fail("Target does not exist.");

            var payload = _payloadValidator.Validate(target, draft);
            if (!payload.ok)
                return payload;

            if (!TimeUtil.TryLocalBudapestToUtc(draft.dateYmd, draft.timeHm, out _, out var timeError))
                return ValidationResult.Fail(string.IsNullOrWhiteSpace(timeError) ? "Invalid schedule date or time." : timeError);

            return ValidationResult.Ok();
        }
    }
}
