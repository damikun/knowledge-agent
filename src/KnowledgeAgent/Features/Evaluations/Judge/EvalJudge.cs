using System.Diagnostics;
using System.Text.Json;
using KnowledgeAgent.Features.Evaluations.Datasets;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.AI.Chat;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using KnowledgeAgent.Infrastructure.Telemetry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Evaluations.Judge;

public sealed record JudgePassage(string Ref, string Title, string Text);

public sealed record JudgeOutcome(EvalJudgementDto Judgement, IReadOnlyDictionary<string, double> Metrics);

public sealed class EvalJudge(
    IPromptService prompts,
    IChatClientFactory chatClients,
    string profileName,
    string environment,
    ILogger logger)
{
    public const string PromptName = "eval-judge";
    public const string DefaultProfile = "judge";

    internal const int MaxPassages = 24;
    internal const int MaxPassageChars = 1_500;
    internal const int MaxAnswerChars = 8_000;

    private static readonly ActivitySource Source = new(KnowledgeAgentTelemetry.Evaluation);

    private static readonly (string Name, string Metric)[] Criteria =
    [
        ("faithfulness", EvalMetrics.JudgeFaithfulness),
        ("relevance", EvalMetrics.JudgeRelevance),
        ("correctness", EvalMetrics.JudgeCorrectness),
    ];

    public async Task<(string? Model, string? Unavailable)> DescribeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var handle = await CreateModelAsync(cancellationToken);
            return ($"{handle.Profile.ProviderKey}/{handle.Profile.Model}", null);
        }
        catch (ModelNotConfiguredException ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<JudgeOutcome> JudgeAsync(
        EvalItem item,
        string answer,
        IReadOnlyList<JudgePassage> passages,
        IReadOnlyDictionary<string, string> traceMetadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        string? model = null;

        // Each judgement is its own trace, separate from the experiment item it grades.
        var previous = Activity.Current;
        Activity.Current = null;
        using var activity = Source.StartActivity("eval.judge", ActivityKind.Internal, parentContext: default);
        try
        {
            activity?.SetTag("langfuse.trace.name", "eval-judge");
            activity?.SetTag("langfuse.observation.type", "evaluator");
            activity?.SetTag("langfuse.environment", environment);
            activity?.SetTag("langfuse.observation.input", item.Question);
            foreach (var (key, value) in traceMetadata)
            {
                activity?.SetTag("langfuse.trace.metadata." + key, value);
            }

            var prompt = await prompts.RenderAsync(PromptName, BuildVariables(item, answer, passages), cancellationToken: cancellationToken);
            var patch = prompt.Profile is null || prompt.Profile.Equals(profileName, StringComparison.OrdinalIgnoreCase) ? prompt.ModelPatch : null;
            var handle = await chatClients.CreateAsync(profileName, patch, requestPatch: null, cancellationToken);
            model = $"{handle.Profile.ProviderKey}/{handle.Profile.Model}";

            var messages = ToMessages(prompt);
            var options = handle.CreateOptions();
            options.AdditionalProperties ??= [];
            options.AdditionalProperties[KnowledgeAgentTelemetry.LangfusePromptNameKey] = prompt.Name;
            options.AdditionalProperties[KnowledgeAgentTelemetry.LangfusePromptVersionKey] = prompt.Version;

            var response = await handle.Client.GetResponseAsync(messages, options, cancellationToken);
            if (!JudgeResponseParser.TryParse(response.Text, out var criteria, out var problem))
            {
                // One retry: models occasionally ignore the requested format.
                messages.AddRange(response.Messages);
                messages.Add(new ChatMessage(ChatRole.User, "Your reply was not the requested JSON object. Reply with only the JSON object."));
                response = await handle.Client.GetResponseAsync(messages, handle.CreateOptions(), cancellationToken);
                if (!JudgeResponseParser.TryParse(response.Text, out criteria, out problem))
                {
                    return Failed(model, "The judge's reply could not be read: " + problem);
                }
            }

            var metrics = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var (name, metric) in Criteria)
            {
                if (criteria.FirstOrDefault(criterion => criterion.Name == name) is { } criterion)
                {
                    metrics[metric] = criterion.Score;
                }
            }

            activity?.SetTag("langfuse.observation.output", JsonSerializer.Serialize(criteria.ToDictionary(c => c.Name, c => c.Verdict)));
            return new JudgeOutcome(new EvalJudgementDto(model, criteria, null), metrics);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The judge failed for eval item {ItemId}", item.Id);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Failed(model, "The judge failed: " + ex.Message);
        }
        finally
        {
            Activity.Current = previous;
        }
    }

    internal static Dictionary<string, object?> BuildVariables(EvalItem item, string answer, IReadOnlyList<JudgePassage> passages) => new(StringComparer.Ordinal)
    {
        ["question"] = item.Question,
        ["answer"] = Clip(string.IsNullOrWhiteSpace(answer) ? "(empty answer)" : answer.Trim(), MaxAnswerChars),
        ["answerable"] = item.Answerable,
        ["referenceAnswer"] = item.ReferenceAnswer,
        ["expectedFacts"] = item.ExpectedFacts.Select(fact => (object?)fact.Split('|')[0].Trim()).ToList(),
        ["passages"] = passages
            .Take(MaxPassages)
            .Select(passage => (object?)new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["ref"] = passage.Ref,
                ["title"] = passage.Title,
                ["text"] = Clip(passage.Text.Trim(), MaxPassageChars),
            })
            .ToList(),
    };

    internal static List<ChatMessage> ToMessages(RenderedPrompt prompt)
    {
        if (prompt.Type == PromptType.Text || prompt.Messages.Count == 0)
        {
            return [new ChatMessage(ChatRole.User, prompt.Text ?? string.Empty)];
        }

        return prompt.Messages
            .Select(message => new ChatMessage(
                message.Role.Trim().ToLowerInvariant() switch
                {
                    "system" => ChatRole.System,
                    "assistant" => ChatRole.Assistant,
                    _ => ChatRole.User,
                },
                message.Content))
            .ToList();
    }

    private async Task<ChatModelHandle> CreateModelAsync(CancellationToken cancellationToken)
    {
        ModelProfilePatch? patch = null;
        try
        {
            var template = await prompts.GetTemplateAsync(PromptName, selector: null, cancellationToken);
            var configProfile = PromptConfig.ParseProfile(template.Config);
            patch = configProfile is null || configProfile.Equals(profileName, StringComparison.OrdinalIgnoreCase)
                ? PromptConfig.ParseModelPatch(template.Config)
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not read the {Prompt} prompt config; using the profile's settings", PromptName);
        }

        return await chatClients.CreateAsync(profileName, patch, requestPatch: null, cancellationToken);
    }

    private static JudgeOutcome Failed(string? model, string error) =>
        new(new EvalJudgementDto(model, [], error), new Dictionary<string, double>());

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max].TrimEnd() + "…";
}
