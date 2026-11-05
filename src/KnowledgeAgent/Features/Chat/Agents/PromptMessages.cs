using KnowledgeAgent.Features.Prompts;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Features.Chat.Agents;

internal static class PromptMessages
{
    public static string ToSystemText(RenderedPrompt prompt)
    {
        if (prompt.Type == PromptType.Text || prompt.Messages.Count == 0)
        {
            return prompt.Text ?? string.Empty;
        }

        var system = prompt.Messages.Where(message => message.ToChatRole() == ChatRole.System).Select(message => message.Content).ToList();
        return string.Join("\n\n", system.Count > 0 ? system : prompt.Messages.Select(message => message.Content));
    }

    public static List<ChatMessage> ForAgent(RenderedPrompt prompt, string userInput)
    {
        var messages = new List<ChatMessage>();
        if (prompt.Type == PromptType.Chat && prompt.Messages.Count > 0)
        {
            messages.AddRange(prompt.Messages.Select(message => new ChatMessage(message.ToChatRole(), message.Content)));
        }
        else if (!string.IsNullOrEmpty(prompt.Text))
        {
            messages.Add(new ChatMessage(ChatRole.System, prompt.Text));
        }

        messages.Add(new ChatMessage(ChatRole.User, userInput));
        return messages;
    }
}
