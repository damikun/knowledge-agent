using System.Threading.Channels;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Help;
using KnowledgeAgent.Features.Search;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Features.Sources.Folders;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Tests.Features.Chat.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Tests.Features.Help;

public sealed class SearchInternalToolTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly InMemorySourceFolderRepository _folders = new();
    private readonly InMemorySourceRepository _sources = new();
    private readonly FakeKnowledgeSearch _search = new();

    [Fact]
    public async Task It_searches_only_the_sources_in_the_Internal_folder_and_its_subfolders()
    {
        var internalFolder = _folders.AddInternal();
        var hr = _folders.Add("HR", internalFolder.Id);
        var docs = _folders.Add("Docs");
        var runbook = _sources.Add("On-call runbook");
        runbook.FolderId = internalFolder.Id;
        var salaries = _sources.Add("Salary bands");
        salaries.FolderId = hr.Id;
        _sources.Add("FAQ");
        _sources.Add("User guide").FolderId = docs.Id;
        _search.AddHit("On-call runbook", "Page the second line after ten minutes.", sourceId: runbook.Id);
        var citations = new CitationRegistry();

        var result = await new SearchInternalTool().InvokeAsync(Invocation(new() { ["query"] = "escalation" }, citations), Ct);

        _search.Requests.ShouldHaveSingleItem().SourceIds.ShouldNotBeNull().ShouldBe([runbook.Id, salaries.Id], ignoreOrder: true);
        result.IsError.ShouldBeFalse();
        result.Text.ShouldContain("[S1] Internal source: On-call runbook");
        var citation = result.Citations.ShouldNotBeNull().ShouldHaveSingleItem();
        (citation.SourceTitle, citation.SourceUrl).ShouldBe(("Internal: On-call runbook", $"/sources?source={runbook.Id:D}"));
    }

    [Fact]
    public async Task Without_Internal_sources_or_an_embedding_model_there_is_nothing_to_search()
    {
        _sources.Add("FAQ");

        var noFolder = await new SearchInternalTool().InvokeAsync(Invocation(new() { ["query"] = "refunds" }), Ct);
        _folders.AddInternal();
        var emptyFolder = await new SearchInternalTool().InvokeAsync(Invocation(new() { ["query"] = "refunds" }), Ct);

        noFolder.Text.ShouldBe(SearchInternalTool.NothingToSearch);
        emptyFolder.Text.ShouldBe(SearchInternalTool.NothingToSearch);
        _search.Requests.ShouldBeEmpty();

        _sources.Add("Refund policy").FolderId = SourceFolder.InternalId;
        _search.Handler = (_, _) => throw new ModelNotConfiguredException("No embedding model.");

        var noModel = await new SearchInternalTool().InvokeAsync(Invocation(new() { ["query"] = "refunds" }), Ct);

        noModel.IsError.ShouldBeFalse("the assistant falls back to the help");
        noModel.Text.ShouldStartWith("Nothing to search: no embedding model is connected");
    }

    private ToolInvocation Invocation(Dictionary<string, object?> arguments, CitationRegistry? citations = null)
    {
        var services = new ServiceCollection()
            .AddSingleton<ISourceFolderRepository>(_folders)
            .AddSingleton<ISourceRepository>(_sources)
            .AddSingleton<IKnowledgeSearch>(_search)
            .BuildServiceProvider();
        var channel = Channel.CreateUnbounded<ChatStreamEvent>();

        // The help agent's turn has no sources at all; the tool finds the Internal ones itself.
        var turn = new AgentTurnContext(Guid.NewGuid(), Guid.NewGuid(), [], citations ?? new CitationRegistry(), channel.Writer);
        return new ToolInvocation("call_1", new ToolArguments(arguments), turn, "help", null!, services);
    }
}
