using Microsoft.Agents.AI.Workflows;
using Npgsql;
using Orchestrator.Clients;
using Orchestrator.Executors;

namespace Orchestrator.Graph;

/// <summary>
/// The orchestrator's Agent Framework workflow (architecture §10.1): six typed executors joined by
/// conditional edges. The framework checkpoints at superstep boundaries, so a crashed worker can resume.
/// The workflow coordinates, retries and records — it never decides clinical content; any language model
/// sits behind the Recommendation Service boundary, where its output is verified.
/// </summary>
public static class OrchestratorGraph
{
    public static Workflow Build(NpgsqlDataSource db, IRecommendationClient client)
    {
        var inbox = new InboxCheckExecutor(db);
        var supersede = new SupersedeCheckExecutor(db);
        var context = new BuildContextExecutor(db);
        var callRag = new CallRagExecutor(client);
        var validate = new ValidateResponseExecutor();
        var persist = new PersistResultExecutor(db);

        return new WorkflowBuilder(inbox)
            .WithName("wound-recommendation")
            .WithDescription("InboxCheck → SupersedeCheck → BuildContext → CallRag → ValidateResponse → PersistResult")
            // Stop early when the message was already handled or a newer revision exists.
            .AddEdge<InboxChecked>(inbox, supersede, m => m is { AlreadyProcessed: false })
            .AddEdge<SupersedeChecked>(supersede, context, m => m is { Superseded: false })
            .AddEdge(context, callRag)
            // Only successful calls are validated; Deferred and ContractError end the run for the consumer to route.
            .AddEdge<RagCallResult>(callRag, validate, m => m is { Status: RagCallStatus.Ok })
            .AddEdge(validate, persist)
            .WithOutputFrom(persist)
            .Build();
    }
}
