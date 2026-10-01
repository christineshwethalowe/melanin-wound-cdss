namespace Housekeeping;

/// <summary>Pure retention decisions, kept apart from the SQL so they can be unit-tested.</summary>
public static class RetentionPolicy
{
    /// <summary>
    /// How old an inbox row must be before it is deleted, or null to keep every row this cycle.
    /// </summary>
    /// <param name="topicRetentions">
    /// retention.ms of each topic the inbox consumers read, as Kafka reports it; null when Kafka could not be asked.
    /// A negative value means "kept forever" (Kafka's -1).
    /// </param>
    public static TimeSpan? InboxRetention(HousekeepingOptions options, IReadOnlyCollection<long>? topicRetentions)
    {
        // Without Kafka's answer the redelivery window is unknown: keep everything rather than guess.
        if (topicRetentions is null || topicRetentions.Count == 0) return null;
        // A topic that is never trimmed can redeliver anything, so every inbox row stays needed.
        if (topicRetentions.Any(ms => ms < 0)) return null;

        var redelivery = TimeSpan.FromMilliseconds(topicRetentions.Max()) + options.InboxSafetyMargin;
        return redelivery > options.InboxRetention ? redelivery : options.InboxRetention;
    }
}
