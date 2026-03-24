namespace DriveVerify.Models;

public enum Verdict
{
    Verified,
    Suspect,
    FakeCapacityDetected,
    CorruptionDetected,
    Cancelled
}

public class TestResult
{
    public Guid SessionId { get; set; }
    public string DriveLetter { get; set; } = string.Empty;
    public string VolumeLabel { get; set; } = string.Empty;
    public string FileSystem { get; set; } = string.Empty;
    public long TotalSize { get; set; }
    public long FreeSpace { get; set; }
    public bool IsRemovable { get; set; }
    public TestMode Mode { get; set; }
    public long ConfiguredTestSize { get; set; }
    public long ActualBytesWritten { get; set; }
    public long ActualBytesVerified { get; set; }
    public double WriteSpeedBytesPerSec { get; set; }
    public double ReadSpeedBytesPerSec { get; set; }
    public TimeSpan Duration { get; set; }
    public long? FirstFailureOffset { get; set; }
    public long VerifiedGoodBytes { get; set; }
    public List<VerificationIssue> Issues { get; set; } = [];
    public RegionStatus[] RegionMap { get; set; } = [];
    public Verdict Verdict { get; set; }
}
