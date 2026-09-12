namespace Mazesta.Desktop.ViewModels;

public sealed class PlaceholderViewModel(string titleKey)
{
    public string Title => Localization.Loc.Get(titleKey);
    public string Body => Localization.Loc.Get("Placeholder_Body");
}
