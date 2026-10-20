using System.IO;
using DriveVerify.Models;

namespace DriveVerify.Services;

public class WriteProgress
{
    public string Phase { get; set; } = "Writing";
    public int FileIndex { get; set; }
    public int BlockIndex { get; set; }
    public long BytesWritten { get; set; }
    public long TotalBytes { get; set; }
    public double SpeedBytesPerSec { get; set; }
    public TimeSpan Elapsed { get; set; }
    public TimeSpan EstimatedRemaining { get; set; }
    public int RegionIndex { get; set; }
    public bool IsWriting { get; set; }
}

public class WritePhaseResult
{
    public long TotalBytesWritten { get; set; }
    public TimeSpan ElapsedTime { get; set; }
    public double AverageSpeedBytesPerSec { get; set; }
    public int BlockCount { get; set; }
    public int FileCount { get; set; }
    public List<Exception> Exceptions { get; set; } = [];
}

public class FileTestWriterService
{
    private const long Fat32FileSizeLimit = 250L * 1024 * 1024; // 250 MB limit for FAT32
    private const long NonFat32FileSizeLimit = 1L * 1024 * 1024 * 1024; // 1 GB limit for other file systems

    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);

    public async Task<WritePhaseResult> WriteAsync(
        TestPlan plan,
        IProgress<WriteProgress> progress,
        CancellationToken ct,
        RegionStatus[]? liveRegionMap = null)
    {
        var result = new WritePhaseResult();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        long lastReportTicks = 0;

        Directory.CreateDirectory(plan.TestFolderPath);

        int totalBlocks = plan.ComputeTotalBlocks();
        long fileSizeLimit = GetFileSizeLimit(plan.TargetDrive.FileSystem);

        int fileIndex = 0;
        long currentFileSize = 0;
        FileStream? currentStream = null;
        long totalBytesWritten = 0;

        try
        {
            int[] blockIndices = GetBlockIndices(plan, totalBlocks);
            int maxPayloadLength = plan.BlockSizeBytes - TestBlockHeader.SerializedSize;
            byte[] blockBuffer = new byte[plan.BlockSizeBytes];
            var driveInfo = new DriveInfo(Path.GetPathRoot(plan.TestFolderPath)!);

            for (int i = 0; i < blockIndices.Length; i++)
            {
                ct.ThrowIfCancellationRequested();

                int blockIndex = blockIndices[i];
                long absoluteOffset = (long)blockIndex * plan.BlockSizeBytes;

                // Shrink the final block so the entire requested size is covered,
                // even when it isn't a multiple of the block size.
                int payloadLength = maxPayloadLength;
                if (plan.Mode == TestMode.FullCapacity)
                {
                    long remaining = plan.TestSizeBytes - totalBytesWritten;

                    // Near the end, trust the live free space over the snapshot taken at
                    // test start (directory entries / cluster rounding consume a little).
                    if (remaining <= plan.BlockSizeBytes)
                        remaining = Math.Max(remaining, driveInfo.AvailableFreeSpace);

                    if (remaining <= TestBlockHeader.SerializedSize)
                        break;
                    payloadLength = (int)Math.Min(maxPayloadLength, remaining - TestBlockHeader.SerializedSize);
                }
                int blockTotalSize = TestBlockHeader.SerializedSize + payloadLength;

                // Generate payload directly into block buffer (no allocation)
                TestPatternService.GeneratePayloadInto(
                    plan.SessionId, blockIndex, absoluteOffset,
                    blockBuffer.AsSpan(TestBlockHeader.SerializedSize, payloadLength));
                uint checksum = ChecksumService.Compute(
                    blockBuffer.AsSpan(TestBlockHeader.SerializedSize, payloadLength));

                // Build header
                var header = new TestBlockHeader
                {
                    SessionId = plan.SessionId,
                    FileIndex = fileIndex,
                    BlockIndex = blockIndex,
                    AbsoluteOffset = absoluteOffset,
                    TimestampUtc = DateTime.UtcNow.Ticks,
                    PayloadLength = payloadLength,
                    PayloadChecksum = checksum
                };

                // Check if we need a new file
                if (currentStream == null || currentFileSize + blockTotalSize > fileSizeLimit)
                {
                    if (currentStream != null)
                    {
                        await currentStream.FlushAsync(ct).ConfigureAwait(false);
                        await currentStream.DisposeAsync().ConfigureAwait(false);
                        result.FileCount++;
                        fileIndex++;  // Increment for next file
                    }

                    string filePath = Path.Combine(plan.TestFolderPath, $"testdata_{fileIndex:D4}.dat");
                    currentStream = new FileStream(filePath, FileMode.Create, FileAccess.Write,
                        FileShare.None, bufferSize: 1,
                        FileOptions.SequentialScan | FileOptions.Asynchronous);
                    currentFileSize = 0;

                    // Update fileIndex in header
                    header.FileIndex = fileIndex;
                }

                // Serialize header into start of block buffer (slice to avoid clearing payload)
                header.Serialize(blockBuffer.AsSpan(0, TestBlockHeader.SerializedSize));

                // Mark region as actively writing (UI polls this map on a timer)
                if (liveRegionMap != null && blockIndex >= 0 && blockIndex < liveRegionMap.Length)
                    liveRegionMap[blockIndex] = RegionStatus.Writing;

                // Write entire block in a single I/O operation
                try
                {
                    await currentStream!.WriteAsync(blockBuffer.AsMemory(0, blockTotalSize), ct).ConfigureAwait(false);
                }
                catch (IOException ex) when (IsDiskFull(ex))
                {
                    // Drive is genuinely full — roll back the partial block and finish cleanly.
                    currentStream!.SetLength(currentFileSize);
                    if (liveRegionMap != null && blockIndex >= 0 && blockIndex < liveRegionMap.Length)
                        liveRegionMap[blockIndex] = RegionStatus.Untested;
                    break;
                }
                currentFileSize += blockTotalSize;
                totalBytesWritten += blockTotalSize;

                if (liveRegionMap != null && blockIndex >= 0 && blockIndex < liveRegionMap.Length)
                    liveRegionMap[blockIndex] = RegionStatus.Good;

                // Throttled progress report (and always on the final block)
                long nowTicks = stopwatch.Elapsed.Ticks;
                bool isLast = i == blockIndices.Length - 1;
                if (isLast || nowTicks - lastReportTicks >= ProgressInterval.Ticks)
                {
                    lastReportTicks = nowTicks;

                    double elapsed = stopwatch.Elapsed.TotalSeconds;
                    double speed = elapsed > 0 ? totalBytesWritten / elapsed : 0;
                    double fraction = (double)(i + 1) / blockIndices.Length;
                    TimeSpan eta = fraction > 0
                        ? TimeSpan.FromSeconds(stopwatch.Elapsed.TotalSeconds / fraction * (1 - fraction))
                        : TimeSpan.Zero;

                    progress.Report(new WriteProgress
                    {
                        Phase = "Writing",
                        FileIndex = fileIndex,
                        BlockIndex = blockIndex,
                        BytesWritten = totalBytesWritten,
                        TotalBytes = plan.TestSizeBytes,
                        SpeedBytesPerSec = speed,
                        Elapsed = stopwatch.Elapsed,
                        EstimatedRemaining = eta,
                        RegionIndex = blockIndex,
                        IsWriting = false
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result.Exceptions.Add(ex);
            // Exception will end the write phase - partial results will be returned
        }
        finally
        {
            if (currentStream != null)
            {
                try
                {
                    await currentStream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch { }
                await currentStream.DisposeAsync().ConfigureAwait(false);
                result.FileCount++;
            }
        }

        stopwatch.Stop();
        result.TotalBytesWritten = totalBytesWritten;
        result.ElapsedTime = stopwatch.Elapsed;
        result.AverageSpeedBytesPerSec = stopwatch.Elapsed.TotalSeconds > 0
            ? totalBytesWritten / stopwatch.Elapsed.TotalSeconds : 0;
        result.BlockCount = plan.Mode == TestMode.QuickSampling
            ? GetSampleCount(totalBlocks)
            : totalBlocks;

        return result;
    }

    private static int[] GetBlockIndices(TestPlan plan, int totalBlocks)
    {
        if (plan.Mode == TestMode.FullCapacity)
        {
            int[] indices = new int[totalBlocks];
            for (int i = 0; i < totalBlocks; i++)
                indices[i] = i;
            return indices;
        }

        // Quick Sampling: evenly-spaced blocks
        int sampleCount = GetSampleCount(totalBlocks);
        int[] samples = new int[sampleCount];
        double step = totalBlocks > 1 ? (double)(totalBlocks - 1) / (sampleCount - 1) : 0;

        for (int i = 0; i < sampleCount; i++)
        {
            samples[i] = (int)Math.Round(step * i);
        }

        return samples;
    }

    private static int GetSampleCount(int totalBlocks)
    {
        return Math.Max(10, Math.Min(totalBlocks, (int)Math.Sqrt(totalBlocks) * 4));
    }

    private static bool IsDiskFull(IOException ex)
    {
        const int ERROR_DISK_FULL = unchecked((int)0x80070070);
        const int ERROR_HANDLE_DISK_FULL = unchecked((int)0x80070027);
        return ex.HResult == ERROR_DISK_FULL || ex.HResult == ERROR_HANDLE_DISK_FULL;
    }

    private static long GetFileSizeLimit(string fileSystem)
    {
        return string.Equals(fileSystem, "FAT32", StringComparison.OrdinalIgnoreCase)
            ? Fat32FileSizeLimit
            : NonFat32FileSizeLimit;
    }
}
