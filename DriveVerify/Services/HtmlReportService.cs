using System.Diagnostics;
using System.IO;
using System.Text;
using DriveVerify.Helpers;
using DriveVerify.Models;

namespace DriveVerify.Services;

public static class HtmlReportService
{
    public static string GenerateReport(HtmlReportModel model)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"UTF-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine("<title>FlashDriveTester Report</title>");
        sb.AppendLine("<style>");
        sb.AppendLine(GetCss());
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        // Header
        sb.AppendLine("<div class=\"header\">");
        sb.AppendLine("<h1>FlashDriveTester Report</h1>");
        sb.AppendLine($"<p>Version {Encode(model.AppVersion)} &mdash; {model.ReportDateUtc:yyyy-MM-dd HH:mm:ss} UTC</p>");
        sb.AppendLine("</div>");

        // Drive Info
        sb.AppendLine("<div class=\"card\">");
        sb.AppendLine("<h2>Drive Information</h2>");
        sb.AppendLine("<table>");
        sb.AppendLine($"<tr><td>Drive</td><td>{Encode(model.DriveLetter)}</td></tr>");
        sb.AppendLine($"<tr><td>Volume Label</td><td>{Encode(model.VolumeLabel)}</td></tr>");
        sb.AppendLine($"<tr><td>File System</td><td>{Encode(model.FileSystem)}</td></tr>");
        sb.AppendLine($"<tr><td>Total Size</td><td>{SizeFormatter.Format(model.TotalSize)}</td></tr>");
        sb.AppendLine($"<tr><td>Free Space</td><td>{SizeFormatter.Format(model.FreeSpace)}</td></tr>");
        sb.AppendLine($"<tr><td>Removable</td><td>{(model.IsRemovable ? "Yes" : "No")}</td></tr>");
        sb.AppendLine("</table>");
        sb.AppendLine("</div>");

        // Test Configuration
        sb.AppendLine("<div class=\"card\">");
        sb.AppendLine("<h2>Test Configuration</h2>");
        sb.AppendLine("<table>");
        sb.AppendLine($"<tr><td>Mode</td><td>{model.Mode}</td></tr>");
        sb.AppendLine($"<tr><td>Block Size</td><td>{SizeFormatter.Format(model.BlockSizeBytes)}</td></tr>");
        sb.AppendLine($"<tr><td>Configured Test Size</td><td>{SizeFormatter.Format(model.ConfiguredTestSize)}</td></tr>");
        sb.AppendLine($"<tr><td>Verify Threads</td><td>{model.VerifyThreadCount}</td></tr>");
        sb.AppendLine($"<tr><td>Session ID</td><td>{model.SessionId}</td></tr>");
        sb.AppendLine("</table>");
        sb.AppendLine("</div>");

        // Results Summary
        sb.AppendLine("<div class=\"card\">");
        sb.AppendLine("<h2>Results Summary</h2>");
        sb.AppendLine("<table>");
        sb.AppendLine($"<tr><td>Bytes Written</td><td>{SizeFormatter.Format(model.ActualBytesWritten)}</td></tr>");
        sb.AppendLine($"<tr><td>Bytes Verified</td><td>{SizeFormatter.Format(model.ActualBytesVerified)}</td></tr>");
        sb.AppendLine($"<tr><td>Write Speed</td><td>{SizeFormatter.Format((long)model.WriteSpeedBytesPerSec)}/s</td></tr>");
        sb.AppendLine($"<tr><td>Read Speed</td><td>{SizeFormatter.Format((long)model.ReadSpeedBytesPerSec)}/s</td></tr>");
        sb.AppendLine($"<tr><td>Duration</td><td>{TimeFormatter.Format(model.Duration)}</td></tr>");
        sb.AppendLine("</table>");
        sb.AppendLine("</div>");

        // Verdict
        string verdictClass = model.Verdict switch
        {
            Verdict.Verified => "verdict-verified",
            Verdict.Suspect => "verdict-suspect",
            Verdict.Cancelled => "verdict-cancelled",
            _ => "verdict-failed"
        };
        string verdictIcon = model.Verdict switch
        {
            Verdict.Verified => "&#x2713;",
            Verdict.Cancelled => "&#x2014;",
            _ => "&#x2717;"
        };
        sb.AppendLine($"<div class=\"verdict {verdictClass}\">");
        sb.AppendLine($"<span class=\"verdict-icon\">{verdictIcon}</span> {model.Verdict}");
        sb.AppendLine("</div>");

