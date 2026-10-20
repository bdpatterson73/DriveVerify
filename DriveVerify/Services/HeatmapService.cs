using System.Text;
using DriveVerify.Models;

namespace DriveVerify.Services;

public class HeatmapCell
{
    public int Index { get; set; }
    public RegionStatus Status { get; set; }
    public string Color { get; set; } = string.Empty;
}

public static class HeatmapService
{
    private static readonly Dictionary<RegionStatus, string> ColorMap = new()
    {
        [RegionStatus.Untested] = "#1C222C",
        [RegionStatus.Writing] = "#00B0FF",
        [RegionStatus.Verifying] = "#FFB300",
        [RegionStatus.Good] = "#39FF6A",
        [RegionStatus.Bad] = "#FF3B4A"
    };

    public static string GetColor(RegionStatus status) =>
        ColorMap.GetValueOrDefault(status, "#1C222C");

    public static List<HeatmapCell> BuildCells(RegionStatus[] regionStatuses)
    {
        var cells = new List<HeatmapCell>(regionStatuses.Length);
        for (int i = 0; i < regionStatuses.Length; i++)
        {
            cells.Add(new HeatmapCell
            {
                Index = i,
                Status = regionStatuses[i],
                Color = GetColor(regionStatuses[i])
            });
        }
        return cells;
    }

    public static string GenerateSvg(RegionStatus[] regionStatuses)
    {
        if (regionStatuses.Length == 0)
            return "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"800\" height=\"20\"></svg>";

        int totalCells = regionStatuses.Length;
        int columns = Math.Min(totalCells, 100);
        int rows = (totalCells + columns - 1) / columns;
        int cellSize = 8;
        int svgWidth = columns * cellSize;
        int svgHeight = rows * cellSize;

        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{svgWidth}\" height=\"{svgHeight}\">");

        for (int i = 0; i < totalCells; i++)
        {
            int col = i % columns;
            int row = i / columns;
            string color = GetColor(regionStatuses[i]);
            sb.Append($"<rect x=\"{col * cellSize}\" y=\"{row * cellSize}\" width=\"{cellSize}\" height=\"{cellSize}\" fill=\"{color}\" />");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }
}
