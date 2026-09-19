using CommunityToolkit.Mvvm.ComponentModel; using Mazesta.Core.Text; using Mazesta.Desktop.Localization; using Mazesta.Diagnostics;
namespace Mazesta.Desktop.ViewModels;

/// <summary>The editor for one <see cref="TestOption"/> a test declares. A choice option is a list (drives,
/// GPUs), read when the page is built because it describes this machine right now; a number or text option
/// is a text box, and numbers accept Persian digits.</summary>
public sealed partial class TestOptionViewModel : ObservableObject
{
    public TestOption Option { get; }
    public string Label => Loc.Get(Option.LabelKey);
    public bool IsChoice => Option.Kind == TestOptionKind.Choice;
    public bool IsFreeText => !IsChoice;
    public IReadOnlyList<OptionChoice> Choices { get; }
    [ObservableProperty] private OptionChoice? _selectedChoice;
    [ObservableProperty] private string _text;

    public TestOptionViewModel(TestOption option)
    {
        Option = option;
        Choices = option.Choices?.Invoke() ?? [];
        _text = option.Default;
        _selectedChoice = Choices.FirstOrDefault(c => c.Value == option.Default) ?? Choices.FirstOrDefault();
    }

    /// <summary>What is handed to the test: the chosen item's value, or the typed text (digits normalised).</summary>
    public string Value => IsChoice ? SelectedChoice?.Value ?? "" : PersianDigits.Normalize(Text).Trim();

    public bool IsValid => Option.Kind != TestOptionKind.Integer || (int.TryParse(Value, out int n) && n >= 0);
}
