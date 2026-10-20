using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace DriveVerify.Views;

public partial class SpeedGraphControl : UserControl
{
    public static readonly DependencyProperty SamplesProperty =
        DependencyProperty.Register(nameof(Samples), typeof(double[]), typeof(SpeedGraphControl),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty GraphColorProperty =
        DependencyProperty.Register(nameof(GraphColor), typeof(Color), typeof(SpeedGraphControl),
            new PropertyMetadata(Color.FromRgb(0x39, 0xFF, 0x6A), OnVisualPropertyChanged));

    public double[]? Samples
    {
        get => (double[]?)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public Color GraphColor
    {
        get => (Color)GetValue(GraphColorProperty);
        set => SetValue(GraphColorProperty, value);
    }

    private readonly DropShadowEffect _glow = new()
    {
        BlurRadius = 10,
        ShadowDepth = 0,
        Opacity = 0.9
    };

    public SpeedGraphControl()
    {
        InitializeComponent();
        TraceLine.Effect = _glow;
        Loaded += (_, _) => Redraw();
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SpeedGraphControl)d).Redraw();

    private void GraphRoot_SizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        double width = GraphRoot.ActualWidth;
        double height = GraphRoot.ActualHeight;
        if (width < 2 || height < 2) return;

        DrawGridLines(width, height);

        var color = GraphColor;
        _glow.Color = color;
        TraceLine.Stroke = new SolidColorBrush(color);
        AreaPolygon.Fill = new LinearGradientBrush(
            Color.FromArgb(0x50, color.R, color.G, color.B),
            Color.FromArgb(0x00, color.R, color.G, color.B),
            90);

        var samples = Samples;
        if (samples is not { Length: > 1 })
        {
            TraceLine.Points = [];
            AreaPolygon.Points = [];
            PeakLabel.Text = string.Empty;
            return;
        }

        double max = 0;
        foreach (var s in samples)
            if (s > max) max = s;
        if (max <= 0) max = 1;
        max *= 1.1; // headroom

        var tracePoints = new PointCollection(samples.Length);
        double stepX = width / (samples.Length - 1);
        for (int i = 0; i < samples.Length; i++)
        {
            double x = i * stepX;
            double y = height - (samples[i] / max * height);
            tracePoints.Add(new Point(x, y));
        }

        var areaPoints = new PointCollection(tracePoints) { new Point(width, height), new Point(0, height) };

        tracePoints.Freeze();
        areaPoints.Freeze();
        TraceLine.Points = tracePoints;
        AreaPolygon.Points = areaPoints;

        PeakLabel.Text = $"peak {SizeFormatterText(max / 1.1)}/s";
    }

    private void DrawGridLines(double width, double height)
    {
        GridCanvas.Children.Clear();
        var gridBrush = new SolidColorBrush(Color.FromRgb(0x14, 0x1C, 0x24));
        gridBrush.Freeze();
        for (int i = 1; i <= 3; i++)
        {
            double y = height * i / 4.0;
            GridCanvas.Children.Add(new Line
            {
                X1 = 0,
                X2 = width,
                Y1 = y,
                Y2 = y,
                Stroke = gridBrush,
                StrokeThickness = 1
            });
        }
    }

    private static string SizeFormatterText(double bytesPerSec)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        int unit = 0;
        while (bytesPerSec >= 1024 && unit < units.Length - 1)
        {
            bytesPerSec /= 1024;
            unit++;
        }
        return $"{bytesPerSec:F1} {units[unit]}";
    }
}
