namespace KnowledgeAgent.Features.Prompts.Rendering;

public interface IPromptTemplateRenderer
{
    string Render(string template, IReadOnlyDictionary<string, object?> variables);

    IReadOnlyList<string> ExtractVariables(string template);

    void Validate(string template) => Render(template, new Dictionary<string, object?>(StringComparer.Ordinal));
}
