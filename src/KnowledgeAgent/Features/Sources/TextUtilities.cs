using System.Security.Cryptography;
using System.Text;

namespace KnowledgeAgent.Features.Sources;

internal static class TextUtilities
{
    public static string Truncate(string value, int maxLength, string ellipsis = "")
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, ellipsis.Length + 1);
        if (value.Length <= maxLength)
        {
            return value;
        }

        var cut = maxLength - ellipsis.Length;
        if (char.IsHighSurrogate(value[cut - 1]))
        {
            cut--;
        }

        return value[..cut].TrimEnd() + ellipsis;
    }

    public static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
