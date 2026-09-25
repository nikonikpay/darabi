namespace Mazesta.Desktop.ViewModels;

public enum ToastKind { Success, Danger, Warning, Info }

/// <summary>A short notice in the corner of the window (spec 9.4: test passed/failed, benchmark done). The text says what happened; the colour
/// (Kind) only helps the eye.</summary>
public sealed record ToastViewModel(string Text, ToastKind Kind);
