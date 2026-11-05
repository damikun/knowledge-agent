using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Infrastructure.AI.Chat;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Chat;

internal sealed class TitleGenerator(IServiceScopeFactory scopeFactory, IOptions<AgentOptions> options, ILogger<TitleGenerator> logger)
{
    public const int MaxTitleLength = 80;
    public const int FallbackLength = 60;

    private const int MaxMessageCharsForTitle = 2_000;

    private static readonly char[] QuoteAndMarkupChars = ['"', '\'', '`', '“', '”', '„', '‘', '’', '«', '»', '*', '_', '#', ' '];
    private static readonly char[] TrailingPunctuation = ['.', '!', '?', ':', ';', ',', '…', '-', '–', '—', ' '];

    public async Task<string> GenerateAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var prompts = scope.ServiceProvider.GetRequiredService<IPromptService>();
            var chatClients = scope.ServiceProvider.GetRequiredService<IChatClientFactory>();

            var input = TextUtil.Shorten(message, MaxMessageCharsForTitle);
            var prompt = await prompts.RenderAsync(
                PromptNames.ConversationTitle,
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["message"] = input },
                cancellationToken: cancellationToken);
            var model = await chatClients.CreateAsync(options.Value.UtilityProfile, prompt.ModelPatch, requestPatch: null, cancellationToken);

            var chatOptions = model.CreateOptions();
            chatOptions.Tools = null;
            chatOptions.ToolMode = null;
            chatOptions.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            chatOptions.AdditionalProperties[AgentRunner.PromptNameProperty] = prompt.Name;
            chatOptions.AdditionalProperties[AgentRunner.PromptVersionProperty] = prompt.Version;

            var response = await model.Client.GetResponseAsync(PromptMessages.ForAgent(prompt, input), chatOptions, cancellationToken);
            var title = Clean(response.Text);
            return title.Length > 0 ? title : Fallback(message);
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                AgentLog.TitleGenerationFailed(logger, ex);
            }

            return Fallback(message);
        }
    }

    public static string Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var line = raw.Split('\n').Select(part => part.Trim()).FirstOrDefault(part => part.Length > 0) ?? string.Empty;
        line = TextUtil.CollapseWhitespace(line).Trim(QuoteAndMarkupChars);
        if (line.StartsWith("title:", StringComparison.OrdinalIgnoreCase))
        {
            line = line["title:".Length..];
        }

        line = line.Trim(QuoteAndMarkupChars).TrimEnd(TrailingPunctuation).Trim(QuoteAndMarkupChars);
        if (line.Length > MaxTitleLength)
        {
            var cut = line.LastIndexOf(' ', MaxTitleLength);
            line = (cut >= MaxTitleLength / 2 ? line[..cut] : line[..MaxTitleLength]).TrimEnd(TrailingPunctuation);
        }

        return line;
    }

    public static string Fallback(string message)
    {
        var text = TextUtil.CollapseWhitespace(message);
        if (text.Length > FallbackLength)
        {
            text = text[..FallbackLength].TrimEnd();
        }

        return text.Length > 0 ? text : ChatService.DefaultTitle;
    }
}
