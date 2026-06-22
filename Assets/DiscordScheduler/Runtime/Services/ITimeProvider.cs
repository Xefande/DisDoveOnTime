using System;

namespace DiscordScheduler
{
    public interface ITimeProvider
    {
        DateTime UtcNow { get; }
    }
}
