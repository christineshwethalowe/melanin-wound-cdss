using System.Text.Json;
using System.Text.Json.Nodes;
using Sync.Common.Contracts;

namespace Sync.Common.Recommendations;

/// <summary>
/// Turns a stored assessment into the RAG contract v1.0 request (architecture §10.1 BuildContext, §10.2), for both
/// the orchestrator and the REST baseline (§13.1).
/// Data minimisation happens here: the request is built field by field from what the contract allows, so
/// patientRef, deviceId, facilityId and fitzpatrickClass can never cross the boundary. caseId is the assessment id.
/// </summary>
public static class RecommendationRequestMapper
{
    public const string NotRecorded = "not_recorded";

    /// <summary>The clinicalAssessment keys the Recommendation Service requires (rag-request.schema.json).</summary>
    public static readonly string[] RagClinicalFields =
        ["ulcerLocation", "protectiveSensation", "pedalPulses", "probeToBone", "infectionGrade", "woundBedLabels"];

    public static RecommendationRequest Build(AssessmentContext ctx) => new(
        RecommendationRequest.CurrentContractVersion,
        ctx.AssessmentId,
        ctx.Revision,
        ctx.CapturedAt,
        ctx.ReceivedAt,
        Analytics(ctx.Analytics),
        Clinical(ctx.ClinicalAssessment),
        ctx.History
            .OrderBy(h => h.CapturedAt)
            .Select(h =>
            {
                var a = Analytics(h.Analytics);
                return new HealingHistoryPoint(h.CapturedAt, a.AreaMm2, a.Pipeline);
            })
            .ToList());

    private static RagWoundAnalytics Analytics(JsonElement stored)
    {
        var pipeline = stored.GetProperty("pipeline");
        return new RagWoundAnalytics(
            stored.GetProperty("areaMm2").GetDouble(),
            stored.GetProperty("colourRegions").EnumerateArray()
                .Select(r => new ColourRegion(r.GetProperty("cluster").GetInt32(), r.GetProperty("percent").GetDouble()))
                .ToList(),
            new PipelineVersions(pipeline.GetProperty("calibration").GetString()!,
                pipeline.GetProperty("segmentation").GetString()!));
    }

    /// <summary>
    /// Copies every field the clinician recorded on the device unchanged. The device form does not collect
    /// ulcerLocation, probeToBone, infectionGrade or woundBedLabels yet (wound-event 1.0; final field list is the
    /// open team item in §15), so those are sent as not_recorded: true, because nobody recorded them. A field
    /// that IS in the wound-event contract is never defaulted: the gateway rejects events that leave one out.
    /// </summary>
    private static JsonElement Clinical(JsonElement stored)
    {
        var result = new JsonObject();
        foreach (var field in RagClinicalFields)
        {
            result[field] = stored.TryGetProperty(field, out var value)
                ? JsonNode.Parse(value.GetRawText())
                : JsonValue.Create(NotRecorded);
        }
        return JsonSerializer.SerializeToElement(result);
    }
}
