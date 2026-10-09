namespace Housekeeping;

/// <summary>Pure retention decisions, kept apart from the SQL so they can be unit-tested.</summary>
public static class RetentionPolicy
{
    /// <summary>How old an inbox row must be before deletion, or null to keep everything this cycle.</summary> <param name="topicRetentions">Each topic's retention.ms (-1 = forever); null if Kafka couldn't be reached.</param>
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
