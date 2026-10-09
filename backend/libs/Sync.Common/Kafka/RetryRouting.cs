namespace Sync.Common.Kafka;

/// <summary>
/// Retry chain from architecture §8.3 and §11: a message that fails for a transient reason is copied to the
/// next hop and the original offset is committed, so its partition keeps moving.
///
///   source topic → wound-events.retry.30s → wound-events.retry.5m → wound-events.dlq
///
/// Retry consumers pause their partition until a message's timestamp + delay has passed. Contract or
/// ordering errors (a 422 or 409 from the Recommendation Service, an unparseable payload) skip the retries
/// and go straight to the DLQ, because retrying cannot fix them.
/// </summary>
public static class RetryRouting
{
    public const string RetryCountHeader = "retry-count";
    public const string OriginalTopicHeader = "original-topic";
    public const string ErrorHeader = "last-error";

    /// <summary>Where a message that failed while being read from <paramref name="currentTopic"/> goes next.</summary>
    public static string NextHop(string currentTopic) => currentTopic switch
    {
        Topics.Retry30s => Topics.Retry5m,
        Topics.Retry5m => Topics.DeadLetter,
        Topics.DeadLetter => Topics.DeadLetter,
        _ => Topics.Retry30s,
    };

    /// <summary>How long a retry consumer waits after the message timestamp before processing it.</summary>
    public static TimeSpan DelayFor(string retryTopic) => retryTopic switch
    {
        Topics.Retry30s => TimeSpan.FromSeconds(30),
        Topics.Retry5m => TimeSpan.FromMinutes(5),
        _ => TimeSpan.Zero,
    };
}
