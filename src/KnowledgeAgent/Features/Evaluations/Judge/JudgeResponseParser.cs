using System.Text.Json;
using System.Text.Json.Nodes;

namespace KnowledgeAgent.Features.Evaluations.Judge;

public static class JudgeResponseParser
{
    public static bool TryParse(string? text, out IReadOnlyList<EvalCriterionDto> criteria, out string? problem)
    {
        criteria = [];
        problem = null;

        // Models sometimes wrap the object in a code fence or prose.
        var start = text?.IndexOf('{') ?? -1;
        var end = text?.LastIndexOf('}') ?? -1;
        if (start < 0 || end <= start)
        {
            problem = "no JSON object found";
            return false;
        }

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(text![start..(end + 1)]) as JsonObject;
        }
        catch (JsonException ex)
        {
            problem = "invalid JSON (" + ex.Message + ")";
            return false;
        }

        if (root is null)
        {
            problem = "the reply is not a JSON object";
            return false;
        }

        var parsed = new List<EvalCriterionDto>();
        foreach (var name in new[] { "faithfulness", "relevance", "correctness" })
        {
            if (root[name] is not JsonObject criterion)
            {
                problem = $"\"{name}\" is missing";
                return false;
            }

            var verdict = Verdict(String(criterion["verdict"]));
            if (verdict is null)
            {
                problem = $"\"{name}.verdict\" must be yes, partially or no";
                return false;
            }

            var issues = criterion["issues"] is JsonArray array
                ? array.Select(String).Where(issue => !string.IsNullOrWhiteSpace(issue)).Select(issue => issue!.Trim()).ToList()
                : [];
            parsed.Add(new EvalCriterionDto(name, verdict, Score(verdict), String(criterion["reasoning"])?.Trim() ?? string.Empty, issues));
        }

        criteria = parsed;
        return true;
    }

    internal static string? Verdict(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "yes" or "pass" or "passed" or "true" or "full" or "fully" => "yes",
        "partially" or "partial" or "somewhat" or "mixed" => "partially",
        "no" or "fail" or "failed" or "false" or "none" => "no",
        _ => null,
    };

    internal static double Score(string verdict) => verdict switch
    {
        "yes" => 1,
        "partially" => 0.5,
        _ => 0,
    };

    private static string? String(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : node?.ToJsonString();
}
