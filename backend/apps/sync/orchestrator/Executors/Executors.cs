namespace Orchestrator.Executors;

// One class per executor from architecture §10.1. Split into separate files as each one is implemented.

/// <summary>Skips a message this consumer already processed (unique on consumer name + eventId).</summary>
public sealed class InboxCheckExecutor;

/// <summary>Marks this revision SUPERSEDED if a higher one is already stored (SELECT ... FOR UPDATE).</summary>
public sealed class SupersedeCheckExecutor;

/// <summary>Loads healing history and strips patientRef, deviceId and facilityId.</summary>
public sealed class BuildContextExecutor;

/// <summary>POST /v1/recommendations with timeout, bounded retries and a circuit breaker.</summary>
public sealed class CallRagExecutor;

/// <summary>Checks the response schema and that every recommendation carries a citation.</summary>
public sealed class ValidateResponseExecutor;

/// <summary>Advisory-locked write: recommendation, change log, provenance, outbox, inbox marker.</summary>
public sealed class PersistResultExecutor;