        // Capacity Analysis
        sb.AppendLine("<div class=\"card\">");
        sb.AppendLine("<h2>Capacity Analysis</h2>");
        sb.AppendLine("<table>");
        if (model.FirstFailureOffset.HasValue)
            sb.AppendLine($"<tr><td>First Failure Offset</td><td>{SizeFormatter.Format(model.FirstFailureOffset.Value)}</td></tr>");
        else
            sb.AppendLine("<tr><td>First Failure Offset</td><td>None</td></tr>");
        sb.AppendLine($"<tr><td>Verified Good Capacity</td><td>{SizeFormatter.Format(model.VerifiedGoodBytes)}</td></tr>");
        double pct = model.TotalSize > 0 ? (double)model.VerifiedGoodBytes / model.TotalSize * 100 : 0;
        sb.AppendLine($"<tr><td>Real vs. Reported</td><td>{pct:F1}%</td></tr>");
        sb.AppendLine("</table>");
        sb.AppendLine("</div>");

        // Issues Table
        if (model.Issues.Count > 0)
        {
            sb.AppendLine("<div class=\"card\">");
            sb.AppendLine("<h2>Issues</h2>");
            sb.AppendLine("<table class=\"issues-table\">");
            sb.AppendLine("<tr><th>Block</th><th>Offset</th><th>Kind</th><th>Detail</th></tr>");
            foreach (var issue in model.Issues)
            {
                sb.AppendLine($"<tr><td>{issue.BlockIndex}</td><td>{SizeFormatter.Format(issue.AbsoluteOffset)}</td><td>{issue.IssueKind}</td><td>{Encode(issue.Detail)}</td></tr>");
            }
            sb.AppendLine("</table>");
            sb.AppendLine("</div>");
        }

        // Heatmap
        if (!string.IsNullOrEmpty(model.HeatmapSvgFragment))
        {
            sb.AppendLine("<div class=\"card\">");
            sb.AppendLine("<h2>Heatmap</h2>");
            sb.AppendLine("<div class=\"heatmap-container\">");
            sb.AppendLine(model.HeatmapSvgFragment);
            sb.AppendLine("</div>");
            sb.AppendLine("<div class=\"legend\">");
            sb.AppendLine("<span class=\"legend-item\"><span class=\"legend-color\" style=\"background:#4CAF50\"></span>Good</span>");
            sb.AppendLine("<span class=\"legend-item\"><span class=\"legend-color\" style=\"background:#F44336\"></span>Bad</span>");
            sb.AppendLine("<span class=\"legend-item\"><span class=\"legend-color\" style=\"background:#3C3C3C\"></span>Untested</span>");
            sb.AppendLine("</div>");
            sb.AppendLine("</div>");
        }

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    public static async Task<string> SaveAndOpenAsync(string html, string folderPath)
    {
        Directory.CreateDirectory(folderPath);
        string fileName = $"FlashDriveTester_Report_{DateTime.UtcNow:yyyyMMdd_HHmmss}.html";
        string filePath = Path.Combine(folderPath, fileName);
        await File.WriteAllTextAsync(filePath, html);

        try
        {
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
        }
        catch
        {
            // Browser open is best-effort
        }

        return filePath;
    }

    private static string Encode(string text) =>
        System.Net.WebUtility.HtmlEncode(text ?? string.Empty);

    private static string GetCss() => """
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background: #1e1e1e; color: #d4d4d4; padding: 20px; max-width: 900px; margin: 0 auto; }
        .header { text-align: center; padding: 20px 0; border-bottom: 2px solid #333; margin-bottom: 20px; }
        .header h1 { color: #569cd6; font-size: 1.8em; }
        .header p { color: #808080; margin-top: 5px; }
        .card { background: #252526; border: 1px solid #333; border-radius: 8px; padding: 16px; margin-bottom: 16px; }
        .card h2 { color: #569cd6; font-size: 1.1em; margin-bottom: 12px; border-bottom: 1px solid #333; padding-bottom: 8px; }
        table { width: 100%; border-collapse: collapse; }
        td, th { padding: 6px 12px; text-align: left; border-bottom: 1px solid #333; }
        th { color: #569cd6; }
        .issues-table { font-size: 0.9em; }
        .verdict { text-align: center; font-size: 1.5em; font-weight: bold; padding: 20px; border-radius: 8px; margin-bottom: 16px; }
        .verdict-icon { font-size: 1.3em; }
        .verdict-verified { background: #4CAF50; color: white; }
        .verdict-suspect { background: #FF9800; color: #1e1e1e; }
        .verdict-failed { background: #F44336; color: white; }
        .verdict-cancelled { background: #555; color: white; }
        .heatmap-container { overflow-x: auto; padding: 8px 0; }
        .legend { margin-top: 8px; display: flex; gap: 16px; }
        .legend-item { display: flex; align-items: center; gap: 4px; font-size: 0.85em; }
        .legend-color { width: 12px; height: 12px; border-radius: 2px; display: inline-block; }
        """;
}
