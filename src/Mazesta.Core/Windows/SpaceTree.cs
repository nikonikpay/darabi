namespace Mazesta.Core.Windows;

/// <summary>
/// A folder of a disk-space scan: its size is everything under it that was read. <see cref="Unread"/> marks a folder Windows refused to list
/// (so its size is "at least"), and <see cref="PartUnread"/> one with such a folder somewhere inside; neither is shown as a measured total.
/// </summary>
public sealed class SpaceNode(string name, SpaceNode? parent = null)
{
    public string Name { get; } = name;
    public SpaceNode? Parent { get; } = parent;
    public long Size { get; private set; }
    public long Files { get; private set; }
    public bool Unread { get; set; }
    public bool PartUnread { get; private set; }
    public List<SpaceNode> Children { get; } = [];

    public SpaceNode Add(string name) { var c = new SpaceNode(name, this); Children.Add(c); return c; }
    public void AddFile(long bytes) { for (var n = this; n is not null; n = n.Parent) { n.Size += bytes; n.Files++; } }
    public string FullPath => Parent is null ? Name : Path.Combine(Parent.FullPath, Name);

    /// <summary>After the scan: the biggest first, and each folder knows whether something under it could not be read.</summary>
    public bool Seal()
    {
        bool part = Unread;
        foreach (var c in Children) part |= c.Seal();
        Children.Sort((a, b) => b.Size.CompareTo(a.Size));
        PartUnread = part && !Unread;
        return part;
    }

    /// <summary>The folder at these names under this one, or null when the scan has no such folder.</summary>
    public SpaceNode? Find(IEnumerable<string> names)
    {
        var n = this;
        foreach (var name in names) { n = n.Children.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)); if (n is null) return null; }
        return n;
    }
}

/// <summary>The biggest files of a scan, keeping only <paramref name="max"/> of them however many are offered.</summary>
public sealed class TopFiles(int max)
{
    private readonly PriorityQueue<(string Path, long Size), long> _q = new();
    public void Add(string path, long size)
    {
        if (_q.Count < max) _q.Enqueue((path, size), size);
        else if (_q.TryPeek(out _, out long least) && size > least) _q.EnqueueDequeue((path, size), size);
    }
    public IReadOnlyList<(string Path, long Size)> Biggest() => [.. _q.UnorderedItems.Select(x => x.Element).OrderByDescending(x => x.Size)];
}
