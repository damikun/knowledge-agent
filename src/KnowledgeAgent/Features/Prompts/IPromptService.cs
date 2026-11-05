namespace KnowledgeAgent.Features.Prompts;

public interface IPromptService
{
    Task<PromptTemplate> GetTemplateAsync(string name, PromptSelector? selector = null, CancellationToken cancellationToken = default);

    Task<RenderedPrompt> RenderAsync(
        string name,
        IReadOnlyDictionary<string, object?> variables,
        PromptSelector? selector = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PromptSummary>> ListAsync(CancellationToken cancellationToken = default);

    string RenderTemplate(string template, IReadOnlyDictionary<string, object?> variables);
}
