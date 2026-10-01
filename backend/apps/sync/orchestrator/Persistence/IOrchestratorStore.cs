using System.Text.Json;
using Orchestrator.Graph;
using Sync.Common.Contracts;
using Sync.Common.Recommendations;

namespace Orchestrator.Persistence;

/// <summary>
/// Everything the workflow reads from or writes to PostgreSQL (architecture §9, §10.1). The executors only
/// decide the flow; this is where the SQL lives, so the workflow can be tested without a database.
/// </summary>
public interface IOrchestratorStore
{
    /// <summary>
    /// True when this consumer already processed the event (messaging.inbox). Otherwise records an
    /// ORCHESTRATION_STARTED provenance row for this attempt and returns false.
    /// </summary>
    Task<bool> BeginAsync(OrchestrationJob job, CancellationToken ct);

    /// <summary>
    /// Advisory lock on the assessment, then FOR UPDATE on its latest revision (§9.3 lock order). If a higher
    /// revision is stored, marks this one SUPERSEDED, adds a SUPERSEDED change and the inbox marker, and returns true.
    /// </summary>
    Task<bool> SupersedeIfOutdatedAsync(OrchestrationJob job, CancellationToken ct);

    /// <summary>The stored assessment and the earlier captures of the same wound, or null if it is not stored.</summary>
    Task<AssessmentContext?> LoadContextAsync(PersistedEvent evt, CancellationToken ct);

    /// <summary>
    /// One advisory-locked transaction: recommendation, RECOMMENDATION_READY change, RAG_RETURNED and
    /// RECOMMENDATION_STORED provenance, outbox row for recommendations.ready, inbox marker. Returns false when
    /// the recommendation for this (assessment, revision) was already stored (a redelivery).
    /// </summary>
    Task<bool> StoreRecommendationAsync(OrchestrationJob job, RecommendationResponse response, JsonElement rawBody,
        CancellationToken ct);

    /// <summary>Adds an ADVICE_DEFERRED change for the device, once per (assessment, revision).</summary>
    Task RecordDeferredAsync(PersistedEvent evt, CancellationToken ct);
}
