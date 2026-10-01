namespace Sync.Common.Evaluation;

/// <summary>
/// The duplicate ablation (architecture §13): <c>Ablation__Enabled=true</c> switches off the gateway's DUPLICATE answer
/// and the orchestrator's inbox, and makes the persister and orchestrator record shadow rows in the ablation schema
/// (no unique constraints). The real tables keep their protections, so the system stays correct; the shadow tables
/// show what a store without the mechanisms would hold. Evaluation only, never in normal operation.
/// </summary>
public sealed record AblationOptions(bool Enabled)
{
    public const string ConfigKey = "Ablation:Enabled";

    public const string Warning =
        "ABLATION MODE (evaluation only, §13): duplicate protection is partly switched off and shadow rows are recorded " +
        "in the ablation schema. Unset Ablation__Enabled for normal operation.";
}
