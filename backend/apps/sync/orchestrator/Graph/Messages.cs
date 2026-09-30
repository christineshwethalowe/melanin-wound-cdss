using System.Text.Json;
using Sync.Common.Contracts;

namespace Orchestrator.Graph;

// Typed messages passed along the workflow edges (architecture §10.1):
//
//   PersistedEvent ─► InboxCheck ─► SupersedeCheck ─► BuildContext ─► CallRag ─► ValidateResponse ─► PersistResult
//                         │               │                              │               │
//                         └ already done  └ superseded                   └ deferred      └ invalid
//                           (end)           (end)                          (retry topic)   (retry, then DLQ)
//
// Every run ends by yielding one OrchestrationOutcome, which the consumer uses to decide whether to commit
// the Kafka offset or route the message to a retry topic / the DLQ.

public sealed record InboxChecked(PersistedEvent Event, bool AlreadyProcessed);

public sealed record SupersedeChecked(PersistedEvent Event, bool Superseded);

public sealed record RagCallResult(PersistedEvent Event, RecommendationRequest Request, RagCallStatus Status,
    JsonElement? Body, string? Error);

public enum RagCallStatus
{
    /// <summary>200 (generated or extractive — both are successes, §10.3).</summary>
    Ok,
    /// <summary>Timeout, 503 or open circuit: record ADVICE_DEFERRED and retry via the retry topics.</summary>
    Deferred,
    /// <summary>422 or 409: a contract or ordering bug. No retry; straight to the DLQ.</summary>
    ContractError,
}

public sealed record ValidatedRecommendation(PersistedEvent Event, RecommendationResponse Response, JsonElement RawBody);

public sealed record OrchestrationOutcome(PersistedEvent Event, OutcomeKind Kind, string? Detail = null);

public enum OutcomeKind
{
    Stored,            // recommendation + change log + provenance + outbox written; commit the offset
    AlreadyProcessed,  // inbox hit; commit the offset
    Superseded,        // a newer revision exists; marked SUPERSEDED; commit the offset
    Deferred,          // ADVICE_DEFERRED recorded; send to the next retry topic, then commit
    DeadLetter,        // contract error or invalid response after retries; send to the DLQ, then commit
}
