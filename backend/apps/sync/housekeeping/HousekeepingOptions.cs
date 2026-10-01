namespace Housekeeping;

/// <summary>Retention rules (architecture §9.4). Section "Housekeeping" in configuration.</summary>
public sealed class HousekeepingOptions
{
    public const string Section = "Housekeeping";

    /// <summary>How often a cleanup cycle runs.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>A published outbox row is kept this long (for debugging), then deleted. Unpublished rows never are.</summary>
    public TimeSpan OutboxRetention { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// A change-log row must be at least this old before it can be archived, even once every device cursor has passed
    /// it. Far above pull's 60-second re-send window (§7.2), so an out-of-order commit is never archived early.
    /// </summary>
    public TimeSpan ChangeLogMargin { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Minimum age of an inbox row before it is deleted. The effective value is never shorter than the longest
    /// retention of the topics its consumers read plus <see cref="InboxSafetyMargin"/>: while Kafka can still redeliver
    /// a message, the inbox row that recognises it as a repeat must exist (<see cref="RetentionPolicy"/>).
    /// </summary>
    public TimeSpan InboxRetention { get; set; } = TimeSpan.FromDays(8);

    public TimeSpan InboxSafetyMargin { get; set; } = TimeSpan.FromDays(1);

    /// <summary>Rows per DELETE statement, so no single statement holds locks for long.</summary>
    public int BatchSize { get; set; } = 5000;

    public static readonly TimeSpan MinChangeLogMargin = TimeSpan.FromMinutes(10);

    public void Validate()
    {
        if (Interval <= TimeSpan.Zero) throw new InvalidOperationException("Housekeeping:Interval must be positive.");
        if (OutboxRetention < TimeSpan.Zero) throw new InvalidOperationException("Housekeeping:OutboxRetention must not be negative.");
        if (ChangeLogMargin < MinChangeLogMargin)
            throw new InvalidOperationException(
                $"Housekeeping:ChangeLogMargin must be at least {MinChangeLogMargin} (pull re-sends the last 60 s, §7.2).");
        if (InboxRetention <= TimeSpan.Zero || InboxSafetyMargin < TimeSpan.Zero)
            throw new InvalidOperationException("Housekeeping:InboxRetention must be positive and InboxSafetyMargin not negative.");
        if (BatchSize is < 1 or > 100_000) throw new InvalidOperationException("Housekeeping:BatchSize must be 1-100000.");
    }
}
