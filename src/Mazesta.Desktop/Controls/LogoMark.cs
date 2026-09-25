using System.Globalization; using System.Windows; using System.Windows.Controls; using System.Windows.Data; using System.Windows.Documents; using System.Windows.Media; using System.Windows.Shapes;
namespace Mazesta.Desktop.Controls;

/// <summary>The Mazesta logo in the app, drawn from the same paths as the report header (Reporting.MazestaLogo) in the Foreground colour.
/// Always left-to-right: under an RTL page the paths would otherwise be mirrored.</summary>
public sealed class LogoMark : Viewbox
{
    public LogoMark()
    {
        FlowDirection = FlowDirection.LeftToRight;
        var box = Mazesta.Reporting.MazestaLogo.ViewBox.Split(' ').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        var canvas = new Canvas { Width = box[2], Height = box[3] };
        foreach (var d in Mazesta.Reporting.MazestaLogo.Paths)
        {
            var path = new Path { Data = Geometry.Parse(d) };
            path.SetBinding(Shape.FillProperty, new Binding { Path = new PropertyPath(TextElement.ForegroundProperty), Source = this });
            canvas.Children.Add(path);
        }
        Child = canvas;
    }

    public Brush Foreground { get => (Brush)GetValue(TextElement.ForegroundProperty); set => SetValue(TextElement.ForegroundProperty, value); }
}
