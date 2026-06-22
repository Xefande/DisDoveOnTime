namespace DiscordScheduler
{
    public sealed class SettingsApplicationService
    {
        public const int MinSleepThresholdMinutes = 1;
        public const int MaxSleepThresholdMinutes = 999999;

        public ValidationResult Normalize(AppDatabase db)
        {
            if (db == null)
                return ValidationResult.Fail("Settings database is not available.");

            if (db.settings == null)
                db.settings = new AppSettings();

            NormalizeSettings(db.settings);
            return ValidationResult.Ok();
        }

        public ValidationResult Apply(AppDatabase db, SettingsDraft draft)
        {
            if (db == null)
                return ValidationResult.Fail("Settings database is not available.");

            if (draft == null)
                return ValidationResult.Fail("Missing settings draft.");

            db.settings = new AppSettings
            {
                sleepThresholdMinutes = ClampSleepThreshold(draft.sleepThresholdMinutes),
                defaultAllowedMentions = CloneAllowedMentions(draft.defaultAllowedMentions),
                defaultOffPolicy = ClampPolicy(draft.defaultOffPolicyIndex),
                defaultSleepPolicy = ClampPolicy(draft.defaultSleepPolicyIndex)
            };

            return ValidationResult.Ok();
        }

        public SettingsDraft ToDraft(AppSettings settings)
        {
            settings = settings ?? new AppSettings();
            NormalizeSettings(settings);

            return new SettingsDraft
            {
                sleepThresholdMinutes = settings.sleepThresholdMinutes,
                defaultAllowedMentions = CloneAllowedMentions(settings.defaultAllowedMentions),
                defaultOffPolicyIndex = (int)settings.defaultOffPolicy,
                defaultSleepPolicyIndex = (int)settings.defaultSleepPolicy
            };
        }

        private static void NormalizeSettings(AppSettings settings)
        {
            settings.sleepThresholdMinutes = ClampSleepThreshold(settings.sleepThresholdMinutes);
            settings.defaultAllowedMentions = CloneAllowedMentions(settings.defaultAllowedMentions);
            settings.defaultOffPolicy = ClampPolicy((int)settings.defaultOffPolicy);
            settings.defaultSleepPolicy = ClampPolicy((int)settings.defaultSleepPolicy);
        }

        private static int ClampSleepThreshold(int value)
        {
            if (value < MinSleepThresholdMinutes)
                return MinSleepThresholdMinutes;

            if (value > MaxSleepThresholdMinutes)
                return MaxSleepThresholdMinutes;

            return value;
        }

        private static MissedPolicy ClampPolicy(int index)
        {
            return index < 0 || index > 2
                ? MissedPolicy.SendOnNextRun
                : (MissedPolicy)index;
        }

        private static AllowedMentions CloneAllowedMentions(AllowedMentions mentions)
        {
            if (mentions == null)
                return new AllowedMentions();

            return new AllowedMentions
            {
                allowUsers = mentions.allowUsers,
                allowRoles = mentions.allowRoles,
                allowEveryone = mentions.allowEveryone,
                userIdsCsv = mentions.userIdsCsv ?? "",
                roleIdsCsv = mentions.roleIdsCsv ?? ""
            };
        }
    }
}
