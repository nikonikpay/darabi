namespace Mazesta.Desktop.ViewModels;

public sealed record NavItem(string Key, string Glyph, Func<object> PageFactory)
{
    public string Label => Localization.Loc.Get(Key);
}
