using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Twentea.Windows;

/// <summary>Small building blocks shared by the tray panel and the settings window. Styles live in Theme.xaml.</summary>
static class Ui
{
    public static Style Look(string key) => (Style)Application.Current.FindResource(key);

    public static TextBlock Text(string text, double size = 14, FontWeight? weight = null, string brush = "TextFillColorPrimaryBrush")
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    public static TextBlock Secondary(string text, double size = 12) => Text(text, size, brush: "TextFillColorSecondaryBrush");

    public static TextBlock Glyph(string glyph, double size = 16)
    {
        var block = new TextBlock { Text = glyph, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        return block;
    }

    public static Button Button(string label, Action onClick, string style = "")
    {
        var button = new Button { Content = label, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        if (style.Length > 0) button.Style = Look(style);
        button.Click += (_, _) => onClick();
        return button;
    }

    public static Border Divider() => Brushed(new Border { Height = 1, Margin = new Thickness(0, 10, 0, 10) }, Border.BackgroundProperty, "DividerStrokeColorDefaultBrush");

    public static T Brushed<T>(T element, DependencyProperty property, string key) where T : FrameworkElement
    {
        element.SetResourceReference(property, key);
        return element;
    }

    /// <summary>A rounded surface with a soft shadow, for borderless windows such as the tray panel.</summary>
    public static Border Surface(UIElement child) => new Border
    {
        CornerRadius = new CornerRadius(8),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(18),
        Margin = new Thickness(14),
        Child = child,
        Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.28 },
    }.With(b =>
    {
        b.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        b.SetResourceReference(Border.BorderBrushProperty, "SurfaceStrokeColorDefaultBrush");
    });

    public static T With<T>(this T value, Action<T> setup)
    {
        setup(value);
        return value;
    }
}

/// <summary>The big round on/off switch, as on the Mac. Orange when on, grey when off.</summary>
sealed class PowerButton : Button
{
    private readonly Ellipse _fill = new();
    private readonly Ellipse _ring = new() { StrokeThickness = 1.5 };
    private readonly TextBlock _glyph = Ui.Glyph("");
    private readonly Grid _face;

    public PowerButton(double size, Action onClick)
    {
        Style = Ui.Look("BareButton");
        _glyph.FontSize = size * 0.38;
        _glyph.HorizontalAlignment = HorizontalAlignment.Center;
        _face = new Grid { Width = size, Height = size, Children = { _fill, _ring, _glyph } };
        Content = _face;
        Click += (_, _) => onClick();
    }

    public void Refresh(bool on)
    {
        if (on)
        {
            _fill.Fill = (Brush)Application.Current.FindResource("TeaPowerOnBrush");
            _ring.Stroke = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
            _glyph.Foreground = Brushes.White;
            _face.Effect = new DropShadowEffect { Color = Color.FromRgb(0xE8, 0x86, 0x2A), BlurRadius = _face.Width * 0.25, ShadowDepth = 0, Opacity = 0.55 };
            ToolTip = "Turn Twentea off";
        }
        else
        {
            _fill.SetResourceReference(Shape.FillProperty, "ControlAltFillColorSecondaryBrush");
            _ring.SetResourceReference(Shape.StrokeProperty, "ControlStrongStrokeColorDefaultBrush");
            _glyph.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            _face.Effect = null;
            ToolTip = "Turn Twentea on";
        }
    }
}
