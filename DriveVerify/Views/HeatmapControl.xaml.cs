using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
        if (d is HeatmapControl control && e.NewValue is RegionStatus[] statuses)
        {
            control.RenderHeatmap(statuses);
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

        int cellWidth = 1;
        int cellHeight = 1;

        int pixelWidth = columns * cellWidth;
        int pixelHeight = rows * cellHeight;

        var bitmap = new WriteableBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[pixelWidth * pixelHeight * 4];

        for (int i = 0; i < totalCells; i++)
        {
            int col = i % columns;
            int row = i / columns;
            var color = GetColorForStatus(statuses[i]);

            int pixelOffset = (row * cellHeight * pixelWidth + col * cellWidth) * 4;
            pixels[pixelOffset + 0] = color.B;
            pixels[pixelOffset + 1] = color.G;
            pixels[pixelOffset + 2] = color.R;
            pixels[pixelOffset + 3] = color.A;
        }

        bitmap.WritePixels(new Int32Rect(0, 0, pixelWidth, pixelHeight), pixels, pixelWidth * 4, 0);
        HeatmapImage.Source = bitmap;
    }

    private static Color GetColorForStatus(RegionStatus status)
    {
        return status switch
        {
            RegionStatus.Untested => Color.FromRgb(0x3C, 0x3C, 0x3C),
            RegionStatus.Writing => Color.FromRgb(0x21, 0x96, 0xF3),
            RegionStatus.Verifying => Color.FromRgb(0xFF, 0x98, 0x00),
            RegionStatus.Good => Color.FromRgb(0x4C, 0xAF, 0x50),
            RegionStatus.Bad => Color.FromRgb(0xF4, 0x43, 0x36),
            _ => Color.FromRgb(0x3C, 0x3C, 0x3C)
        };
    }
}
