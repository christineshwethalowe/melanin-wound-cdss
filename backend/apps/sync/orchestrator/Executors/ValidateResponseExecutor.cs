using Microsoft.Agents.AI.Workflows;
using Orchestrator.Graph;

namespace Orchestrator.Executors;

/// <summary>
/// Checks the response against contracts/rag-response.schema.json and that every section carries at least
/// one citation whose tag resolves in the citations list (architecture §10.1). It does not re-judge clinical
/// content: that stays behind the Recommendation Service boundary.
/// Invalid → retry topic, then DLQ.
/// </summary>
public sealed class ValidateResponseExecutor() : Executor<RagCallResult, ValidatedRecommendation>("ValidateResponse")
{
    public override ValueTask<ValidatedRecommendation> HandleAsync(RagCallResult message, IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        // TODO(phase 6): schema check (JsonSchema.Net, as in the gateway) + citation check; deserialize into
        // RecommendationResponse; keep the raw JSON for storage.
        throw new NotImplementedException("Plan phase 6: ValidateResponse");
    }
}
