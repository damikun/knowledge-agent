namespace KnowledgeAgent.Features.Sources.Folders;

internal sealed class SourceFolderTree
{
    private readonly Dictionary<Guid, SourceFolder> _folders;
    private readonly ILookup<Guid?, SourceFolder> _children;

    public SourceFolderTree(IEnumerable<SourceFolder> folders)
    {
        _folders = folders.ToDictionary(folder => folder.Id);
        _children = _folders.Values.ToLookup(folder => folder.ParentId);
    }

    public SourceFolder? Find(Guid id) => _folders.GetValueOrDefault(id);

    public IEnumerable<SourceFolder> ChildrenOf(Guid? parentId) => _children[parentId];

    public int DepthOf(Guid id) => PathTo(id).Count;

    public int HeightOf(Guid id)
    {
        var height = 0;
        var visited = new HashSet<Guid>();
        var level = _folders.ContainsKey(id) ? new List<Guid> { id } : [];
        while (level.Count > 0)
        {
            height++;
            visited.UnionWith(level);
            level = level.SelectMany(parent => _children[parent])
                .Select(child => child.Id)
                .Where(child => !visited.Contains(child))
                .ToList();
        }

        return height;
    }

    public bool IsInside(Guid id, Guid ancestorId) => PathTo(id).Any(folder => folder.Id == ancestorId);

    public bool IsInternal(Guid id) => PathTo(id).Any(folder => folder.IsInternal);

    public IReadOnlySet<Guid> InternalFolderIds() =>
        _folders.Values.Where(folder => folder.IsInternal).SelectMany(folder => SubtreeOf(folder.Id)).Select(folder => folder.Id).ToHashSet();

    public IReadOnlyList<SourceFolder> SubtreeOf(Guid id)
    {
        if (Find(id) is not { } root)
        {
            return [];
        }

        var subtree = new List<SourceFolder> { root };
        var visited = new HashSet<Guid> { id };
        for (var i = 0; i < subtree.Count; i++)
        {
            subtree.AddRange(_children[subtree[i].Id].Where(child => visited.Add(child.Id)));
        }

        return subtree;
    }

    public IReadOnlyList<SourceFolder> PathTo(Guid id)
    {
        var path = new List<SourceFolder>();
        var visited = new HashSet<Guid>();
        for (var folder = Find(id); folder is not null && visited.Add(folder.Id); folder = folder.ParentId is { } parentId ? Find(parentId) : null)
        {
            path.Add(folder);
        }

        path.Reverse();
        return path;
    }
}
