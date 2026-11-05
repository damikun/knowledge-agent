using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.Web;
using KnowledgeAgent.Features.Connections.Mcp;
using KnowledgeAgent.Tests.Features.Connections;
using KnowledgeAgent.Tests.Features.Chat.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace KnowledgeAgent.Tests.Features.Agents;

public sealed class AgentServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly InMemoryAgentRepository _agents = new();
    private readonly InMemorySourceFolderRepository _folders = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Agents_are_created_with_defaults_and_normalized_input()
    {
        var folder = _folders.Add("Support");

        var agent = await Service().CreateAsync(
            new SaveAgentRequest(
                "  Help desk  ",
                Key: " Help-Desk ",
                Instructions: " Be brief. ",
                Language: "",
                FolderIds: [folder.Id, folder.Id, Guid.Empty],
                Tools: [ToolNames.ReadSource, ToolNames.SearchKnowledgeBase, ToolNames.SearchKnowledgeBase]),
            Ct);

        agent.Key.ShouldBe("help-desk");
        agent.Name.ShouldBe("Help desk");
        agent.Instructions.ShouldBe("Be brief.");
        agent.Language.ShouldBeNull();
        agent.ProfileName.ShouldBe(AgentDefaults.ProfileName);
        agent.FolderIds.ShouldBe([folder.Id]);
        agent.Tools.ShouldBe([ToolNames.SearchKnowledgeBase, ToolNames.ReadSource], "tools keep the catalog order");
        agent.IsBuiltIn.ShouldBeFalse();
        agent.CreatedAt.ShouldBe(_time.GetUtcNow());

        var defaults = await Service().CreateAsync(new SaveAgentRequest("All tools", Key: "all-tools"), Ct);
        defaults.Tools.ShouldBe(AgentDefaults.Tools);
        defaults.FolderIds.ShouldBeEmpty();
        defaults.HandoverPolicy.ShouldBe(HandoverPolicy.OnRequest);
    }

    [Fact]
    public async Task The_handover_policy_is_kept_unless_given_and_the_former_handover_tool_is_dropped()
    {
        var service = Service();
        var agent = await service.CreateAsync(
            new SaveAgentRequest("Shop", Key: "shop", Tools: [ToolNames.SearchKnowledgeBase, ToolNames.RequestHumanHandover], HandoverPolicy: HandoverPolicy.WhenUnsure),
            Ct);

        agent.HandoverPolicy.ShouldBe(HandoverPolicy.WhenUnsure);
        agent.Tools.ShouldBe([ToolNames.SearchKnowledgeBase], "an older client may still send the tool");
        AgentDefaults.Tools.ShouldNotContain(ToolNames.RequestHumanHandover);

        var renamed = await service.UpdateAsync(agent.Id, new SaveAgentRequest("Shop help"), Ct);
        renamed.ShouldNotBeNull().HandoverPolicy.ShouldBe(HandoverPolicy.WhenUnsure);
        var never = await service.UpdateAsync(agent.Id, new SaveAgentRequest("Shop help", HandoverPolicy: HandoverPolicy.Never), Ct);
        never.ShouldNotBeNull().HandoverPolicy.ShouldBe(HandoverPolicy.Never);

        var error = await Should.ThrowAsync<ValidationFailedException>(() => service.UpdateAsync(agent.Id, new SaveAgentRequest("Shop", HandoverPolicy: (HandoverPolicy)7), Ct));
        error.Errors.Keys.ShouldBe(["handoverPolicy"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-leading-dash")]
    [InlineData("has space")]
    [InlineData("ümlaut")]
    public async Task Keys_must_be_slugs(string? key)
    {
        var error = await Should.ThrowAsync<ValidationFailedException>(() => Service().CreateAsync(new SaveAgentRequest("Name", Key: key), Ct));

        error.Errors.Keys.ShouldBe(["key"]);
    }

    [Fact]
    public async Task Keys_are_unique_and_cannot_change()
    {
        var service = Service();
        var agent = await service.CreateAsync(new SaveAgentRequest("Sales", Key: "sales"), Ct);

        var duplicate = await Should.ThrowAsync<ValidationFailedException>(() => service.CreateAsync(new SaveAgentRequest("Sales 2", Key: "SALES"), Ct));
        duplicate.Errors["key"].ShouldHaveSingleItem().ShouldContain("already exists");

        var renamed = await Should.ThrowAsync<ValidationFailedException>(() => service.UpdateAsync(agent.Id, new SaveAgentRequest("Sales", Key: "other"), Ct));
        renamed.Errors["key"].ShouldHaveSingleItem().ShouldContain("cannot change");

        var updated = await service.UpdateAsync(agent.Id, new SaveAgentRequest("Sales team", Key: "Sales", Description: "Pricing questions"), Ct);
        updated.ShouldNotBeNull().Name.ShouldBe("Sales team");
        updated.Key.ShouldBe("sales");
        updated.Description.ShouldBe("Pricing questions");
        (await service.UpdateAsync(Guid.NewGuid(), new SaveAgentRequest("Missing"), Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Every_invalid_field_is_reported_at_once()
    {
        var error = await Should.ThrowAsync<ValidationFailedException>(() => Service().CreateAsync(
            new SaveAgentRequest(
                " ",
                Key: "valid",
                Instructions: new string('a', AgentConfig.MaxInstructionsLength + 1),
                ProfileName: "embedding",
                FolderIds: [Guid.NewGuid()],
                Tools: ["shell"]),
            Ct));

        error.Errors.Keys.ShouldBe(["name", "instructions", "profileName", "tools", "folderIds"], ignoreOrder: true);
        error.Errors["profileName"].ShouldHaveSingleItem().ShouldContain("orchestrator");
        _agents.Count.ShouldBe(0);
    }

    [Fact]
    public async Task The_built_in_agent_can_be_neither_saved_nor_deleted()
    {
        var builtin = _agents.Add(new AgentConfig { Id = Guid.NewGuid(), Key = AgentConfig.InternalKey, Name = "Knowledge Agent", IsBuiltIn = true });
        var service = Service();

        var save = await Should.ThrowAsync<ValidationFailedException>(() => service.UpdateAsync(builtin.Id, new SaveAgentRequest("Company brain", Instructions: "Answer in English."), Ct));
        var delete = await Should.ThrowAsync<ValidationFailedException>(() => service.DeleteAsync(builtin.Id, Ct));

        save.Errors["id"].ShouldBe(["The built-in agent is part of the product and cannot be changed or deleted."]);
        delete.Errors.Keys.ShouldBe(["id"]);
        (await _agents.GetAsync(builtin.Id, Ct)).ShouldNotBeNull().Name.ShouldBe("Knowledge Agent", "its row stays for its conversations");

        var other = await service.CreateAsync(new SaveAgentRequest("Other", Key: "other"), Ct);
        (await service.DeleteAsync(other.Id, Ct)).ShouldBeTrue();
        (await service.DeleteAsync(other.Id, Ct)).ShouldBeFalse();
    }

    [Fact]
    public void A_built_in_row_saved_with_apps_specialists_handover_or_use_elsewhere_reads_without_them()
    {
        var row = new AgentConfig
        {
            Key = AgentConfig.InternalKey,
            IsBuiltIn = true,
            McpTools = [new AgentMcpApp { ServerId = Guid.NewGuid() }],
            Specialists = [new AgentSpecialist { AgentId = Guid.NewGuid(), WhenToUse = "Invoices" }],
            ShowSpecialistName = true,
            HandoverPolicy = HandoverPolicy.WhenUnsure,
            McpServerEnabled = true,
            HttpApiEnabled = true,
        }.ApplyBuiltInLimits();

        (row.McpTools.Count, row.Specialists.Count, row.ShowSpecialistName, row.HandoverPolicy, row.McpServerEnabled, row.HttpApiEnabled)
            .ShouldBe((0, 0, false, HandoverPolicy.Never, false, false));

        var other = new AgentConfig { Key = "front", McpTools = [new AgentMcpApp { ServerId = Guid.NewGuid() }], HttpApiEnabled = true }.ApplyBuiltInLimits();
        (other.McpTools.Count, other.HttpApiEnabled).ShouldBe((1, true));
    }

    [Fact]
    public async Task No_agent_can_be_limited_to_the_Internal_folder_or_its_subfolders()
    {
        var internalFolder = _folders.AddInternal();
        var hr = _folders.Add("HR", internalFolder.Id);
        var docs = _folders.Add("Docs");
        var service = Service();

        foreach (var folderId in new[] { internalFolder.Id, hr.Id })
        {
            var error = await Should.ThrowAsync<ValidationFailedException>(() => service.CreateAsync(new SaveAgentRequest("Website", Key: "website", FolderIds: [docs.Id, folderId]), Ct));
            error.Errors["folderIds"].ShouldBe(["The Internal folder and its subfolders are only for the help assistant."]);
        }

        var website = await service.CreateAsync(new SaveAgentRequest("Website", Key: "website", FolderIds: [docs.Id]), Ct);
        await Should.ThrowAsync<ValidationFailedException>(() => service.UpdateAsync(website.Id, new SaveAgentRequest("Website", FolderIds: [hr.Id]), Ct));
    }

    [Fact]
    public async Task Agents_in_use_cannot_be_deleted()
    {
        var agent = await Service().CreateAsync(new SaveAgentRequest("Website", Key: "website"), Ct);

        var error = await Should.ThrowAsync<ValidationFailedException>(() => Service(new FixedUsage(["the widget 'Help'"])).DeleteAsync(agent.Id, Ct));

        error.Errors["id"].ShouldHaveSingleItem().ShouldContain("the widget 'Help'");
        (await Service(new FixedUsage([])).DeleteAsync(agent.Id, Ct)).ShouldBeTrue();
    }

    [Fact]
    public void Options_list_the_tools_and_chat_profiles()
    {
        var options = Service().GetOptions();

        options.Tools.ShouldBe(AgentDefaults.Tools);
        options.Profiles.ShouldBe(["fast", "orchestrator"]);
    }

    [Fact]
    public async Task Connected_apps_are_checked_and_picks_the_app_dropped_since_are_kept_until_removed()
    {
        var servers = new InMemoryMcpServerRepository();
        var shop = new McpServer
        {
            Id = Guid.NewGuid(),
            Key = "shop",
            Name = "Shop",
            Tools = [new McpToolInfo { Name = "lookup_order", ReadOnlyHint = true }, new McpToolInfo { Name = "cancel_order", DestructiveHint = true }],
        };
        await servers.AddAsync(shop, Ct);
        var service = new AgentService(_agents, _folders, new StaticOptionsMonitor<AiOptions>(Ai()), [], _time, NullLogger<AgentService>.Instance, servers);

        var agent = await service.CreateAsync(
            new SaveAgentRequest("Shop", Key: "shop", McpApps: [new AgentMcpAppDto(shop.Id, Mode: McpAppMode.Chosen, Tools: [new AgentMcpToolDto("lookup_order"), new AgentMcpToolDto("cancel_order")])]),
            Ct);
        agent.McpApps.Single().Tools!.Select(tool => tool.Policy).ShouldBe([McpToolPolicy.Auto, McpToolPolicy.Confirm]);

        // The app renamed cancel_order: the saved pick stays, so saving the agent still works.
        shop.Tools.RemoveAll(tool => tool.Name == "cancel_order");
        var request = new SaveAgentRequest("Shop", McpApps: agent.McpApps);
        var kept = await service.UpdateAsync(agent.Id, request, Ct);
        kept!.McpApps.Single().Tools!.Select(tool => tool.Name).ShouldBe(["lookup_order", "cancel_order"]);

        var typo = await Should.ThrowAsync<ValidationFailedException>(() => service.UpdateAsync(
            agent.Id,
            new SaveAgentRequest("Shop", McpApps: [new AgentMcpAppDto(shop.Id, Mode: McpAppMode.Chosen, Tools: [new AgentMcpToolDto("lookup_ordr")])]),
            Ct));
        typo.Errors.ShouldContainKey("mcpApps");
        await Should.ThrowAsync<ValidationFailedException>(() => service.UpdateAsync(agent.Id, new SaveAgentRequest("Shop", McpApps: [new AgentMcpAppDto(Guid.NewGuid())]), Ct));

        var off = await service.UpdateAsync(agent.Id, new SaveAgentRequest("Shop", McpApps: [kept.McpApps.Single() with { Enabled = false }]), Ct);
        off!.McpApps.Single().Enabled.ShouldBeFalse("off keeps the choices");
    }

    private AgentService Service(params IAgentUsage[] usages) =>
        new(_agents, _folders, new StaticOptionsMonitor<AiOptions>(Ai()), usages, _time, NullLogger<AgentService>.Instance);

    private static AiOptions Ai() => new()
    {
        Profiles =
        {
            ["orchestrator"] = new ModelProfileOptions { Kind = ModelKind.Chat },
            ["fast"] = new ModelProfileOptions { Kind = ModelKind.Chat },
            ["embedding"] = new ModelProfileOptions { Kind = ModelKind.Embedding },
        },
    };

    private sealed class FixedUsage(IReadOnlyList<string> uses) : IAgentUsage
    {
        public Task<IReadOnlyList<string>> ListUsesAsync(Guid agentId, CancellationToken cancellationToken = default) => Task.FromResult(uses);
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
