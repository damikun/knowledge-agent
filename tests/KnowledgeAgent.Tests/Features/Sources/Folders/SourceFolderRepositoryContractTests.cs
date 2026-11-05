using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Features.Sources.Folders;
using KnowledgeAgent.Tests.Infrastructure.Persistence.Support;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Tests.Features.Sources.Folders;

// Covers the folder repository and the folder side of the source repository. Tests share one database per class,
// so each one works with its own folders and sources.
public abstract class SourceFolderRepositoryContractTests<TFixture>
    where TFixture : PersistenceFixture
{
    private static readonly DateTimeOffset BaseTime = new(2025, 5, 6, 7, 8, 9, TimeSpan.Zero);

    protected SourceFolderRepositoryContractTests(TFixture fixture)
    {
        // Skip here when the database is unavailable, before Should.ThrowAsync can mistake the skip for a failure.
        _ = fixture.Services;
        Fixture = fixture;
    }

    protected TFixture Fixture { get; }

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Folders_round_trip_with_their_parent()
    {
        var createdAt = new DateTimeOffset(2025, 3, 4, 10, 11, 12, TimeSpan.FromHours(2));
        var parent = await AddFolderAsync("Příručky ✓ \"HR\"", createdAt: createdAt);
        var child = await AddFolderAsync("Onboarding", parent.Id);

        var loaded = await InScopeAsync((ISourceFolderRepository folders) => folders.GetAsync(child.Id, Ct));
        var listed = await InScopeAsync((ISourceFolderRepository folders) => folders.ListAsync(Ct));
        var stored = (await InScopeAsync((ISourceFolderRepository folders) => folders.GetAsync(parent.Id, Ct))).ShouldNotBeNull();

        loaded.ShouldNotBeNull().ParentId.ShouldBe(parent.Id);
        loaded.Name.ShouldBe("Onboarding");
        stored.Name.ShouldBe("Příručky ✓ \"HR\"");
        stored.ParentId.ShouldBeNull();
        stored.CreatedAt.ShouldBe(createdAt);
        stored.CreatedAt.Offset.ShouldBe(TimeSpan.Zero);
        listed.Select(folder => folder.Id).ShouldContain(parent.Id);
        listed.Single(folder => folder.Id == child.Id).ParentId.ShouldBe(parent.Id);
        (await InScopeAsync((ISourceFolderRepository folders) => folders.GetAsync(Guid.NewGuid(), Ct))).ShouldBeNull();
    }

    [Fact]
    public async Task Update_saves_the_name_and_parent_and_sets_UpdatedAt()
    {
        var target = await AddFolderAsync("Target");
        var folder = await AddFolderAsync("Drafts");
        folder.Name = "Final drafts";
        folder.ParentId = target.Id;
        var updatedAt = Tick();

        await InScopeAsync((ISourceFolderRepository folders) => folders.UpdateAsync(folder, Ct));

        var stored = (await InScopeAsync((ISourceFolderRepository folders) => folders.GetAsync(folder.Id, Ct))).ShouldNotBeNull();
        stored.Name.ShouldBe("Final drafts");
        stored.ParentId.ShouldBe(target.Id);
        stored.UpdatedAt.ShouldBe(updatedAt);
        stored.CreatedAt.ShouldBe(BaseTime);
        await Should.ThrowAsync<KeyNotFoundException>(() =>
            InScopeAsync((ISourceFolderRepository folders) => folders.UpdateAsync(new SourceFolder { Name = "Missing" }, Ct)));
    }

    [Fact]
    public async Task Delete_removes_the_subfolders_and_leaves_their_sources_unfiled()
    {
        var root = await AddFolderAsync("Projects");
        var child = await AddFolderAsync("Apollo", root.Id);
        var grandchild = await AddFolderAsync("Specs", child.Id);
        var sibling = await AddFolderAsync("Archive");
        var inRoot = await AddSourceAsync("Roadmap", root.Id);
        var inGrandchild = await AddSourceAsync("Interface spec", grandchild.Id);
        var inSibling = await AddSourceAsync("Old notes", sibling.Id);

        var deleted = await InScopeAsync((ISourceFolderRepository folders) => folders.DeleteAsync(root.Id, Ct));
        var deletedAgain = await InScopeAsync((ISourceFolderRepository folders) => folders.DeleteAsync(root.Id, Ct));

        deleted.ShouldBeTrue();
        deletedAgain.ShouldBeFalse();
        foreach (var id in new[] { root.Id, child.Id, grandchild.Id })
        {
            (await InScopeAsync((ISourceFolderRepository folders) => folders.GetAsync(id, Ct))).ShouldBeNull();
        }

        (await InScopeAsync((ISourceFolderRepository folders) => folders.GetAsync(sibling.Id, Ct))).ShouldNotBeNull();
        (await GetSourceAsync(inRoot.Id)).FolderId.ShouldBeNull();
        (await GetSourceAsync(inGrandchild.Id)).FolderId.ShouldBeNull();
        (await GetSourceAsync(inSibling.Id)).FolderId.ShouldBe(sibling.Id);
    }

    [Fact]
    public async Task MoveToFolder_moves_the_existing_sources_and_counts_them()
    {
        var folder = await AddFolderAsync("Contracts");
        var first = await AddSourceAsync("Lease");
        var second = await AddSourceAsync("Service agreement");

        var moved = await InScopeAsync((ISourceRepository sources) =>
            sources.MoveToFolderAsync([first.Id, second.Id, Guid.NewGuid(), first.Id], folder.Id, Ct));

        moved.ShouldBe(2);
        (await GetSourceAsync(first.Id)).FolderId.ShouldBe(folder.Id);
        (await GetSourceAsync(second.Id)).FolderId.ShouldBe(folder.Id);

        (await InScopeAsync((ISourceRepository sources) => sources.MoveToFolderAsync([first.Id], null, Ct))).ShouldBe(1);
        (await GetSourceAsync(first.Id)).FolderId.ShouldBeNull();
        (await InScopeAsync((ISourceRepository sources) => sources.MoveToFolderAsync([], folder.Id, Ct))).ShouldBe(0);
    }

    [Fact]
    public async Task MoveMatching_moves_every_source_the_filters_match_regardless_of_paging()
    {
        var token = "tk" + Guid.NewGuid().ToString("N")[..10];
        var docs = await AddFolderAsync("Docs");
        var intro = await AddSourceAsync($"{token} intro", site: "docs.example.com");
        var broken = await AddSourceAsync($"{token} broken page", status: SourceStatus.Failed, site: "docs.example.com");
        var post = await AddSourceAsync($"{token} post", site: "blog.example.com");
        var other = await AddSourceAsync("Unrelated page", site: "docs.example.com");

        var moved = await InScopeAsync((ISourceRepository sources) =>
            sources.MoveMatchingToFolderAsync(new SourceQuery(Text: token, Site: "docs.example.com", Limit: 1), docs.Id, Ct));
        var back = await InScopeAsync((ISourceRepository sources) =>
            sources.MoveMatchingToFolderAsync(new SourceQuery(Statuses: [SourceStatus.Failed], Folder: new FolderFilter(docs.Id)), null, Ct));

        moved.ShouldBe(2);
        back.ShouldBe(1);
        (await GetSourceAsync(intro.Id)).FolderId.ShouldBe(docs.Id);
        (await GetSourceAsync(broken.Id)).FolderId.ShouldBeNull();
        (await GetSourceAsync(post.Id)).FolderId.ShouldBeNull();
        (await GetSourceAsync(other.Id)).FolderId.ShouldBeNull();
    }

    [Fact]
    public async Task A_source_added_into_a_folder_keeps_it()
    {
        var folder = await AddFolderAsync("Inbox");

        var source = await AddSourceAsync("Welcome letter", folder.Id);

        (await GetSourceAsync(source.Id)).FolderId.ShouldBe(folder.Id);
    }

    [Fact]
    public async Task Update_of_a_stale_source_keeps_the_folder_it_was_moved_to()
    {
        // Ingestion loads a source, works on it for a while and saves it, possibly after someone moved it.
        var folder = await AddFolderAsync("Reports");
        var source = await AddSourceAsync("Quarterly report");
        var stale = await GetSourceAsync(source.Id);
        await InScopeAsync((ISourceRepository sources) => sources.MoveToFolderAsync([source.Id], folder.Id, Ct));

        stale.Status = SourceStatus.Ready;
        stale.ChunkCount = 7;
        await InScopeAsync((ISourceRepository sources) => sources.UpdateAsync(stale, Ct));

        var stored = await GetSourceAsync(source.Id);
        stored.FolderId.ShouldBe(folder.Id);
        stored.Status.ShouldBe(SourceStatus.Ready);
        stored.ChunkCount.ShouldBe(7);
    }

    [Fact]
    public async Task Find_filters_by_folder_or_unfiled_together_with_the_other_filters()
    {
        var token = "tk" + Guid.NewGuid().ToString("N")[..10];
        var legal = await AddFolderAsync("Legal");
        var finance = await AddFolderAsync("Finance");
        var nda = await AddSourceAsync($"{token} NDA", legal.Id);
        var lease = await AddSourceAsync($"{token} lease", legal.Id, SourceStatus.Failed);
        var budget = await AddSourceAsync($"{token} budget", finance.Id);
        var loose = await AddSourceAsync($"{token} loose notes");

        var inLegal = await FindAsync(new SourceQuery(Text: token, Folder: new FolderFilter(legal.Id)));
        var readyInLegal = await FindAsync(new SourceQuery(Text: token, Statuses: [SourceStatus.Ready], Folder: new FolderFilter(legal.Id)));
        var unfiled = await FindAsync(new SourceQuery(Text: token, Folder: FolderFilter.Unfiled));
        var everywhere = await FindAsync(new SourceQuery(Text: token));
        var newestFirst = await FindAsync(new SourceQuery(Ids: [nda.Id, lease.Id, budget.Id], Folder: new FolderFilter(legal.Id)));

        inLegal.Items.Select(source => source.Id).ShouldBe([lease.Id, nda.Id], ignoreOrder: true);
        inLegal.Total.ShouldBe(2);
        readyInLegal.Items.ShouldHaveSingleItem().Id.ShouldBe(nda.Id);
        unfiled.Items.ShouldHaveSingleItem().Id.ShouldBe(loose.Id);
        everywhere.Total.ShouldBe(4);
        newestFirst.Items.Select(source => source.Id).ShouldBe([lease.Id, nda.Id]);
        (await FindAsync(new SourceQuery(Text: token, Folder: new FolderFilter(Guid.NewGuid())))).Total.ShouldBe(0);
    }

    [Fact]
    public async Task Counts_group_every_source_by_its_folder()
    {
        var before = await InScopeAsync((ISourceRepository sources) => sources.CountByFolderAsync(Ct));
        var folder = await AddFolderAsync("Research");
        var empty = await AddFolderAsync("Empty");
        await AddSourceAsync("Paper one", folder.Id);
        await AddSourceAsync("Paper two", folder.Id, SourceStatus.Processing);
        await AddSourceAsync("Unsorted");

        var after = await InScopeAsync((ISourceRepository sources) => sources.CountByFolderAsync(Ct));

        after.ByFolder[folder.Id].ShouldBe(2);
        after.ByFolder.ShouldNotContainKey(empty.Id);
        (after.Unfiled - before.Unfiled).ShouldBe(1);
        (after.Total - before.Total).ShouldBe(3);
        after.Total.ShouldBe(after.Unfiled + after.ByFolder.Values.Sum());
    }

    protected async Task<SourceFolder> AddFolderAsync(string name, Guid? parentId = null, DateTimeOffset? createdAt = null)
    {
        var folder = new SourceFolder { Name = name, ParentId = parentId, CreatedAt = createdAt ?? BaseTime, UpdatedAt = createdAt ?? BaseTime };
        return await InScopeAsync((ISourceFolderRepository folders) => folders.AddAsync(folder, Ct));
    }

    protected Task<Source> AddSourceAsync(string title, Guid? folderId = null, SourceStatus status = SourceStatus.Ready, string? site = null)
    {
        var source = new Source
        {
            Type = site is null ? SourceType.Text : SourceType.Url,
            Status = status,
            Title = title,
            Url = site is null ? null : $"https://{site}/{Guid.NewGuid():N}",
            Site = site,
            FolderId = folderId,
            CreatedAt = Tick(),
        };
        source.UpdatedAt = source.CreatedAt;
        return InScopeAsync((ISourceRepository sources) => sources.AddAsync(source, cancellationToken: Ct));
    }

    protected async Task<Source> GetSourceAsync(Guid id) =>
        (await InScopeAsync((ISourceRepository sources) => sources.GetAsync(id, Ct))).ShouldNotBeNull();

    protected Task<SourcePage> FindAsync(SourceQuery query) =>
        InScopeAsync((ISourceRepository sources) => sources.FindAsync(query, Ct));

    protected DateTimeOffset Tick()
    {
        Fixture.Time.Advance(TimeSpan.FromMinutes(1));
        return Fixture.Time.GetUtcNow();
    }

    protected async Task<TResult> InScopeAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    protected async Task InScopeAsync<TService>(Func<TService, Task> action)
        where TService : notnull
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
