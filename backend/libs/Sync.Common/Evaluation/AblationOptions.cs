namespace Sync.Common.Evaluation;

/// <summary>Evaluation-only switch that turns off dedup and writes shadow rows to show what duplicates would look like.</summary>
public sealed record AblationOptions(bool Enabled)
{
    public const string ConfigKey = "Ablation:Enabled";

    public const string Warning =
        "ABLATION MODE (evaluation only, §13): duplicate protection is partly switched off and shadow rows are recorded " +
        "in the ablation schema. Unset Ablation__Enabled for normal operation.";
}
