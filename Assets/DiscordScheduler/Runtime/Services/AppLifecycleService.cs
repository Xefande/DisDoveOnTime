using System.Linq;

namespace DiscordScheduler
{
    public sealed class AppLifecycleService
    {
        private readonly PostStateMachine _stateMachine;
        private bool _shutdownStarted;

        public AppLifecycleService(PostStateMachine stateMachine)
        {
            _stateMachine = stateMachine;
        }

        public bool ShutdownStarted => _shutdownStarted;

        public void MarkShutdownStarted()
        {
            _shutdownStarted = true;
        }

        public ValidationResult CanStartSend()
        {
            return _shutdownStarted
                ? ValidationResult.Fail("Application shutdown is in progress; send start blocked.")
                : ValidationResult.Ok();
        }

        public int RecoverStaleSendingPosts(AppDatabase db, string reason)
        {
            if (db?.posts == null || _stateMachine == null)
                return 0;

            var recovered = 0;
            foreach (var post in db.posts.Where(post => post != null && post.status == PostStatus.Sending))
            {
                var result = _stateMachine.RecoverStaleSending(post, reason);
                if (result.ok)
                    recovered++;
            }

            return recovered;
        }

        public ValidationResult MarkActiveSendAmbiguousOnShutdown(ScheduledPost post, string reason)
        {
            MarkShutdownStarted();
            if (post == null || post.status != PostStatus.Sending)
                return ValidationResult.Ok();

            return _stateMachine.RecoverStaleSending(post, reason);
        }
    }
}
