using System.Text.Json;

namespace KnowledgeAgent.Features.Chat.Agents;

internal sealed class ToolCallLog
{
    private readonly Lock _gate = new();
    private readonly List<Entry> _entries = [];

    public Entry Start(string callId, string toolName, string agent, string? parentCallId, JsonElement? arguments)
    {
        var entry = new Entry(callId, toolName, agent, parentCallId, arguments);
        lock (_gate)
        {
            _entries.Add(entry);
        }

        return entry;
    }

    public IReadOnlyList<ToolCallDto> Snapshot()
    {
        lock (_gate)
        {
            return _entries.Select(entry => entry.ToDto()).ToList();
        }
    }

    internal sealed class Entry(string callId, string toolName, string agent, string? parentCallId, JsonElement? arguments)
    {
        private readonly Lock _gate = new();
        private string _summary = string.Empty;
        private bool _isError;
        private long _durationMs;
        private string? _result;
        private ToolCallConfirmationDto? _confirmation;

        public void Complete(string summary, bool isError, long durationMs, string? result = null, ToolCallConfirmationDto? confirmation = null)
        {
            lock (_gate)
            {
                _summary = summary;
                _isError = isError;
                _durationMs = durationMs;
                _result = result;
                _confirmation = confirmation;
            }
        }

        public ToolCallDto ToDto()
        {
            lock (_gate)
            {
                return new ToolCallDto(callId, toolName, agent, parentCallId, arguments, _summary, _isError, _durationMs)
                {
                    Result = _result,
                    Confirmation = _confirmation,
                };
            }
        }
    }
}
