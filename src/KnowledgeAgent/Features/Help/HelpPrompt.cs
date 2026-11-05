using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Features.Prompts.Rendering;

namespace KnowledgeAgent.Features.Help;

internal sealed class HelpPrompt(IPromptTemplateRenderer renderer)
{
    public const string Name = "help-assistant";
    internal const string ResourceName = "Help/prompt/help-assistant.hbs";

    private static readonly Lazy<string> Template = new(() =>
    {
        using var stream = typeof(HelpPrompt).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded help prompt {ResourceName} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().ReplaceLineEndings("\n").TrimEnd('\n');
    });

    public RenderedPrompt Render(IReadOnlyDictionary<string, object?> variables) => new()
    {
        Name = Name,
        Origin = PromptOrigin.Local,
        Type = PromptType.Text,
        Text = renderer.Render(Template.Value, variables),
    };
}
