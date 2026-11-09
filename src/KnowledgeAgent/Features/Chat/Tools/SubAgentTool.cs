using System.Globalization;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.AI.Chat;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Features.Chat.Tools;

internal sealed class SubAgentTool(AgentDefinition agent, SubAgentToolSpec spec, AgentOptions options, TimeProvider timeProvider) : IAgentTool
{
    public string Name => spec.ToolName;

    public AIFunctionDeclaration Declaration { get; } =
        AIFunctionFactory.CreateDeclaration(spec.ToolName, spec.Description, ToolSchemas.DelegateTask());

    public TimeSpan? Timeout =>
        TimeSpan.FromSeconds(Math.Clamp(options.ToolTimeoutSeconds, 1, 3600) * (double)(agent.MaxIterations + 1));

    public async Task<ToolResult> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        var task = invocation.Arguments.GetString("task", required: true);
        var focusSourceIds = invocation.Arguments.GetGuidList("source_ids");
        invocation.Arguments.ThrowIfInvalid(Name);

        var services = invocation.Services;
        var prompts = services.GetRequiredService<IPromptService>();
        var chatClients = services.GetRequiredService<IChatClientFactory>();
        var sources = services.GetRequiredService<ISourceRepository>();

        var focusSources = new List<object?>();
        foreach (var sourceId in focusSourceIds.Where(invocation.Turn.IsInScope))
        {
            if (await sources.GetAsync(sourceId, cancellationToken) is { } source)
            {
                focusSources.Add(new Dictionary<string, object?> { ["id"] = source.Id.ToString("D"), ["title"] = source.Title });
            }
        }

        var variables = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["appName"] = options.AppName,
            ["today"] = timeProvider.GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["maxSearches"] = agent.MaxIterations,
            ["focusSources"] = focusSources,
        };
        var prompt = await prompts.RenderAsync(agent.PromptName, variables, cancellationToken: cancellationToken);

        ChatModelHandle model;
        try
        {
            model = await chatClients.CreateAsync(agent.ProfileName, prompt.ModelPatch, requestPatch: null, cancellationToken);
        }
        catch (ModelNotConfiguredException ex)
        {
            return ToolResult.Failure($"The {agent.Name} agent cannot run: {ex.Message}");
        }

        var sink = new AgentEventSink(invocation.Turn.Events, agent.Name, invocation.CallId);
        sink.Emit(new AgentStartedEvent(model.Profile.ProviderKey, model.Profile.Model));

        using var activity = AgentTelemetry.StartSubAgent(agent, invocation.CallId, task!, model.Profile, prompt.Name, prompt.Version);
        var result = await invocation.Runner.RunAsync(
            new AgentRunRequest(
                agent,
                model,
                PromptMessages.ForAgent(prompt, task!),
                new PromptReference(prompt.Name, prompt.Version),
                invocation.Turn,
                ParentCallId: invocation.CallId),
            cancellationToken);
        AgentTelemetry.CompleteAgent(activity, result);
        sink.Emit(new AgentCompletedEvent(result.FinishReason, result.Iterations));

        if (result.Error is { } error)
        {
            return ToolResult.Failure($"The {agent.Name} agent could not complete the task: {error.Message}") with
            {
                Citations = result.Citations,
            };
        }

        var answer = result.FinalText.Length > 0 ? result.FinalText : result.FullText;
        if (answer.Length == 0)
        {
            return ToolResult.Failure($"The {agent.Name} agent finished without producing a report.");
        }

        var summary = string.Create(
            CultureInfo.InvariantCulture,
            $"{DisplayName(agent.Name)} finished after {result.Iterations} step(s) with {result.Citations.Count} citation(s)");
        return new ToolResult(answer, summary, Citations: result.Citations) { Untrusted = true };
    }

    private static string DisplayName(string name) =>
        name.Length == 0 ? "Sub-agent" : char.ToUpperInvariant(name[0]) + name[1..];
}
