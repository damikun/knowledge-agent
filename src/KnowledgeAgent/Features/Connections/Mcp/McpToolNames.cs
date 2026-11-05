using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace KnowledgeAgent.Features.Connections.Mcp;

// The names the model sees: mcp_<server key>__<tool>, within the 64 letters, digits, _ and - that model providers allow.
public static partial class McpToolNames
{
    public const string Prefix = "mcp_";
    public const string Separator = "__";
    private const int MaxLength = 64;

    public static string For(string serverKey, string toolName)
    {
        var start = Prefix + serverKey + Separator;
        var tool = Unsafe().Replace(toolName, "_");
        if (start.Length + tool.Length <= MaxLength && tool == toolName)
        {
            return start + tool;
        }

        // Shortened or changed names get a hash of the original, so two tools never end up with the same name.
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(toolName)))[..6];
        var room = Math.Max(1, MaxLength - start.Length - hash.Length - 1);
        return start + (tool.Length > room ? tool[..room] : tool) + "_" + hash;
    }

    public static bool IsMcp(string name) => name.StartsWith(Prefix, StringComparison.Ordinal);

    public static string KeyFromName(string name)
    {
        var key = new string(name.Normalize(NormalizationForm.FormD).ToLowerInvariant().Where(c => c is >= 'a' and <= 'z' or >= '0' and <= '9').ToArray());
        if (key.Length == 0 || !char.IsAsciiLetterLower(key[0]))
        {
            key = "server" + key;
        }

        return key.Length > McpServer.MaxKeyLength ? key[..McpServer.MaxKeyLength] : key;
    }

    [GeneratedRegex("[^A-Za-z0-9_-]")]
    private static partial Regex Unsafe();
}
