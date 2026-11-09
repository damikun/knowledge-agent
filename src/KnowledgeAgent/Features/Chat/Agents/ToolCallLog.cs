using System.Text.Json;

namespace KnowledgeAgent.Features.Chat.Agents;

internal sealed class ToolCallLog
{
    private readonly Lock _gate = new();
    private readonly List<Entry> _entries = [];

    public Entry Start(string callId, string toolName, string agent, string? parentCallId, JsonElement? arguments, string? preamble = null)
    {
        var entry = new Entry(callId, toolName, agent, parentCallId, arguments, preamble);
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

    public IReadOnlySet<string> FinishedCallIds()
    {
        lock (_gate)
        {
            return _entries.Where(entry => entry.Finished).Select(entry => entry.CallId).ToHashSet(StringComparer.Ordinal);
        }
    }

    internal sealed class Entry(string callId, string toolName, string agent, string? parentCallId, JsonElement? arguments, string? preamble)
    {
        private readonly Lock _gate = new();
        private bool _finished;
        private string _summary = string.Empty;
        private bool _isError;
        private long _durationMs;
        private string? _result;
        private ToolCallConfirmationDto? _confirmation;

        public string CallId => callId;

        public bool Finished
        {
            get
            {
                lock (_gate)
                {
                    return _finished;
                }
            }
        }

        public void Complete(string summary, bool isError, long durationMs, string? result = null, ToolCallConfirmationDto? confirmation = null)
        {
            lock (_gate)
            {
                _finished = true;
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
                    Preamble = preamble,
                };
            }
        }
    }
}
