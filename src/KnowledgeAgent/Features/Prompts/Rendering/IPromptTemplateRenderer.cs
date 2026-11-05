namespace KnowledgeAgent.Features.Prompts.Rendering;

public interface IPromptTemplateRenderer
{
    /// <summary>Renders without HTML escaping. Values may be primitives, collections, objects or JSON nodes.</summary>
    string Render(string template, IReadOnlyDictionary<string, object?> variables);

    IReadOnlyList<string> ExtractVariables(string template);

    /// <summary>Throws a <see cref="PromptRenderException"/> when the template has a syntax error.</summary>
    void Validate(string template) => Render(template, new Dictionary<string, object?>(StringComparer.Ordinal));
}
