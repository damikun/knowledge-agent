using HandlebarsDotNet;
using HandlebarsDotNet.IO;

namespace KnowledgeAgent.Features.Prompts.Rendering;

// Without this, Handlebars.Net prints True and culture-specific dates such as 22.10.2026, unlike Handlebars.js.
internal sealed class InvariantValueFormatterProvider : IFormatterProvider
{
    private static readonly Formatter Instance = new();

    public bool TryCreateFormatter(Type type, out IFormatter formatter)
    {
        if (type == typeof(bool)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(DateOnly)
            || type == typeof(TimeOnly))
        {
            formatter = Instance;
            return true;
        }

        formatter = null!;
        return false;
    }

    private sealed class Formatter : IFormatter
    {
        public void Format<T>(T value, in EncodedTextWriter writer) => writer.Write(TemplateValues.ToText(value), false);
    }
}
