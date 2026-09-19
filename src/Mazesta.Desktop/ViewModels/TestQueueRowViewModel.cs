using CommunityToolkit.Mvvm.ComponentModel; using Mazesta.Core.Text; using Mazesta.Desktop.Localization; using Mazesta.Diagnostics;
namespace Mazesta.Desktop.ViewModels;

/// <summary>One row in the Test Center queue: a registered <see cref="TestDefinition"/> plus the chosen
/// duration/repeat/options and (while running) its live status. Duration and repeat count stay as text so
/// Persian-digit input works (PersianDigits); they are parsed only when the row is queued.</summary>
public sealed partial class TestQueueRowViewModel : ObservableObject
{
    public static IReadOnlyList<RepeatMode> RepeatModes { get; } = Enum.GetValues<RepeatMode>();

    public TestQueueRowViewModel(TestDefinition definition)
    {
        Definition = definition;
        _durationText = definition.DefaultDurationSeconds.ToString();
        Options = [.. definition.Options.Select(o => new TestOptionViewModel(o))];
    }

    public TestDefinition Definition { get; }
    public string Name => Loc.Get(Definition.NameKey);
    public IReadOnlyList<TestOptionViewModel> Options { get; }
    public bool HasOptions => Options.Count > 0;

    /// <summary>Nothing is selected until the technician chooses (spec: the user's unchecked defaults are kept) - a
    /// RAM or VRAM test that starts by itself is not a harmless default.</summary>
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _durationText;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsRepeatCount))] private RepeatMode _repeat = RepeatMode.Once;
    [ObservableProperty] private string _repeatCountText = "1";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasValidationError))] private string? _validationError;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(OutcomeText))] private TestOutcome _outcome = TestOutcome.NotRun;
    [ObservableProperty] private double _percentComplete;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasErrors), nameof(ErrorsText))] private long _errorCount;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasDetail))] private string? _detail;

    public string OutcomeText => Loc.Get($"Test_Outcome_{Outcome}");
    public string ErrorsText => Loc.Format("Test_Errors_Format", ErrorCount);
    public bool IsRepeatCount => Repeat == RepeatMode.Count;
    public bool HasValidationError => ValidationError is not null;
    public bool HasErrors => ErrorCount != 0;
    public bool HasDetail => Detail is not null;

    /// <summary>Validates and builds this row's queue entry. Returns null and sets ValidationError instead
    /// of throwing, so the invalid text stays on screen for correction (spec §8).</summary>
    public QueuedTest? TryBuildQueuedTest()
    {
        ValidationError = null;
        if (!PersianDigits.TryParseInt(DurationText, out int duration) || duration <= 0)
        { ValidationError = Loc.Get("Test_Validation_InvalidDuration"); return null; }
        int repeatCount = 1;
        if (Repeat == RepeatMode.Count && (!PersianDigits.TryParseInt(RepeatCountText, out repeatCount) || repeatCount <= 0))
        { ValidationError = Loc.Get("Test_Validation_InvalidRepeatCount"); return null; }
        if (Options.FirstOrDefault(o => !o.IsValid) is { } bad)
        { ValidationError = Loc.Format("Test_Validation_InvalidOption", bad.Label); return null; }
        return new QueuedTest(Definition, duration, Repeat, repeatCount, Options.ToDictionary(o => o.Option.Key, o => o.Value));
    }

    public void ResetRunState() { Outcome = TestOutcome.NotRun; PercentComplete = 0; StatusText = ""; ErrorCount = 0; Detail = null; }
}
