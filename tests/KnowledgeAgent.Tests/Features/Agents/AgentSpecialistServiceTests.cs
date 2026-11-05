using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.Web;
using KnowledgeAgent.Tests.Features.Chat.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace KnowledgeAgent.Tests.Features.Agents;

public sealed class AgentSpecialistServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly InMemoryAgentRepository _agents = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Specialists_are_saved_trimmed_once_each_and_kept_when_left_out()
    {
        var service = Service();
        var billing = await service.CreateAsync(new SaveAgentRequest("Billing", Key: "billing"), Ct);
        var front = await service.CreateAsync(
            new SaveAgentRequest("Front", Key: "front")
            {
                Specialists = [new(billing.Id, "  Invoices and refunds "), new(billing.Id, "Twice"), new(Guid.Empty, "Nobody")],
                ShowSpecialistName = true,
            },
            Ct);

        front.Specialists.ShouldBe([new AgentSpecialistDto(billing.Id, "Invoices and refunds")]);
        front.ShowSpecialistName.ShouldBeTrue();

        var renamed = await service.UpdateAsync(front.Id, new SaveAgentRequest("Front desk"), Ct);
        renamed.ShouldNotBeNull().Specialists.ShouldBe(front.Specialists);
        renamed.ShowSpecialistName.ShouldBeTrue();

        var cleared = await service.UpdateAsync(front.Id, new SaveAgentRequest("Front desk") { Specialists = [], ShowSpecialistName = false }, Ct);
        cleared.ShouldNotBeNull().Specialists.ShouldBeEmpty();
        cleared.ShowSpecialistName.ShouldBeFalse();
    }

    [Fact]
    public async Task An_agent_cannot_hand_off_to_itself_a_missing_agent_the_built_in_one_or_without_saying_when()
    {
        var service = Service();
        var builtIn = _agents.Add(new AgentConfig { Key = AgentConfig.InternalKey, Name = "Knowledge Agent", IsBuiltIn = true });
        var billing = await service.CreateAsync(new SaveAgentRequest("Billing", Key: "billing"), Ct);
        var front = await service.CreateAsync(new SaveAgentRequest("Front", Key: "front"), Ct);

        var error = await Should.ThrowAsync<ValidationFailedException>(() => service.UpdateAsync(
            front.Id,
            new SaveAgentRequest("Front")
            {
                Specialists =
                [
                    new(front.Id, "Me"),
                    new(Guid.NewGuid(), "Gone"),
                    new(builtIn.Id, "Everything"),
                    new(billing.Id, " "),
                ],
            },
            Ct));

        error.Errors.Keys.ShouldBe(["specialists"]);
        var messages = error.Errors["specialists"];
        messages.Length.ShouldBe(4);
        messages.ShouldContain(message => message.Contains("itself"));
        messages.ShouldContain(message => message.Contains("does not exist"));
        messages.ShouldContain(message => message.Contains("built-in"));
        messages.ShouldContain(message => message.Contains("Billing"));
        (await service.GetAsync(front.Id, Ct)).ShouldNotBeNull().Specialists.ShouldBeEmpty();
    }

    [Fact]
    public async Task Hand_offs_cannot_form_a_loop()
    {
        var service = Service();
        var orders = await service.CreateAsync(new SaveAgentRequest("Orders", Key: "orders"), Ct);
        var billing = await service.CreateAsync(new SaveAgentRequest("Billing", Key: "billing") { Specialists = [new(orders.Id, "Orders")] }, Ct);
        var front = await service.CreateAsync(new SaveAgentRequest("Front", Key: "front") { Specialists = [new(billing.Id, "Invoices")] }, Ct);

        var direct = await Should.ThrowAsync<ValidationFailedException>(() =>
            service.UpdateAsync(billing.Id, new SaveAgentRequest("Billing") { Specialists = [new(front.Id, "Everything else")] }, Ct));
        direct.Errors["specialists"].ShouldHaveSingleItem().ShouldContain("Front already hands off to Billing");

        var indirect = await Should.ThrowAsync<ValidationFailedException>(() =>
            service.UpdateAsync(orders.Id, new SaveAgentRequest("Orders") { Specialists = [new(front.Id, "Everything else")] }, Ct));
        indirect.Errors["specialists"].ShouldHaveSingleItem().ShouldContain("directly or through another agent");
    }

    [Fact]
    public async Task An_agent_used_as_a_specialist_is_only_deleted_after_it_is_removed_from_its_teams()
    {
        var service = Service();
        var billing = await service.CreateAsync(new SaveAgentRequest("Billing", Key: "billing"), Ct);
        var front = await service.CreateAsync(new SaveAgentRequest("Front", Key: "front") { Specialists = [new(billing.Id, "Invoices")] }, Ct);
        var shop = await service.CreateAsync(new SaveAgentRequest("Shop", Key: "shop") { Specialists = [new(billing.Id, "Payments")] }, Ct);

        var error = await Should.ThrowAsync<AgentIsSpecialistException>(() => service.DeleteAsync(billing.Id, Ct));

        error.Agents.Select(agent => agent.Name).ShouldBe(["Front", "Shop"], ignoreOrder: true);
        error.Message.ShouldContain("Front");
        (await service.GetAsync(billing.Id, Ct)).ShouldNotBeNull();

        (await service.DeleteAsync(billing.Id, removeFromTeams: true, Ct)).ShouldBeTrue();
        (await service.GetAsync(billing.Id, Ct)).ShouldBeNull();
        (await service.GetAsync(front.Id, Ct)).ShouldNotBeNull().Specialists.ShouldBeEmpty();
        (await service.GetAsync(shop.Id, Ct)).ShouldNotBeNull().Specialists.ShouldBeEmpty();
    }

    private AgentService Service() =>
        new(_agents, new InMemorySourceFolderRepository(), new StaticOptionsMonitor<AiOptions>(Ai()), [], _time, NullLogger<AgentService>.Instance);

    private static AiOptions Ai() => new()
    {
        Profiles = { ["orchestrator"] = new ModelProfileOptions { Kind = ModelKind.Chat } },
    };

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
