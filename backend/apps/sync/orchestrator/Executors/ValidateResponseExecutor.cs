using System.Text.Json;
using Json.Schema;
using Microsoft.Agents.AI.Workflows;
using Orchestrator.Graph;
using Sync.Common.Contracts;

namespace Orchestrator.Executors;

/// <summary>
/// Checks the response against contracts/rag-response.schema.json, that it answers the case and revision that
/// were asked, and that every section's citation tags resolve in the citations list (architecture §10.1).
/// It does not re-judge clinical content: that stays behind the Recommendation Service boundary.
/// Invalid → Deferred (retry topics, then DLQ).
/// </summary>
[YieldsOutput(typeof(OrchestrationOutcome))]
public sealed class ValidateResponseExecutor(JsonSchema responseSchema)
    : Executor<RagCallResult, ValidatedRecommendation>("ValidateResponse")
{
    private static readonly EvaluationOptions SchemaOptions = new() { OutputFormat = OutputFormat.List };

    public override async ValueTask<ValidatedRecommendation> HandleAsync(RagCallResult message, IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var body = message.Body ?? default;
        var (response, error) = Validate(responseSchema, message.Request, body);
        if (error is not null)
            await context.YieldOutputAsync(new OrchestrationOutcome(message.Job.Event, OutcomeKind.Deferred,
                $"INVALID_RESPONSE: {error}"), cancellationToken);
        return new ValidatedRecommendation(message.Job, response, body, error);
    }

    public static (RecommendationResponse? Response, string? Error) Validate(JsonSchema schema,
        RecommendationRequest request, JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object) return (null, "empty body");

        var result = schema.Evaluate(body, SchemaOptions);
        if (!result.IsValid)
        {
            var first = result.Details?.Where(d => d.Errors is { Count: > 0 })
                .Select(d => $"{d.InstanceLocation}: {d.Errors!.Values.First()}").FirstOrDefault();
            return (null, first ?? "does not match rag-response schema");
        }

        var response = body.Deserialize<RecommendationResponse>(JsonSerializerOptions.Web)!;
        if (response.CaseId != request.CaseId || response.Revision != request.Revision)
            return (null, $"answers case {response.CaseId} rev {response.Revision}, asked {request.CaseId} rev {request.Revision}");

        var tags = response.Citations.Select(c => c.Tag).ToHashSet();
        var unresolved = response.Sections.SelectMany(s => s.CitationTags).Where(t => !tags.Contains(t)).Distinct().ToList();
        if (unresolved.Count > 0) return (null, $"unresolved citation tags {string.Join(", ", unresolved)}");

        return (response, null);
    }
}
