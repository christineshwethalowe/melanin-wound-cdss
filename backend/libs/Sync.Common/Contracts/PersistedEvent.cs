namespace Sync.Common.Contracts;

/// <summary>
/// Message on wound-events.persisted, written to the outbox by the persister and consumed by the
/// orchestrator. It carries only identifiers; the orchestrator reads the stored assessment itself.
/// </summary>
public sealed record PersistedEvent(Guid EventId, Guid AssessmentId, int Revision, Guid WoundId);
