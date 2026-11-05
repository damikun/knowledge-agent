using System.Threading.Channels;

namespace KnowledgeAgent.Features.Chat.Agents;

internal sealed class AgentEventSink(ChannelWriter<ChatStreamEvent> writer, string agent, string? parentCallId)
{
    public string Agent { get; } = agent;
    public string? ParentCallId { get; } = parentCallId;

    public void Emit(ChatStreamEvent streamEvent) =>
        writer.TryWrite(streamEvent with { Agent = Agent, ParentCallId = ParentCallId });
}
