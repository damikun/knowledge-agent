using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace KnowledgeAgent.Infrastructure.VectorStore.ClickHouse;

internal static partial class ClickHouseIdentifier
{
    public const string PatternDescription = "^[A-Za-z_][A-Za-z0-9_]*$";

    public static bool IsValid([NotNullWhen(true)] string? value) => value is not null && Pattern().IsMatch(value);

    public static string Validate(string? value, string settingName)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException(InvalidMessage(value, settingName), settingName);
        }

        return value;
    }

    public static string InvalidMessage(string? value, string settingName) =>
        $"{settingName} '{value}' is not a valid ClickHouse identifier (expected {PatternDescription}).";

    public static string Quote(string identifier) => $"`{identifier}`";

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
