namespace DriveVerify.Models;

public class DriveItem
{
    public string DriveLetter { get; set; } = string.Empty;
    public string VolumeLabel { get; set; } = string.Empty;
    public string FileSystem { get; set; } = string.Empty;
    public long TotalSize { get; set; }
    public long FreeSpace { get; set; }
    public bool IsRemovable { get; set; }
    public bool IsReady { get; set; }

    public string DisplayName
    {
        get
        {
            string label = string.IsNullOrWhiteSpace(VolumeLabel) ? "Removable Disk" : VolumeLabel;
            string size = Helpers.SizeFormatter.Format(TotalSize);
            return $"{DriveLetter} — {label} ({size}, {FileSystem})";
        }
    }

    public override string ToString() => DisplayName;
}
