using System.Text;
using System.Text.Json;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.AI.Chat;
using KnowledgeAgent.Infrastructure.Jobs;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Labels;

internal sealed partial class AutoLabelJob(
    IConversationRepository conversations,
    ILabelRepository labels,
    IPromptService prompts,
    IChatClientFactory chatClients,
    IOptions<AgentOptions> options,
    TimeProvider timeProvider,
    ILogger<AutoLabelJob> logger) : IJobHandler
{
    public const string Kind = "conversations.auto-label";
    public const int MaxLabels = 2;

    private const int MaxTranscriptChars = 8_000;
    private const int MaxMessageChars = 1_500;
    private const string Instruction = "Choose the labels for this conversation.";

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(context.Key, out var conversationId)
            || await conversations.GetAsync(conversationId, includeTurns: true, cancellationToken) is not { } conversation
            || conversation.AutoLabeledAt is not null)
        {
            return;
        }

        var removed = (await labels.ListRowsAsync(conversationId, cancellationToken)).Where(row => row.Removed).Select(row => row.LabelId).ToHashSet();
        var candidates = (await labels.ListAutoApplyAsync(cancellationToken)).Where(label => !removed.Contains(label.Id)).ToList();
        var transcript = Transcript(conversation.Messages);
        if (candidates.Count == 0 || transcript is null)
        {
            return;
        }

        string answer;
        try
        {
            var prompt = await prompts.RenderAsync(
                PromptNames.ConversationLabels,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["labels"] = candidates.Select(label => new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["id"] = label.Id.ToString(),
                        ["name"] = label.Name,
                        ["description"] = label.Description,
                    }).ToList(),
                    ["transcript"] = transcript,
                    ["maxLabels"] = MaxLabels,
                },
                cancellationToken: cancellationToken);
            var model = await chatClients.CreateAsync(options.Value.UtilityProfile, prompt.ModelPatch, requestPatch: null, cancellationToken);
            var chatOptions = model.CreateOptions();
            chatOptions.Tools = null;
            chatOptions.ToolMode = null;
            chatOptions.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            chatOptions.AdditionalProperties[AgentRunner.PromptNameProperty] = prompt.Name;
            chatOptions.AdditionalProperties[AgentRunner.PromptVersionProperty] = prompt.Version;
            var response = await model.Client.GetResponseAsync(PromptMessages.ForAgent(prompt, Instruction), chatOptions, cancellationToken);
            answer = response.Text;
        }
        catch (ModelNotConfiguredException ex)
        {
            LogNoModel(logger, conversationId, ex.Message);
            return;
        }

        var picked = Parse(answer, candidates);
        if (await labels.ApplyAutoAsync(conversationId, picked, timeProvider.GetUtcNow(), cancellationToken))
        {
            LogLabeled(logger, conversationId, picked.Count);
        }
    }

    internal static IReadOnlyList<Guid> Parse(string? answer, IReadOnlyList<Label> candidates)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return [];
        }

        var values = new List<string>();
        var start = answer.IndexOfAny(['{', '[']);
        if (start >= 0)
        {
            var end = answer.LastIndexOf(answer[start] == '{' ? '}' : ']');
            if (end > start)
            {
                try
                {
                    using var document = JsonDocument.Parse(answer[start..(end + 1)]);
                    var root = document.RootElement;
                    var array = root.ValueKind == JsonValueKind.Object
                        && root.EnumerateObject().FirstOrDefault(property => property.NameEquals("labels") || property.NameEquals("Labels")) is { Value.ValueKind: JsonValueKind.Array } property
                        ? property.Value
                        : root;
                    if (array.ValueKind == JsonValueKind.Array)
                    {
                        values.AddRange(array.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!));
                    }
                }
                catch (JsonException)
                {
                }
            }
        }

        var byId = candidates.ToDictionary(label => label.Id);
        var byName = candidates.GroupBy(label => label.Name.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        return values
            .Select(value => Guid.TryParse(value, out var id) && byId.TryGetValue(id, out var label)
                ? label
                : byName.GetValueOrDefault(value.Trim()))
            .OfType<Label>()
            .Select(label => label.Id)
            .Distinct()
            .Take(MaxLabels)
            .ToList();
    }

    internal static string? Transcript(IReadOnlyList<ConversationMessage> messages)
    {
        if (!messages.Any(message => message.Author is MessageAuthor.User or MessageAuthor.Visitor))
        {
            return null;
        }

        var text = new StringBuilder();
        foreach (var message in messages.OrderBy(message => message.Sequence))
        {
            var who = message.Author switch
            {
                MessageAuthor.User or MessageAuthor.Visitor => "Customer",
                MessageAuthor.Assistant => "Assistant",
                MessageAuthor.Staff => "Support team",
                _ => null,
            };
            if (who is null || string.IsNullOrWhiteSpace(message.Text))
            {
                continue;
            }

            var line = $"{who}: {TextUtil.Shorten(TextUtil.CollapseWhitespace(message.Text), MaxMessageChars)}\n";
            if (text.Length + line.Length > MaxTranscriptChars)
            {
                break;
            }

            text.Append(line);
        }

        return text.Length > 0 ? text.ToString().TrimEnd() : null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Put {Count} labels on conversation {ConversationId}")]
    private static partial void LogLabeled(ILogger logger, Guid conversationId, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipped labeling conversation {ConversationId}: {Reason}")]
    private static partial void LogNoModel(ILogger logger, Guid conversationId, string reason);
}
