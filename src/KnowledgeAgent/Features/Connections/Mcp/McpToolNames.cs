using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace KnowledgeAgent.Features.Connections.Mcp;

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
