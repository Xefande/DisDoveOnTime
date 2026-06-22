using System.Collections.Generic;
using System.Linq;

namespace DiscordScheduler
{
    public sealed class SendQueueService
    {
        private readonly Queue<string> _queue = new Queue<string>();
        private readonly HashSet<string> _queuedIds = new HashSet<string>();
        private readonly PostStateMachine _stateMachine;
        private string _activePostId = "";
        private SendCommandSnapshot _activeSnapshot;

        public bool HasActiveSend => !string.IsNullOrEmpty(_activePostId);
        public string ActivePostId => _activePostId;
        public int Count => _queue.Count;

        public SendQueueService(PostStateMachine stateMachine)
        {
            _stateMachine = stateMachine;
        }

        public ValidationResult TryEnqueue(ScheduledPost post)
        {
            if (post == null)
                return ValidationResult.Fail("Missing post.");

            if (string.IsNullOrWhiteSpace(post.id))
                return ValidationResult.Fail("Missing post id.");

            if (IsQueuedOrActive(post.id))
                return ValidationResult.Fail("Post is already queued or active.");

            var mark = _stateMachine.TryMarkSending(post);
            if (!mark.ok)
                return mark;

            _queue.Enqueue(post.id);
            _queuedIds.Add(post.id);
            return ValidationResult.Ok();
        }

        public bool TryDequeueNext(AppDatabase db, out ScheduledPost post)
        {
            post = null;

            if (HasActiveSend)
                return false;

            while (_queue.Count > 0)
            {
                var id = _queue.Dequeue();
                _queuedIds.Remove(id);

                post = db?.posts?.FirstOrDefault(x => x != null && x.id == id);
                if (post == null)
                    continue;

                if (post.status != PostStatus.Sending)
                {
                    post = null;
                    continue;
                }

                return true;
            }

            return false;
        }

        public void MarkActive(string postId)
        {
            _activePostId = postId ?? "";
            _activeSnapshot = null;
        }

        public void SetActiveSnapshot(SendCommandSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.postId))
                return;

            if (!string.Equals(_activePostId, snapshot.postId, System.StringComparison.Ordinal))
                return;

            _activeSnapshot = snapshot;
        }

        public void MarkFinished(string postId)
        {
            if (string.Equals(_activePostId, postId ?? "", System.StringComparison.Ordinal))
            {
                _activePostId = "";
                _activeSnapshot = null;
            }
        }

        public void MarkFinished(string postId, string attemptId)
        {
            if (!IsActiveAttempt(postId, attemptId))
                return;

            _activePostId = "";
            _activeSnapshot = null;
        }

        public SendCommandSnapshot GetActiveSnapshot()
        {
            return _activeSnapshot;
        }

        public bool IsActiveAttempt(string postId, string attemptId)
        {
            if (!IsActive(postId))
                return false;

            if (_activeSnapshot == null)
                return string.IsNullOrWhiteSpace(attemptId);

            return string.Equals(_activeSnapshot.postId ?? "", postId ?? "", System.StringComparison.Ordinal) &&
                   string.Equals(_activeSnapshot.attemptId ?? "", attemptId ?? "", System.StringComparison.Ordinal);
        }

        public bool IsQueuedOrActive(string postId)
        {
            if (string.IsNullOrWhiteSpace(postId))
                return false;

            return _queuedIds.Contains(postId) || string.Equals(_activePostId, postId, System.StringComparison.Ordinal);
        }

        public bool IsActive(string postId)
        {
            return !string.IsNullOrWhiteSpace(postId) &&
                   string.Equals(_activePostId, postId, System.StringComparison.Ordinal);
        }

        public bool IsQueued(string postId)
        {
            return !string.IsNullOrWhiteSpace(postId) && _queuedIds.Contains(postId);
        }

        public bool CancelQueued(string postId)
        {
            if (!IsQueued(postId))
                return false;

            _queuedIds.Remove(postId);
            var remaining = _queue.Where(id => !string.Equals(id, postId, System.StringComparison.Ordinal)).ToList();
            _queue.Clear();
            foreach (var id in remaining)
                _queue.Enqueue(id);

            return true;
        }
    }
}
