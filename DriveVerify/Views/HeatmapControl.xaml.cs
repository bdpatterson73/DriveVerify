using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using DriveVerify.Models;
using DriveVerify.Services;

namespace DriveVerify.Views;

public partial class HeatmapControl : UserControl
{
    public static readonly DependencyProperty RegionStatusesProperty =
        DependencyProperty.Register(
            nameof(RegionStatuses),
            typeof(RegionStatus[]),
            typeof(HeatmapControl),
            new PropertyMetadata(null, OnRegionStatusesChanged));

    public RegionStatus[]? RegionStatuses
    {
        get => (RegionStatus[]?)GetValue(RegionStatusesProperty);
        set => SetValue(RegionStatusesProperty, value);
    }

    public HeatmapControl()
    {
        InitializeComponent();
    }

    private static void OnRegionStatusesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HeatmapControl control)
        {
            if (e.NewValue is RegionStatus[] statuses)
            {
                control.RenderHeatmap(statuses);
                control.UpdateScanLine(statuses);
            }
            else
            {
                control.HeatmapImage.Source = null;
                control.UpdateScanLine(null);
            }
        }
    }

    private bool _scanRunning;

    private void UpdateScanLine(RegionStatus[]? statuses)
    {
        bool active = false;
        if (statuses is not null)
        {
            foreach (var s in statuses)
            {
                if (s == RegionStatus.Writing || s == RegionStatus.Verifying)
                {
                    active = true;
                    break;
                }
            }
        }

        if (active && !_scanRunning)
        {
            _scanRunning = true;
            ScanLine.Visibility = Visibility.Visible;
            var sweep = new DoubleAnimation
            {
                From = -ScanLine.Width,
                To = ActualWidth,
                Duration = TimeSpan.FromSeconds(2.4),
                RepeatBehavior = RepeatBehavior.Forever
            };
            ScanLine.BeginAnimation(Canvas.LeftProperty, sweep);
        }
        else if (!active && _scanRunning)
        {
            _scanRunning = false;
            ScanLine.BeginAnimation(Canvas.LeftProperty, null);
            ScanLine.Visibility = Visibility.Collapsed;
        }
    }

    private void RenderHeatmap(RegionStatus[] statuses)
    {
        if (statuses.Length == 0)
        {
            HeatmapImage.Source = null;
            return;
        }

        int totalCells = statuses.Length;
        int columns = Math.Min(totalCells, 128);
        int rows = (totalCells + columns - 1) / columns;

        // Tiled cells: colored block with a 1px dark gutter and a brighter top edge for depth.
        // Fall back to flat 1px cells for very large region maps to keep the 66ms refresh cheap.
        bool tiled = totalCells <= 24_000;
        int cellWidth = tiled ? 6 : 1;
        int cellHeight = tiled ? 6 : 1;

        int pixelWidth = columns * cellWidth;
        int pixelHeight = rows * cellHeight;

        var bitmap = new WriteableBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[pixelWidth * pixelHeight * 4];

        var gutter = Color.FromRgb(0x0A, 0x0C, 0x10);

        for (int i = 0; i < totalCells; i++)
        {
            int col = i % columns;
            int row = i / columns;
            var color = GetColorForStatus(statuses[i]);
            var bright = Brighten(color, 1.35);
            var dim = Brighten(color, 0.65);

            for (int y = 0; y < cellHeight; y++)
            {
                int py = row * cellHeight + y;
                for (int x = 0; x < cellWidth; x++)
                {
                    int px = col * cellWidth + x;
                    Color c;
                    if (!tiled)
                        c = color;
                    else if (x == cellWidth - 1 || y == cellHeight - 1)
                        c = gutter;                   // gutter between cells
                    else if (y == 0)
                        c = bright;                   // highlight top edge
                    else if (y == cellHeight - 2)
                        c = dim;                      // shaded bottom edge
                    else
                        c = color;

                    int offset = (py * pixelWidth + px) * 4;
                    pixels[offset + 0] = c.B;
                    pixels[offset + 1] = c.G;
                    pixels[offset + 2] = c.R;
                    pixels[offset + 3] = 0xFF;
                }
            }
        }

        bitmap.WritePixels(new Int32Rect(0, 0, pixelWidth, pixelHeight), pixels, pixelWidth * 4, 0);
        HeatmapImage.Source = bitmap;
    }

    private static Color Brighten(Color c, double factor)
    {
        return Color.FromRgb(
            (byte)Math.Min(255, c.R * factor),
            (byte)Math.Min(255, c.G * factor),
            (byte)Math.Min(255, c.B * factor));
    }

    private static Color GetColorForStatus(RegionStatus status)
    {
        return status switch
        {
            RegionStatus.Untested => Color.FromRgb(0x1C, 0x22, 0x2C),
            RegionStatus.Writing => Color.FromRgb(0x00, 0xB0, 0xFF),
            RegionStatus.Verifying => Color.FromRgb(0xFF, 0xB3, 0x00),
            RegionStatus.Good => Color.FromRgb(0x39, 0xFF, 0x6A),
            RegionStatus.Bad => Color.FromRgb(0xFF, 0x3B, 0x4A),
            _ => Color.FromRgb(0x1C, 0x22, 0x2C)
        };
    }
}
