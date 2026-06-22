using System;

namespace DiscordScheduler
{
    public sealed class PostApplicationService
    {
        private readonly PostFormValidator _validator;
        private readonly PostMediaService _media;
        private readonly ITimeProvider _time;
        private readonly MutationGuardService _mutationGuard;

        public PostApplicationService(PostFormValidator validator, PostMediaService media, ITimeProvider time, MutationGuardService mutationGuard = null)
        {
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _media = media ?? throw new ArgumentNullException(nameof(media));
            _time = time ?? throw new ArgumentNullException(nameof(time));
            _mutationGuard = mutationGuard;
        }

        public ValidationResult CreatePost(PostDraft draft, AppDatabase db, out ScheduledPost post)
        {
            post = null;

            var ready = ValidateReady(draft, db);
            if (!ready.ok)
                return ready;

            var candidate = ScheduledPost.CreateNew(draft.targetId);
            var nowIso = TimeUtil.ToIsoUtc(_time.UtcNow);
            candidate.createdAtUtcIso = nowIso;
            candidate.updatedAtUtcIso = nowIso;

            ApplyDraft(candidate, draft);

            var mediaResult = _media.TryPrepareMedia(draft.mediaPath, candidate.id, out var kind, out var storedPath);
            if (!mediaResult.ok)
                return mediaResult;

            candidate.SetMedia(kind, storedPath);
            candidate.updatedAtUtcIso = nowIso;
            db.posts.Add(candidate);
            post = candidate;
            return ValidationResult.Ok();
        }

        public ValidationResult UpdatePost(ScheduledPost post, PostDraft draft, AppDatabase db)
        {
            if (post == null)
                return ValidationResult.Fail("Missing post.");

            var mutation = _mutationGuard?.CanEditPost(post);
            if (mutation != null && !mutation.ok)
                return mutation;

            var ready = ValidateReady(draft, db);
            if (!ready.ok)
                return ready;

            var snapshot = Capture(post);
            ApplyDraft(post, draft);

            var mediaResult = _media.TryPrepareMedia(draft.mediaPath, post.id, out var kind, out var storedPath);
            if (!mediaResult.ok)
            {
                Restore(post, snapshot);
                return mediaResult;
            }

            post.SetMedia(kind, storedPath);
            post.updatedAtUtcIso = TimeUtil.ToIsoUtc(_time.UtcNow);
            return ValidationResult.Ok();
        }

        private ValidationResult ValidateReady(PostDraft draft, AppDatabase db)
        {
            if (db == null || db.posts == null)
                return ValidationResult.Fail("Post database is not available.");

            return _validator.Validate(draft, db);
        }

        private static void ApplyDraft(ScheduledPost post, PostDraft draft)
        {
            post.targetId = draft.targetId;
            post.title = (draft.title ?? "").Trim();
            post.body = draft.body ?? "";
            post.sendAsEmbed = draft.sendAsEmbed;
            post.allowedMentions = CloneAllowedMentions(draft.allowedMentions);
            post.missedPolicyIfOff = draft.missedPolicyIfOff;
            post.missedPolicyIfSleep = draft.missedPolicyIfSleep;

            if (TimeUtil.TryLocalBudapestToUtc(draft.dateYmd, draft.timeHm, out var scheduledUtc, out _))
                post.scheduledAtUtcIso = TimeUtil.ToIsoUtc(scheduledUtc);
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

        private static PostSnapshot Capture(ScheduledPost post)
        {
            return new PostSnapshot
            {
                targetId = post.targetId,
                title = post.title,
                body = post.body,
                scheduledAtUtcIso = post.scheduledAtUtcIso,
                imagePath = post.imagePath,
                mediaKind = post.mediaKind,
                mediaPath = post.mediaPath,
                allowedMentions = CloneAllowedMentions(post.allowedMentions),
                missedPolicyIfOff = post.missedPolicyIfOff,
                missedPolicyIfSleep = post.missedPolicyIfSleep,
                sendAsEmbed = post.sendAsEmbed,
                updatedAtUtcIso = post.updatedAtUtcIso
            };
        }

        private static void Restore(ScheduledPost post, PostSnapshot snapshot)
        {
            post.targetId = snapshot.targetId;
            post.title = snapshot.title;
            post.body = snapshot.body;
            post.scheduledAtUtcIso = snapshot.scheduledAtUtcIso;
            post.imagePath = snapshot.imagePath;
            post.mediaKind = snapshot.mediaKind;
            post.mediaPath = snapshot.mediaPath;
            post.allowedMentions = CloneAllowedMentions(snapshot.allowedMentions);
            post.missedPolicyIfOff = snapshot.missedPolicyIfOff;
            post.missedPolicyIfSleep = snapshot.missedPolicyIfSleep;
            post.sendAsEmbed = snapshot.sendAsEmbed;
            post.updatedAtUtcIso = snapshot.updatedAtUtcIso;
        }

        private sealed class PostSnapshot
        {
            public string targetId;
            public string title;
            public string body;
            public string scheduledAtUtcIso;
            public string imagePath;
            public MediaKind mediaKind;
            public string mediaPath;
            public AllowedMentions allowedMentions;
            public MissedPolicy missedPolicyIfOff;
            public MissedPolicy missedPolicyIfSleep;
            public bool sendAsEmbed;
            public string updatedAtUtcIso;
        }
    }
}
