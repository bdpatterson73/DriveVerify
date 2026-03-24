namespace DriveVerify.Models;

public class TestPlan
{
    public Guid SessionId { get; set; } = Guid.NewGuid();
    public DriveItem TargetDrive { get; set; } = null!;
    public TestMode Mode { get; set; } = TestMode.FullCapacity;
    public long TestSizeBytes { get; set; }
    public int BlockSizeBytes { get; set; } = 4 * 1024 * 1024;
    public int VerifyThreadCount { get; set; } = Math.Min(Environment.ProcessorCount, 8);
    public string TestFolderPath { get; set; } = string.Empty;

    public int ComputeTotalBlocks()
    {
        if (BlockSizeBytes <= 0) return 0;
        return (int)((TestSizeBytes + BlockSizeBytes - 1) / BlockSizeBytes);
    }
}
