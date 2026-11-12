using Mazesta.Core.Windows;
namespace Mazesta.Diagnostics.Windows;

/// <summary>
/// Where a drive's space went: every folder under a root with the size of what is in it. Junctions and other reparse folders are not entered
/// (they point at space counted elsewhere), and cloud files that are not on this disk (OneDrive "online only") count nothing.
/// A folder Windows refuses to list is marked unread, never counted as empty.
/// </summary>
public static class SpaceScan
{
    // Recall-on-open / recall-on-data-access / offline: the file's bytes are not on this disk.
    private const FileAttributes NotHere = (FileAttributes)0x400000 | (FileAttributes)0x40000 | FileAttributes.Offline;

    public sealed record Progress(long Files, long Bytes, string Folder);

    // ponytail: hard links (WinSxS) are counted once per name, so Windows' own folder reads bigger than the space it takes, as in Explorer
    public static (SpaceNode Root, TopFiles Top) Scan(string root, Action<Progress> progress, CancellationToken ct)
    {
        var top = new TopFiles(30); var tree = new SpaceNode(root);
        var stack = new Stack<(DirectoryInfo Dir, SpaceNode Node)>(); stack.Push((new DirectoryInfo(root), tree));
        var opts = new EnumerationOptions { IgnoreInaccessible = false, AttributesToSkip = 0, RecurseSubdirectories = false, ReturnSpecialDirectories = false };
        long next = Environment.TickCount64;
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (dir, node) = stack.Pop();
            try
            {
                foreach (var e in dir.EnumerateFileSystemInfos("*", opts))
                {
                    if (e is DirectoryInfo d) { if ((d.Attributes & FileAttributes.ReparsePoint) == 0) stack.Push((d, node.Add(d.Name))); }
                    else if (e is FileInfo f && (f.Attributes & NotHere) == 0) { node.AddFile(f.Length); top.Add(f.FullName, f.Length); }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { node.Unread = true; }
            if (Environment.TickCount64 >= next) { progress(new(tree.Files, tree.Size, dir.FullName)); next = Environment.TickCount64 + 250; }
        }
        tree.Seal();
        return (tree, top);
    }
}
