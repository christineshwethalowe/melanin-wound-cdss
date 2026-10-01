using Json.Schema;
using Microsoft.Agents.AI.Workflows;
using Orchestrator.Clients;
using Orchestrator.Executors;
using Orchestrator.Persistence;

namespace Orchestrator.Graph;

/// <summary>
/// The orchestrator's Agent Framework workflow (architecture §10.1): six typed executors joined by
/// conditional edges. The workflow coordinates, retries and records. It never decides clinical content;
/// any language model sits behind the Recommendation Service boundary, where its output is verified.
///
/// Crash safety comes from Kafka and the database rather than from workflow checkpoints: the offset is
/// committed only after the run's outcome is durable, so a killed worker's message is redelivered and the
/// run repeats; InboxCheck and the unique (assessment_id, revision) constraint make the repeat harmless (§11).
/// </summary>
public sealed class OrchestratorGraph(IOrchestratorStore store, IRecommendationClient client, JsonSchema responseSchema)
{
    /// <summary>A fresh workflow per run, so runs on different consumers never share executor instances.</summary>
    public Workflow Create()
    {
        var inbox = new InboxCheckExecutor(store);
        var supersede = new SupersedeCheckExecutor(store);
        var context = new BuildContextExecutor(store);
        var callRag = new CallRagExecutor(client);
        var validate = new ValidateResponseExecutor(responseSchema);
        var persist = new PersistResultExecutor(store);

        return new WorkflowBuilder(inbox)
            .WithName("wound-recommendation")
            .WithDescription("InboxCheck → SupersedeCheck → BuildContext → CallRag → ValidateResponse → PersistResult")
            // Each step continues only on success; otherwise it has already yielded the run's outcome.
            .AddEdge<InboxChecked>(inbox, supersede, m => m is { AlreadyProcessed: false })
            .AddEdge<SupersedeChecked>(supersede, context, m => m is { Superseded: false })
            .AddEdge<ContextBuilt>(context, callRag, m => m is { Request: not null })
            .AddEdge<RagCallResult>(callRag, validate, m => m is { Status: RagCallStatus.Ok })
            .AddEdge<ValidatedRecommendation>(validate, persist, m => m is { Error: null })
            .WithOutputFrom(inbox, supersede, context, callRag, validate, persist)
            .Build();
    }
}
