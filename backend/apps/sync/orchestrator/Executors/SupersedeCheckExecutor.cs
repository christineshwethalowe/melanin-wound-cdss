using Microsoft.Agents.AI.Workflows;
using Npgsql;
using Orchestrator.Graph;

namespace Orchestrator.Executors;

/// <summary>
/// If a higher revision of the same assessment is already stored, marks this one SUPERSEDED and ends the
/// run (architecture §10.1). Needed because retry topics can reorder revisions.
/// Takes the advisory lock on assessment_id, then SELECT ... FOR UPDATE on the latest row (§9.3 lock order).
/// Terminal: no failure path.
/// </summary>
public sealed class SupersedeCheckExecutor(NpgsqlDataSource db) : Executor<InboxChecked, SupersedeChecked>("SupersedeCheck")
{
    public override ValueTask<SupersedeChecked> HandleAsync(InboxChecked message, IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        // TODO(phase 6): compare message.Event.Revision with MAX(revision) for the assessment; if lower,
        // UPDATE status = 'SUPERSEDED', add a SUPERSEDED change_log row, and yield OutcomeKind.Superseded.
        _ = db;
        throw new NotImplementedException("Plan phase 6: SupersedeCheck");
    }
}
