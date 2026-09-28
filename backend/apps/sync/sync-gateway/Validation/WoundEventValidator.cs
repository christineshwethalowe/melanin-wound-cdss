namespace SyncGateway.Validation;

/// <summary>
/// Validates each incoming event against contracts/wound-event.schema.json before producing it.
/// Same schema file the Flutter app and the CI contract tests use (architecture §5).
/// </summary>
public sealed class WoundEventValidator
{
    // TODO(m4): load the schema from contracts/ with JsonSchema.Net and enforce the 16 KB cap.
}
