namespace Mazesta.Core.Overlay;

/// <summary>How the shown program relates to the window in front: it, a process owning one of its child windows, a descendant, or unrelated.</summary>
public enum FrameLink { Foreground, ChildWindow, Descendant, Busiest }

/// <summary>
/// Which program's frame rate the overlay shows. The window in front is not always the process that presents: a Store / Game Pass game's frame
/// window belongs to ApplicationFrameHost while the game's own process (the owner of a child window) draws, and Chromium, Electron and WebView2
/// programs draw in a child GPU process. The foreground process wins when it presents; then a process owning one of its child windows; then its
/// descendants; and when nothing linked to the front window draws, the busiest presenting program - always shown with its own name, never
/// passed off as the program in front.
/// </summary>
public static class FrameTarget
{
    /// <summary>A fallback program must draw at least this often, so a clock ticking in a tray window is not shown as a game.</summary>
    public const int MinFallbackFrames = 10;

    /// <param name="foreground">The process owning the window in front (0 when none).</param>
    /// <param name="framesLastSecond">Frames each presenting process drew in the last second.</param>
    /// <param name="parentOf">A process's parent id, or null when unknown.</param>
    /// <param name="childWindowOwners">Processes that own a child window of the window in front.</param>
    /// <param name="excluded">Processes never shown as a fallback (the desktop compositor).</param>
    /// <returns>The process to show and how it relates to the window in front; null when nothing suitable draws.</returns>
    public static (int Pid, FrameLink Link)? Choose(int foreground, IReadOnlyDictionary<int, int> framesLastSecond, Func<int, int?> parentOf,
        IReadOnlyCollection<int> childWindowOwners, IReadOnlySet<int> excluded)
    {
        int Frames(int pid) => framesLastSecond.GetValueOrDefault(pid);
        if (foreground != 0)
        {
            if (Frames(foreground) > 0) return (foreground, FrameLink.Foreground);
            var owner = childWindowOwners.Where(p => p != foreground && Frames(p) > 0).OrderByDescending(Frames).FirstOrDefault();
            if (owner != 0) return (owner, FrameLink.ChildWindow);
            var descendant = framesLastSecond.Keys.Where(p => Frames(p) > 0 && IsDescendant(p, foreground, parentOf)).OrderByDescending(Frames).FirstOrDefault();
            if (descendant != 0) return (descendant, FrameLink.Descendant);
        }
        var busiest = framesLastSecond.Where(kv => kv.Value >= MinFallbackFrames && !excluded.Contains(kv.Key)).OrderByDescending(kv => kv.Value).FirstOrDefault();
        return busiest.Key != 0 ? (busiest.Key, FrameLink.Busiest) : null;
    }

    private static bool IsDescendant(int pid, int ancestor, Func<int, int?> parentOf)
    {
        // Parent ids are reused after a parent exits; the depth limit also ends a cycle such reuse can form.
        for (int depth = 0, p = pid; depth < 8; depth++)
        {
            if (parentOf(p) is not { } parent || parent == 0 || parent == p) return false;
            if (parent == ancestor) return true;
            p = parent;
        }
        return false;
    }
}
