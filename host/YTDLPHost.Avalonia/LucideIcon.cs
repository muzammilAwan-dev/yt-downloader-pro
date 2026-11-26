using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace YTDLPHost.UI;

/// <summary>
/// A Lucide icon: path data drawn in a 24 x 24 box and scaled to the control's size, as a round-capped outline (or filled for play and
/// pause). The colour is the inherited foreground, so an icon inside a button follows the button's hover colour.
/// (WPF counterpart: the LucideIcon styles of main's App.xaml. Same path data, same stroke widths.)
/// </summary>
public sealed class LucideIcon : Control
{
    public static readonly StyledProperty<Geometry?> DataProperty = AvaloniaProperty.Register<LucideIcon, Geometry?>(nameof(Data));
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<LucideIcon>();
    public static readonly StyledProperty<double> StrokeWidthProperty = AvaloniaProperty.Register<LucideIcon, double>(nameof(StrokeWidth), 2.2);
    public static readonly StyledProperty<bool> IsSolidProperty = AvaloniaProperty.Register<LucideIcon, bool>(nameof(IsSolid));

    static LucideIcon()
    {
        AffectsRender<LucideIcon>(DataProperty, ForegroundProperty, StrokeWidthProperty, IsSolidProperty);
        AffectsMeasure<LucideIcon>(WidthProperty, HeightProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<LucideIcon>(false);
        FocusableProperty.OverrideDefaultValue<LucideIcon>(false);
    }

    public Geometry? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double StrokeWidth { get => GetValue(StrokeWidthProperty); set => SetValue(StrokeWidthProperty, value); }
    public bool IsSolid { get => GetValue(IsSolidProperty); set => SetValue(IsSolidProperty, value); }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsNaN(Width) ? 16 : Width, double.IsNaN(Height) ? 16 : Height);

    public override void Render(DrawingContext context)
    {
        var data = Data;
        var brush = Foreground;
        if (data is null || brush is null) return;

        double scale = Math.Min(Bounds.Width, Bounds.Height) / 24.0;
        double dx = (Bounds.Width - 24 * scale) / 2, dy = (Bounds.Height - 24 * scale) / 2;
        var pen = new Pen(brush, IsSolid ? 2 : StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(dx, dy)))
        {
            context.DrawGeometry(IsSolid ? brush : null, pen, data);
        }
    }
}
