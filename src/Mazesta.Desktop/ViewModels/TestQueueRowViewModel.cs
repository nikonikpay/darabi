using CommunityToolkit.Mvvm.ComponentModel; using Mazesta.Core.Text; using Mazesta.Diagnostics;
namespace Mazesta.Desktop.ViewModels;

/// <summary>One row in the Test Center queue: a registered <see cref="TestDefinition"/> plus the
/// technician's chosen duration/repeat, and (while running) its own live status. Duration and repeat
/// count are kept as text so Persian-digit input (spec §8, via PersianDigits) is possible; they are
/// parsed only when the row is actually queued.</summary>
public sealed partial class TestQueueRowViewModel(TestDefinition definition) : ObservableObject
{
    public static IReadOnlyList<RepeatMode> RepeatModes { get; } = [RepeatMode.Once, RepeatMode.Count, RepeatMode.Unlimited];

    public TestDefinition Definition { get; } = definition;
    public string Name => Localization.Loc.Get(Definition.NameKey);

    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private string _durationText = definition.DefaultDurationSeconds.ToString();
    [ObservableProperty] private RepeatMode _repeat = RepeatMode.Once;
    [ObservableProperty] private string _repeatCountText = "1";
    [ObservableProperty] private string? _validationError;

    [ObservableProperty] private TestOutcome _outcome = TestOutcome.NotRun;
    [ObservableProperty] private double _percentComplete;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private long _errorCount;
    [ObservableProperty] private string? _detail;

    public string OutcomeText => Localization.Loc.Get($"Test_Outcome_{Outcome}");
    partial void OnOutcomeChanged(TestOutcome value) => OnPropertyChanged(nameof(OutcomeText));

    /// <summary>Validates and builds this row's queue entry. Returns null (and sets
    /// ValidationError) instead of throwing: the caller keeps the invalid text on screen for the
    /// technician to correct, per spec §8 "مقدار نامعتبر برای اصلاح باقی بماند".</summary>
    public QueuedTest? TryBuildQueuedTest()
    {
        ValidationError = null;
        if (!PersianDigits.TryParseInt(DurationText, out int duration) || duration <= 0)
        { ValidationError = Localization.Loc.Get("Test_Validation_InvalidDuration"); return null; }
        int repeatCount = 1;
        if (Repeat == RepeatMode.Count && (!PersianDigits.TryParseInt(RepeatCountText, out repeatCount) || repeatCount <= 0))
        { ValidationError = Localization.Loc.Get("Test_Validation_InvalidRepeatCount"); return null; }
        return new QueuedTest(Definition, duration, Repeat, repeatCount);
    }

    public void ResetRunState() { Outcome = TestOutcome.NotRun; PercentComplete = 0; StatusText = ""; ErrorCount = 0; Detail = null; }
}
