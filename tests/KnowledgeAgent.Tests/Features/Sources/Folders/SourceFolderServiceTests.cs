using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Features.Sources.Folders;
using KnowledgeAgent.Infrastructure.Web;
using KnowledgeAgent.Tests.Infrastructure.Persistence.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace KnowledgeAgent.Tests.Features.Sources.Folders;

// The service runs against the EF Core in-memory store, so the rules are checked with the real repositories.
public sealed class SourceFolderServiceTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero));
    private IHost _host = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = PersistenceHost.Build(
            new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "InMemory",
                ["Persistence:InMemoryDatabaseName"] = $"folder-service-{Guid.NewGuid():N}",
            },
            _time,
            builder => builder.Services.AddSourceFolders());
        await PersistenceHost.RunStartupInitializersAsync(_host.Services, Ct);
    }

    public ValueTask DisposeAsync()
    {
        _host.Dispose();
        return ValueTask.CompletedTask;
    }

    [Theory]
    [InlineData("  Q3 \t reports\n ", "Q3 reports")]
    [InlineData("Line\none", "Line one")]
    [InlineData("Zero​width", "Zerowidth")]
    [InlineData("Šumava ✓", "Šumava ✓")]
    public async Task Names_are_trimmed_and_their_whitespace_collapsed(string name, string expected)
    {
        var folder = await FoldersAsync(service => service.CreateAsync(name, cancellationToken: Ct));

        folder.Name.ShouldBe(expected);
        folder.ParentId.ShouldBeNull();
        folder.CreatedAt.ShouldBe(_time.GetUtcNow());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\n ")]
    public async Task Blank_names_are_rejected(string? name)
    {
        var error = await ErrorAsync(service => service.CreateAsync(name, cancellationToken: Ct));

        error.Errors["name"].ShouldBe(["A folder name is required."]);
    }

    [Fact]
    public async Task Names_longer_than_100_characters_are_rejected()
    {
        (await FoldersAsync(service => service.CreateAsync(new string('a', 100), cancellationToken: Ct))).Name.Length.ShouldBe(100);

        var error = await ErrorAsync(service => service.CreateAsync(new string('b', 101), cancellationToken: Ct));

        error.Errors["name"].ShouldBe(["The name must be at most 100 characters long."]);
    }

    [Fact]
    public async Task Names_are_unique_among_siblings_ignoring_case()
    {
        var legal = await CreateAsync("Legal");
        var archive = await CreateAsync("Archive");
        var nested = await CreateAsync("legal", archive.Id);

        var duplicate = await ErrorAsync(service => service.CreateAsync("LEGAL", cancellationToken: Ct));
        var nestedDuplicate = await ErrorAsync(service => service.CreateAsync("Legal", archive.Id, Ct));
        var renamedOnto = await ErrorAsync(service => service.RenameAsync(archive.Id, "legal", Ct));
        var recased = await FoldersAsync(service => service.RenameAsync(legal.Id, "LEGAL", Ct));

        duplicate.Errors["name"].ShouldBe(["A folder named 'LEGAL' already exists at the top level."]);
        nestedDuplicate.Errors["name"].ShouldBe(["A folder named 'Legal' already exists in 'Archive'."]);
        renamedOnto.Errors["name"].ShouldBe(["A folder named 'legal' already exists at the top level."]);
        recased.ShouldNotBeNull().Name.ShouldBe("LEGAL");
        nested.ParentId.ShouldBe(archive.Id);
    }

    [Fact]
    public async Task Rename_saves_the_new_name_and_its_time()
    {
        var folder = await CreateAsync("Drafts");
        _time.Advance(TimeSpan.FromMinutes(5));

        var renamed = await FoldersAsync(service => service.RenameAsync(folder.Id, "  Final   drafts ", Ct));

        renamed.ShouldNotBeNull().Name.ShouldBe("Final drafts");
        var stored = (await FoldersAsync(service => service.GetAsync(folder.Id, Ct))).ShouldNotBeNull();
        stored.Name.ShouldBe("Final drafts");
        stored.UpdatedAt.ShouldBe(_time.GetUtcNow());
        stored.CreatedAt.ShouldBe(folder.CreatedAt);
    }

    [Fact]
    public async Task Folders_nest_at_most_three_levels_deep()
    {
        var first = await CreateAsync("One");
        var second = await CreateAsync("Two", first.Id);
        var third = await CreateAsync("Three", second.Id);

        var error = await ErrorAsync(service => service.CreateAsync("Four", third.Id, Ct));
        var unknownParent = await ErrorAsync(service => service.CreateAsync("Lost", Guid.NewGuid(), Ct));

        error.Errors["parentId"].ShouldBe(["Folders can be nested at most 3 levels deep."]);
        unknownParent.Errors["parentId"].ShouldBe(["The parent folder does not exist."]);
    }

    [Fact]
    public async Task A_folder_cannot_move_into_itself_or_below_itself()
    {
        var projects = await CreateAsync("Projects");
        var apollo = await CreateAsync("Apollo", projects.Id);

        var intoChild = await ErrorAsync(service => service.MoveAsync(projects.Id, apollo.Id, Ct));
        var intoSelf = await ErrorAsync(service => service.MoveAsync(projects.Id, projects.Id, Ct));

        intoChild.Errors["parentId"].ShouldBe(["A folder cannot be moved into itself or one of its subfolders."]);
        intoSelf.Errors["parentId"].ShouldBe(["A folder cannot be moved into itself or one of its subfolders."]);
    }

    [Fact]
    public async Task A_move_keeps_the_whole_subtree_within_the_depth_limit()
    {
        var team = await CreateAsync("Team");
        var shared = await CreateAsync("Shared", team.Id);
        var reports = await CreateAsync("Reports");
        await CreateAsync("2026", reports.Id);

        var tooDeep = await ErrorAsync(service => service.MoveAsync(reports.Id, shared.Id, Ct));
        var moved = await FoldersAsync(service => service.MoveAsync(reports.Id, team.Id, Ct));
        var backToTop = await FoldersAsync(service => service.MoveAsync(reports.Id, null, Ct));

        tooDeep.Errors["parentId"].ShouldBe(["Folders can be nested at most 3 levels deep."]);
        moved.ShouldNotBeNull().ParentId.ShouldBe(team.Id);
        backToTop.ShouldNotBeNull().ParentId.ShouldBeNull();
    }

    [Fact]
    public async Task A_move_reports_a_name_taken_in_the_target()
    {
        var notes = await CreateAsync("Notes");
        var archive = await CreateAsync("Archive");
        var archivedNotes = await CreateAsync("notes", archive.Id);

        var error = await ErrorAsync(service => service.MoveAsync(archivedNotes.Id, null, Ct));
        var unknownTarget = await ErrorAsync(service => service.MoveAsync(notes.Id, Guid.NewGuid(), Ct));

        error.Errors["parentId"].ShouldBe(["A folder named 'notes' already exists at the top level."]);
        unknownTarget.Errors["parentId"].ShouldBe(["The parent folder does not exist."]);
    }

    [Fact]
    public async Task Unknown_folders_are_reported_as_missing()
    {
        var unknown = Guid.NewGuid();

        (await FoldersAsync(service => service.RenameAsync(unknown, "Name", Ct))).ShouldBeNull();
        (await FoldersAsync(service => service.MoveAsync(unknown, null, Ct))).ShouldBeNull();
        (await FoldersAsync(service => service.DeleteAsync(unknown, Ct))).ShouldBeFalse();
        (await FoldersAsync(service => service.GetAsync(unknown, Ct))).ShouldBeNull();
    }

    [Fact]
    public async Task Concurrent_moves_cannot_create_a_cycle()
    {
        var first = await CreateAsync("First");
        var second = await CreateAsync("Second");

        var outcomes = await Task.WhenAll(
            Task.Run(() => TryMoveAsync(first.Id, second.Id), Ct),
            Task.Run(() => TryMoveAsync(second.Id, first.Id), Ct));

        outcomes.Count(moved => moved).ShouldBe(1);
        var folders = (await FoldersAsync(service => service.ListAsync(Ct))).Folders;
        folders.Count(folder => folder.ParentId is null && !folder.IsInternal).ShouldBe(1);
    }

    [Fact]
    public async Task Requests_share_one_gate()
    {
        await using var first = _host.Services.CreateAsyncScope();
        await using var second = _host.Services.CreateAsyncScope();

        first.ServiceProvider.GetRequiredService<SourceFolderGate>().ShouldBeSameAs(second.ServiceProvider.GetRequiredService<SourceFolderGate>());
    }

    [Fact]
    public async Task Sources_move_into_a_folder_and_back_out()
    {
        var folder = await CreateAsync("Contracts");
        var lease = await AddSourceAsync("Lease");
        var nda = await AddSourceAsync("NDA");
        await AddSourceAsync("Loose");

        var moved = await FoldersAsync(service => service.MoveSourcesAsync([lease.Id, nda.Id, lease.Id, Guid.NewGuid()], folder.Id, Ct));
        var listing = await FoldersAsync(service => service.ListAsync(Ct));
        var unfiled = await FoldersAsync(service => service.MoveSourcesAsync([nda.Id], null, Ct));
        var count = await FoldersAsync(service => service.CountSourcesAsync(folder.Id, Ct));

        moved.ShouldBe(2);
        listing.Folders.Where(item => !item.IsInternal).ShouldHaveSingleItem().Id.ShouldBe(folder.Id);
        listing.Counts.ByFolder[folder.Id].ShouldBe(2);
        listing.Counts.Unfiled.ShouldBe(1);
        listing.Counts.Total.ShouldBe(3);
        unfiled.ShouldBe(1);
        count.ShouldBe(1);
    }

    [Fact]
    public async Task Moving_sources_needs_a_selection_of_at_most_100_and_an_existing_folder()
    {
        var folder = await CreateAsync("Inbox");
        var tooMany = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToList();

        var none = await ErrorAsync(service => service.MoveSourcesAsync([], folder.Id, Ct));
        var overLimit = await ErrorAsync(service => service.MoveSourcesAsync(tooMany, folder.Id, Ct));
        var missing = await ErrorAsync(service => service.MoveSourcesAsync([Guid.NewGuid()], Guid.NewGuid(), Ct));

        none.Errors["sourceIds"].ShouldBe(["Choose at least one source."]);
        overLimit.Errors["sourceIds"].ShouldBe(["At most 100 sources can be moved at once."]);
        missing.Errors["folderId"].ShouldBe(["The folder does not exist."]);
        (await FoldersAsync(service => service.MoveSourcesAsync([.. tooMany.Take(50), .. tooMany.Take(50)], folder.Id, Ct))).ShouldBe(0);
    }

    [Fact]
    public async Task Every_source_matching_a_filter_moves_in_one_step()
    {
        var folder = await CreateAsync("Python docs");
        var tutorial = await AddSourceAsync("Tutorial", site: "docs.python.org");
        var library = await AddSourceAsync("Library reference", site: "docs.python.org");
        var post = await AddSourceAsync("Release post", site: "blog.python.org");

        var moved = await FoldersAsync(service => service.MoveMatchingSourcesAsync(new SourceQuery(Site: "docs.python.org"), folder.Id, Ct));
        var missing = await ErrorAsync(service => service.MoveMatchingSourcesAsync(new SourceQuery(), Guid.NewGuid(), Ct));

        moved.ShouldBe(2);
        missing.Errors["folderId"].ShouldBe(["The folder does not exist."]);
        var counts = (await FoldersAsync(service => service.ListAsync(Ct))).Counts;
        counts.ByFolder[folder.Id].ShouldBe(2);
        (await InScopeAsync((ISourceRepository sources) => sources.GetAsync(post.Id, Ct))).ShouldNotBeNull().FolderId.ShouldBeNull();
        foreach (var id in new[] { tutorial.Id, library.Id })
        {
            (await InScopeAsync((ISourceRepository sources) => sources.GetAsync(id, Ct))).ShouldNotBeNull().FolderId.ShouldBe(folder.Id);
        }
    }

    [Fact]
    public async Task Deleting_a_folder_keeps_its_sources()
    {
        var folder = await CreateAsync("Old");
        var child = await CreateAsync("Older", folder.Id);
        var source = await AddSourceAsync("Kept", child.Id);

        (await FoldersAsync(service => service.DeleteAsync(folder.Id, Ct))).ShouldBeTrue();

        (await FoldersAsync(service => service.ListAsync(Ct))).Folders.ShouldHaveSingleItem().IsInternal.ShouldBeTrue();
        var stored = await InScopeAsync((ISourceRepository sources) => sources.GetAsync(source.Id, Ct));
        stored.ShouldNotBeNull().FolderId.ShouldBeNull();
    }

    [Fact]
    public async Task The_Internal_folder_exists_and_cannot_be_renamed_moved_or_deleted_but_holds_folders_and_sources()
    {
        var folders = (await FoldersAsync(service => service.ListAsync(Ct))).Folders;
        var internalFolder = folders.ShouldHaveSingleItem();
        (internalFolder.Id, internalFolder.Name, internalFolder.ParentId, internalFolder.IsInternal).ShouldBe((SourceFolder.InternalId, "Internal", null, true));
        var other = await CreateAsync("Docs");

        (await ErrorAsync(service => service.RenameAsync(internalFolder.Id, "Team only", Ct))).Errors["id"]
            .ShouldBe(["The Internal folder is built in and cannot be renamed."]);
        (await ErrorAsync(service => service.MoveAsync(internalFolder.Id, other.Id, Ct))).Errors["id"]
            .ShouldBe(["The Internal folder is built in and cannot be moved."]);
        (await ErrorAsync(service => service.DeleteAsync(internalFolder.Id, Ct))).Errors["id"]
            .ShouldBe(["The Internal folder is built in and cannot be deleted."]);
        (await ErrorAsync(service => service.CreateAsync("internal", cancellationToken: Ct))).Errors.ShouldContainKey("name");

        // Inside it, folders and sources behave like anywhere else.
        var hr = await CreateAsync("HR", internalFolder.Id);
        var moved = await FoldersAsync(service => service.MoveAsync(other.Id, internalFolder.Id, Ct));
        var source = await AddSourceAsync("Salary bands");
        (await FoldersAsync(service => service.MoveSourcesAsync([source.Id], hr.Id, Ct))).ShouldBe(1);
        (await FoldersAsync(service => service.RenameAsync(hr.Id, "People", Ct))).ShouldNotBeNull().Name.ShouldBe("People");
        moved.ShouldNotBeNull().ParentId.ShouldBe(internalFolder.Id);
        (await FoldersAsync(service => service.DeleteAsync(hr.Id, Ct))).ShouldBeTrue();

        var after = (await FoldersAsync(service => service.ListAsync(Ct))).Folders;
        after.Single(folder => folder.Id == internalFolder.Id).Name.ShouldBe("Internal");
        after.Where(folder => folder.IsInternal).ShouldHaveSingleItem("the flag stays on the Internal folder; its subfolders are internal by being inside it");
    }

    private Task<SourceFolder> CreateAsync(string name, Guid? parentId = null) =>
        FoldersAsync(service => service.CreateAsync(name, parentId, Ct));

    private async Task<bool> TryMoveAsync(Guid id, Guid parentId)
    {
        try
        {
            await FoldersAsync(service => service.MoveAsync(id, parentId, Ct));
            return true;
        }
        catch (ValidationFailedException)
        {
            return false;
        }
    }

    private Task<Source> AddSourceAsync(string title, Guid? folderId = null, string? site = null) =>
        InScopeAsync((ISourceRepository sources) => sources.AddAsync(
            new Source { Type = SourceType.Text, Title = title, Site = site, FolderId = folderId, CreatedAt = _time.GetUtcNow() },
            cancellationToken: Ct));

    private Task<T> FoldersAsync<T>(Func<ISourceFolderService, Task<T>> action) => InScopeAsync(action);

    private async Task<ValidationFailedException> ErrorAsync<T>(Func<ISourceFolderService, Task<T>> action) =>
        await Should.ThrowAsync<ValidationFailedException>(() => InScopeAsync(action));

    private async Task<T> InScopeAsync<TService, T>(Func<TService, Task<T>> action)
        where TService : notnull
    {
        await using var scope = _host.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
