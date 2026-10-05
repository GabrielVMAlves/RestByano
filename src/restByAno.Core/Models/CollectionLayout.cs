namespace restByAno.Core.Models;

/// <summary>
/// Canonical ordering of a collection. The tree shows folders (by Order), each with its requests (by Order),
/// followed by the requests that live at the collection root (by Order). The collection runner executes ordinary
/// requests in exactly this order, so what is saved and displayed is what runs.
/// </summary>
public static class CollectionLayout
{
    public static IReadOnlyList<RequestDefinition> ExecutionOrder(CollectionDefinition collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        var folderIds = collection.Folders.Select(f => f.Id).ToHashSet();
        var ordered = new List<RequestDefinition>();

        foreach (var folder in collection.Folders.OrderBy(f => f.Order))
            ordered.AddRange(collection.Requests.Where(r => r.FolderId == folder.Id).OrderBy(r => r.Order));

        // Requests that point at a missing folder are treated as root requests rather than dropped.
        ordered.AddRange(collection.Requests.Where(r => r.FolderId is null || !folderIds.Contains(r.FolderId.Value)).OrderBy(r => r.Order));
        return ordered;
    }
}

/// <summary>Deep copies that allocate fresh ids so a copy can never overwrite or alias the original.</summary>
public static class DefinitionCloner
{
    public static RequestDefinition CloneRequest(RequestDefinition source, string? name = null, Guid? folderId = null, int? order = null) =>
        source with
        {
            Id = Guid.NewGuid(),
            Name = name ?? source.Name,
            FolderId = folderId ?? source.FolderId,
            Order = order ?? source.Order,
            Assertions = source.Assertions.Select(a => a with { Id = Guid.NewGuid() }).ToList(),
            Extractors = source.Extractors.Select(x => x with { Id = Guid.NewGuid() }).ToList(),
        };

    public static (FolderDefinition Folder, IReadOnlyList<RequestDefinition> Requests) CloneFolder(
        FolderDefinition folder, IEnumerable<RequestDefinition> requests, string name, int order)
    {
        var copy = folder with { Id = Guid.NewGuid(), Name = name, Order = order };
        var copies = requests.Select(r => CloneRequest(r, folderId: copy.Id)).ToList();
        return (copy, copies);
    }

    public static CollectionDefinition CloneCollection(CollectionDefinition source, string name)
    {
        var folderMap = source.Folders.ToDictionary(f => f.Id, _ => Guid.NewGuid());
        return source with
        {
            Id = Guid.NewGuid(),
            Name = name,
            Defaults = source.Defaults.ToList(),
            Folders = source.Folders.Select(f => f with { Id = folderMap[f.Id] }).ToList(),
            Requests = source.Requests.Select(r => CloneRequest(r, folderId: null) with
            {
                FolderId = r.FolderId is { } id && folderMap.TryGetValue(id, out var mapped) ? mapped : null,
            }).ToList(),
        };
    }
}
