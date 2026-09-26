using System.Windows; using System.Windows.Controls;
namespace Mazesta.Desktop.Controls;

/// <summary>Two children side by side - the first takes the rest of the width, the second <see cref="SideWidth"/> - or, when the page is
/// narrower than <see cref="Breakpoint"/>, one under the other. Laid out in the panel's flow direction, so under RTL the first child is on the right.</summary>
public sealed class TwoColumns : Panel
{
    public static readonly DependencyProperty SideWidthProperty = DependencyProperty.Register(nameof(SideWidth), typeof(double), typeof(TwoColumns),
        new FrameworkPropertyMetadata(360.0, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty BreakpointProperty = DependencyProperty.Register(nameof(Breakpoint), typeof(double), typeof(TwoColumns),
        new FrameworkPropertyMetadata(900.0, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(nameof(Gap), typeof(double), typeof(TwoColumns),
        new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double SideWidth { get => (double)GetValue(SideWidthProperty); set => SetValue(SideWidthProperty, value); }
    public double Breakpoint { get => (double)GetValue(BreakpointProperty); set => SetValue(BreakpointProperty, value); }
    public double Gap { get => (double)GetValue(GapProperty); set => SetValue(GapProperty, value); }

    private bool Wide(double width) => !double.IsInfinity(width) && width >= Breakpoint && InternalChildren.Count >= 2;

    protected override Size MeasureOverride(Size available)
    {
        if (InternalChildren.Count == 0) return default;
        if (Wide(available.Width))
        {
            double main = Math.Max(0, available.Width - SideWidth - Gap);
            InternalChildren[0].Measure(new Size(main, available.Height)); InternalChildren[1].Measure(new Size(SideWidth, available.Height));
            return new Size(available.Width, Math.Max(InternalChildren[0].DesiredSize.Height, InternalChildren[1].DesiredSize.Height));
        }
        double height = 0, width = 0;
        foreach (UIElement child in InternalChildren) { child.Measure(new Size(available.Width, double.PositiveInfinity)); height += child.DesiredSize.Height; width = Math.Max(width, child.DesiredSize.Width); }
        return new Size(double.IsInfinity(available.Width) ? width : available.Width, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        if (InternalChildren.Count == 0) return final;
        if (Wide(final.Width))
        {
            double main = Math.Max(0, final.Width - SideWidth - Gap);
            InternalChildren[0].Arrange(new Rect(0, 0, main, final.Height)); InternalChildren[1].Arrange(new Rect(main + Gap, 0, SideWidth, final.Height));
            return final;
        }
        double y = 0;
        foreach (UIElement child in InternalChildren) { child.Arrange(new Rect(0, y, final.Width, child.DesiredSize.Height)); y += child.DesiredSize.Height; }
        return final;
    }
}
