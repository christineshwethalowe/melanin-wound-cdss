using System.Text.Json;

namespace Sync.Common.Recommendations;

/// <summary>
/// A stored assessment and the earlier captures of the same wound: what both the orchestrator's BuildContext
/// step and the REST baseline turn into a Recommendation Service request (architecture §10.1, §13.1).
/// </summary>
public sealed record AssessmentContext(
    Guid AssessmentId,
    int Revision,
    DateTimeOffset CapturedAt,
    DateTimeOffset ReceivedAt,
    JsonElement Analytics,
    JsonElement ClinicalAssessment,
    IReadOnlyList<HistoryRow> History);

/// <summary>An earlier capture of the same wound (latest revision of each earlier assessment).</summary>
public sealed record HistoryRow(DateTimeOffset CapturedAt, JsonElement Analytics);
