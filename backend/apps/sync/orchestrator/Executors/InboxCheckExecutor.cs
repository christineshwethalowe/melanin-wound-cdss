using Microsoft.Agents.AI.Workflows;
using Npgsql;
using Orchestrator.Graph;
using Sync.Common.Contracts;

namespace Orchestrator.Executors;

/// <summary>
/// Skips a message this consumer already processed (architecture §10.1): messaging.inbox is unique on
/// (consumer_name, event_id). The relay can publish a row twice after a crash; this is where the repeat stops.
/// On a database error the message goes to a retry topic.
/// </summary>
public sealed class InboxCheckExecutor(NpgsqlDataSource db) : Executor<PersistedEvent, InboxChecked>("InboxCheck")
{
    public const string ConsumerName = "orchestrator";

    public override ValueTask<InboxChecked> HandleAsync(PersistedEvent message, IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        // TODO(phase 6): SELECT 1 FROM messaging.inbox WHERE consumer_name = @ConsumerName AND event_id = @eventId.
        // The inbox row itself is written by PersistResult, in the same transaction as the recommendation.
        _ = db;
        throw new NotImplementedException("Plan phase 6: InboxCheck");
    }
}
