using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using KnowledgeAgent.Features.Prompts;

namespace KnowledgeAgent.Tests.Features.Chat.Fakes;

public sealed record PromptRender(string Name, IReadOnlyDictionary<string, object?> Variables, string Text);

public sealed partial class FakePromptService : IPromptService
{
    public const string OrchestratorTemplate = "You are {{agentName}} ({{appName}}). Today is {{today}}. Ready sources: {{sourceCount}}.";
    public const string ResearcherTemplate = "You are the researcher of {{appName}}. Use at most {{maxSearches}} searches.";
    public const string TitleTemplate = "Write a short title for: {{message}}";

    private readonly ConcurrentDictionary<string, PromptTemplate> _templates = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<PromptRender> _renders = new();
    private readonly ConcurrentQueue<string> _templateFetches = new();

    public FakePromptService()
    {
        SetTemplate(PromptNames.OrchestratorSystem, OrchestratorTemplate, version: 3);
        SetTemplate(PromptNames.ResearcherSystem, ResearcherTemplate, version: 2);
        SetTemplate(PromptNames.ConversationTitle, TitleTemplate, version: 1);
    }

    public IReadOnlyList<PromptRender> Renders => _renders.ToList();

    public IReadOnlyList<string> TemplateFetches => _templateFetches.ToList();

    public string? FailRenderOf { get; set; }

    public void SetTemplate(string name, string text, int version = 1, JsonObject? config = null, PromptOrigin origin = PromptOrigin.Langfuse) =>
        _templates[name] = new PromptTemplate
        {
            Name = name,
            Version = version,
            Type = PromptType.Text,
            Text = text,
            Config = config,
            Labels = ["production"],
            Origin = origin,
        };

    public IReadOnlyList<PromptRender> RendersOf(string name) => Renders.Where(render => render.Name == name).ToList();

    public Task<PromptTemplate> GetTemplateAsync(string name, PromptSelector? selector = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _templateFetches.Enqueue(name);
        return _templates.TryGetValue(name, out var template)
            ? Task.FromResult(template)
            : Task.FromException<PromptTemplate>(new PromptNotFoundException(name));
    }

    public Task<RenderedPrompt> RenderAsync(
        string name,
        IReadOnlyDictionary<string, object?> variables,
        PromptSelector? selector = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailRenderOf == name)
        {
            return Task.FromException<RenderedPrompt>(new PromptRenderException($"Template '{name}' is broken."));
        }

        if (!_templates.TryGetValue(name, out var template))
        {
            return Task.FromException<RenderedPrompt>(new PromptNotFoundException(name));
        }

        var snapshot = new Dictionary<string, object?>(variables, StringComparer.Ordinal);
        var rendered = new RenderedPrompt
        {
            Name = template.Name,
            Version = template.Version,
            Origin = template.Origin,
            Type = template.Type,
            Text = template.Text is null ? null : Render(template.Text, snapshot),
            Messages = template.Messages.Select(message => message with { Content = Render(message.Content, snapshot) }).ToList(),
            Config = template.Config,
            ModelPatch = PromptConfig.ParseModelPatch(template.Config),
            Profile = PromptConfig.ParseProfile(template.Config),
        };

        _renders.Enqueue(new PromptRender(name, snapshot, rendered.Text ?? string.Join("\n", rendered.Messages.Select(m => m.Content))));
        return Task.FromResult(rendered);
    }

    public Task<IReadOnlyList<PromptSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PromptSummary>>(_templates.Values
            .Select(template => new PromptSummary(template.Name, template.Origin, template.Version, template.Type, template.Labels, [], null))
            .ToList());

    public string RenderTemplate(string template, IReadOnlyDictionary<string, object?> variables) => Render(template, variables);

    private static string Render(string template, IReadOnlyDictionary<string, object?> variables) =>
        VariablePattern().Replace(template, match =>
            variables.TryGetValue(match.Groups[1].Value, out var value) ? Format(value) : string.Empty);

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        bool flag => flag ? "true" : "false",
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? string.Empty,
        JsonElement element => element.GetRawText(),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        ICollection collection => collection.Count.ToString(CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    [GeneratedRegex(@"\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*\}\}")]
    private static partial Regex VariablePattern();
}
