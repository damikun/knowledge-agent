namespace KnowledgeAgent.Features.Agents;

public sealed class AgentConfig
{
    public const string InternalKey = "internal";
    public const int MaxKeyLength = 64;
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 500;
    public const int MaxInstructionsLength = 8_000;
    public const int MaxLanguageLength = 40;
    public const int MaxFolders = 50;
    public const int MaxSpecialists = 10;

    public Guid Id { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Instructions { get; set; }

    public string? Language { get; set; }

    public string ProfileName { get; set; } = AgentDefaults.ProfileName;

    public List<Guid> FolderIds { get; set; } = [];

    public List<string> Tools { get; set; } = [];

    // The column is still mcp_tools; the API calls these mcpApps.
    public List<AgentMcpApp> McpTools { get; set; } = [];

    public bool McpServerEnabled { get; set; }

    public bool HttpApiEnabled { get; set; }

    public bool AllowGeneralKnowledge { get; set; }

    public List<AgentSpecialist> Specialists { get; set; } = [];

    public bool ShowSpecialistName { get; set; }

    public HandoverPolicy HandoverPolicy { get; set; } = HandoverPolicy.OnRequest;

    public bool IsBuiltIn { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public AgentConfig ApplyBuiltInLimits()
    {
        if (IsBuiltIn)
        {
            McpTools = [];
            Specialists = [];
            ShowSpecialistName = false;
            McpServerEnabled = false;
            HttpApiEnabled = false;
            HandoverPolicy = HandoverPolicy.Never;
        }

        return this;
    }
}

public enum HandoverPolicy
{
    Never,

    OnRequest,

    WhenUnsure,
}

public sealed class AgentSpecialist
{
    public const int MaxWhenToUseLength = 300;

    public Guid AgentId { get; set; }

    public string WhenToUse { get; set; } = string.Empty;
}
