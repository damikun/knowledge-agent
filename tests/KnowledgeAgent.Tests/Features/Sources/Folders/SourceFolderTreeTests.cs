using KnowledgeAgent.Features.Sources.Folders;

namespace KnowledgeAgent.Tests.Features.Sources.Folders;

public sealed class SourceFolderTreeTests
{
    private static readonly SourceFolder Projects = Folder("Projects");
    private static readonly SourceFolder Apollo = Folder("Apollo", Projects);
    private static readonly SourceFolder Specs = Folder("Specs", Apollo);
    private static readonly SourceFolder Gemini = Folder("Gemini", Projects);
    private static readonly SourceFolder Archive = Folder("Archive");

    private readonly SourceFolderTree _tree = new([Specs, Archive, Apollo, Projects, Gemini]);

    [Fact]
    public void Depth_counts_the_levels_from_the_top()
    {
        _tree.DepthOf(Projects.Id).ShouldBe(1);
        _tree.DepthOf(Apollo.Id).ShouldBe(2);
        _tree.DepthOf(Specs.Id).ShouldBe(3);
        _tree.DepthOf(Guid.NewGuid()).ShouldBe(0);
        _tree.PathTo(Specs.Id).Select(folder => folder.Name).ShouldBe(["Projects", "Apollo", "Specs"]);
    }

    [Fact]
    public void Height_counts_the_levels_from_the_folder_down()
    {
        _tree.HeightOf(Projects.Id).ShouldBe(3);
        _tree.HeightOf(Apollo.Id).ShouldBe(2);
        _tree.HeightOf(Specs.Id).ShouldBe(1);
        _tree.HeightOf(Guid.NewGuid()).ShouldBe(0);
    }

    [Fact]
    public void Subtrees_and_ancestry()
    {
        _tree.SubtreeOf(Projects.Id).Select(folder => folder.Name).ShouldBe(["Projects", "Apollo", "Gemini", "Specs"], ignoreOrder: true);
        _tree.SubtreeOf(Archive.Id).ShouldHaveSingleItem().ShouldBe(Archive);
        _tree.SubtreeOf(Guid.NewGuid()).ShouldBeEmpty();
        _tree.IsInside(Specs.Id, Projects.Id).ShouldBeTrue();
        _tree.IsInside(Specs.Id, Specs.Id).ShouldBeTrue();
        _tree.IsInside(Gemini.Id, Apollo.Id).ShouldBeFalse();
        _tree.ChildrenOf(Projects.Id).Select(folder => folder.Name).ShouldBe(["Apollo", "Gemini"], ignoreOrder: true);
        _tree.ChildrenOf(null).Select(folder => folder.Name).ShouldBe(["Projects", "Archive"], ignoreOrder: true);
    }

    [Fact]
    public void The_Internal_folder_makes_its_whole_subtree_internal()
    {
        var internalFolder = new SourceFolder { Name = "Internal", IsInternal = true };
        var hr = Folder("HR", internalFolder);
        var payroll = Folder("Payroll", hr);
        var tree = new SourceFolderTree([Projects, Apollo, internalFolder, hr, payroll]);

        tree.InternalFolderIds().ShouldBe([internalFolder.Id, hr.Id, payroll.Id], ignoreOrder: true);
        tree.IsInternal(payroll.Id).ShouldBeTrue();
        tree.IsInternal(Apollo.Id).ShouldBeFalse();
        tree.IsInternal(Guid.NewGuid()).ShouldBeFalse();
        _tree.InternalFolderIds().ShouldBeEmpty();
    }

    [Fact]
    public void A_parent_cycle_does_not_hang_the_walks()
    {
        var first = new SourceFolder { Name = "First" };
        var second = new SourceFolder { Name = "Second", ParentId = first.Id };
        first.ParentId = second.Id;
        var tree = new SourceFolderTree([first, second]);

        tree.DepthOf(first.Id).ShouldBe(2);
        tree.HeightOf(first.Id).ShouldBe(2);
        tree.SubtreeOf(first.Id).Count.ShouldBe(2);
        tree.IsInside(first.Id, second.Id).ShouldBeTrue();
    }

    private static SourceFolder Folder(string name, SourceFolder? parent = null) => new() { Name = name, ParentId = parent?.Id };
}
