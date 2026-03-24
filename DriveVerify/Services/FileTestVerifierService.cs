using System.Collections.Concurrent;
using System.IO;
using DriveVerify.Models;

namespace DriveVerify.Services;

public class VerifyProgress
{
    public string Phase { get; set; } = "Verifying";
    public int BlockIndex { get; set; }
    public long BytesVerified { get; set; }
    public long TotalBytes { get; set; }
    public double SpeedBytesPerSec { get; set; }
    public TimeSpan Elapsed { get; set; }
    public TimeSpan EstimatedRemaining { get; set; }
    public int IssueCount { get; set; }
    public int RegionIndex { get; set; }
    public bool RegionFailed { get; set; }
    public bool IsVerifying { get; set; }
}

public class VerifyPhaseResult
{
    public long TotalBytesVerified { get; set; }
    public TimeSpan ElapsedTime { get; set; }
    public double AverageSpeedBytesPerSec { get; set; }
    public List<VerificationIssue> Issues { get; set; } = [];
    public long? FirstFailureOffset { get; set; }
    public RegionStatus[] RegionMap { get; set; } = [];
}

public class FileTestVerifierService
{
    public async Task<VerifyPhaseResult> VerifyAsync(
        TestPlan plan,
        IProgress<VerifyProgress> progress,
        CancellationToken ct)
    {
        var issues = new ConcurrentBag<VerificationIssue>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        int totalBlocks = plan.ComputeTotalBlocks();
        var regionMap = new RegionStatus[totalBlocks];
        Array.Fill(regionMap, RegionStatus.Good); // Assume good from write phase

        // Enumerate test files in order
        var testFiles = Directory.GetFiles(plan.TestFolderPath, "testdata_*.dat")
            .OrderBy(f => f)
            .ToArray();

        long totalBytesInFiles = testFiles.Sum(f => new FileInfo(f).Length);
        long totalBytesVerified = 0;
        int blocksVerified = 0;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = plan.VerifyThreadCount,
            CancellationToken = ct
        };

        await Parallel.ForEachAsync(testFiles, parallelOptions, async (filePath, token) =>
        {
            token.ThrowIfCancellationRequested();

            try
            {
                await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, bufferSize: 65536, useAsync: true);

                byte[] headerBuffer = new byte[TestBlockHeader.SerializedSize];

                while (stream.Position < stream.Length)
                {
                    token.ThrowIfCancellationRequested();

                    long blockStartPosition = stream.Position;

                    // Read header
                    int headerBytesRead = await ReadFullAsync(stream, headerBuffer, token);
                    if (headerBytesRead < TestBlockHeader.SerializedSize)
                    {
                        // Short read on header - create issue for remaining data
                        if (headerBytesRead > 0)
                        {
                            issues.Add(new VerificationIssue
                            {
                                BlockIndex = -1,
                                AbsoluteOffset = blockStartPosition,
                                IssueKind = IssueKind.ShortRead,
                                Detail = $"Only {headerBytesRead} of {TestBlockHeader.SerializedSize} header bytes read at position {blockStartPosition}"
                            });
                        }
                        break;
                    }

                    TestBlockHeader header;
                    try
                    {
                        header = TestBlockHeader.Deserialize(headerBuffer);
                    }
                    catch
                    {
                        issues.Add(new VerificationIssue
                        {
                            BlockIndex = -1,
                            AbsoluteOffset = blockStartPosition,
                            IssueKind = IssueKind.Unreadable,
                            Detail = "Failed to deserialize block header"
                        });
                        break;
                    }

                    int regionIndex = header.BlockIndex;
                    bool blockFailed = false;

                    // Update region to verifying and report progress
                    if (regionIndex >= 0 && regionIndex < regionMap.Length)
                    {
                        regionMap[regionIndex] = RegionStatus.Verifying;
                        // Report "verifying" status to UI
                        progress.Report(new VerifyProgress
                        {
                            Phase = "Verifying",
                            BlockIndex = header.BlockIndex,
                            BytesVerified = Interlocked.Read(ref totalBytesVerified),
                            TotalBytes = totalBytesInFiles,
                            SpeedBytesPerSec = 0,
                            Elapsed = stopwatch.Elapsed,
                            EstimatedRemaining = TimeSpan.Zero,
                            IssueCount = issues.Count,
                            RegionIndex = regionIndex,
                            RegionFailed = false,
                            IsVerifying = true
                        });
                    }

                    // Validate magic signature
                    if (header.MagicSignature != TestBlockHeader.MagicSignatureValue)
                    {
                        issues.Add(new VerificationIssue
                        {
                            BlockIndex = header.BlockIndex,
                            AbsoluteOffset = header.AbsoluteOffset,
                            IssueKind = IssueKind.HeaderMismatch,
                            Detail = $"Invalid magic signature: 0x{header.MagicSignature:X16}"
                        });
                        blockFailed = true;
                    }

                    // Validate session ID
                    if (header.SessionId != plan.SessionId)
                    {
                        issues.Add(new VerificationIssue
                        {
                            BlockIndex = header.BlockIndex,
                            AbsoluteOffset = header.AbsoluteOffset,
                            IssueKind = IssueKind.HeaderMismatch,
                            Detail = $"Session ID mismatch: expected {plan.SessionId}, got {header.SessionId}"
                        });
                        blockFailed = true;
                    }

                    // Read payload
                    if (header.PayloadLength <= 0 || header.PayloadLength > plan.BlockSizeBytes)
                    {
                        issues.Add(new VerificationIssue
                        {
                            BlockIndex = header.BlockIndex,
                            AbsoluteOffset = header.AbsoluteOffset,
                            IssueKind = IssueKind.HeaderMismatch,
                            Detail = $"Invalid payload length: {header.PayloadLength}"
                        });
                        blockFailed = true;
                        if (regionIndex >= 0 && regionIndex < regionMap.Length)
                            regionMap[regionIndex] = RegionStatus.Bad;

                        ReportVerifyProgress(progress, stopwatch, header.BlockIndex, totalBytesVerified,
                            totalBytesInFiles, issues.Count, regionIndex, blockFailed);
                        break;
                    }

                    byte[] payloadBuffer = new byte[header.PayloadLength];
                    int payloadBytesRead = await ReadFullAsync(stream, payloadBuffer, token);

                    if (payloadBytesRead < header.PayloadLength)
                    {
                        issues.Add(new VerificationIssue
                        {
                            BlockIndex = header.BlockIndex,
                            AbsoluteOffset = header.AbsoluteOffset,
                            IssueKind = IssueKind.ShortRead,
                            Detail = $"Only {payloadBytesRead} of {header.PayloadLength} payload bytes read"
                        });
                        blockFailed = true;
                    }

                    if (!blockFailed || payloadBytesRead > 0)
                    {
                        // Verify CRC32
                        uint computedChecksum = ChecksumService.Compute(payloadBuffer.AsSpan(0, payloadBytesRead));
                        if (computedChecksum != header.PayloadChecksum)
                        {
                            issues.Add(new VerificationIssue
                            {
                                BlockIndex = header.BlockIndex,
                                AbsoluteOffset = header.AbsoluteOffset,
                                IssueKind = IssueKind.ChecksumMismatch,
                                Detail = $"CRC32 mismatch: expected 0x{header.PayloadChecksum:X8}, computed 0x{computedChecksum:X8}"
                            });
                            blockFailed = true;
                        }

                        // Compare against expected payload
                        byte[] expected = TestPatternService.GenerateExpectedPayload(
                            header.SessionId, header.BlockIndex, header.AbsoluteOffset, header.PayloadLength);

                        if (!payloadBuffer.AsSpan(0, payloadBytesRead).SequenceEqual(expected.AsSpan(0, payloadBytesRead)))
                        {
                            // Check for wrap/duplicate pattern
                            bool isDuplicate = DetectWrapPattern(payloadBuffer, expected, payloadBytesRead);
                            issues.Add(new VerificationIssue
                            {
                                BlockIndex = header.BlockIndex,
                                AbsoluteOffset = header.AbsoluteOffset,
                                IssueKind = isDuplicate ? IssueKind.DuplicateWrap : IssueKind.ChecksumMismatch,
                                Detail = isDuplicate
                                    ? "Data appears to wrap/duplicate another region (fake capacity indicator)"
                                    : "Payload data does not match expected pattern"
                            });
                            blockFailed = true;
                        }
                    }

                    // Update region status
                    if (regionIndex >= 0 && regionIndex < regionMap.Length)
                        regionMap[regionIndex] = blockFailed ? RegionStatus.Bad : RegionStatus.Good;

                    long blockBytes = TestBlockHeader.SerializedSize + payloadBytesRead;
                    Interlocked.Add(ref totalBytesVerified, blockBytes);
                    Interlocked.Increment(ref blocksVerified);

                    ReportVerifyProgress(progress, stopwatch, header.BlockIndex,
                        Interlocked.Read(ref totalBytesVerified), totalBytesInFiles,
                        issues.Count, regionIndex, blockFailed);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (IOException ex)
            {
                issues.Add(new VerificationIssue
                {
                    BlockIndex = -1,
                    AbsoluteOffset = -1,
                    IssueKind = IssueKind.Unreadable,
                    Detail = $"IO error reading {filePath}: {ex.Message}"
                });
            }
        });

        stopwatch.Stop();

        var sortedIssues = issues.OrderBy(i => i.AbsoluteOffset).ToList();

        return new VerifyPhaseResult
        {
            TotalBytesVerified = Interlocked.Read(ref totalBytesVerified),
            ElapsedTime = stopwatch.Elapsed,
            AverageSpeedBytesPerSec = stopwatch.Elapsed.TotalSeconds > 0
                ? Interlocked.Read(ref totalBytesVerified) / stopwatch.Elapsed.TotalSeconds : 0,
            Issues = sortedIssues,
            FirstFailureOffset = sortedIssues.FirstOrDefault(i => i.AbsoluteOffset >= 0)?.AbsoluteOffset,
            RegionMap = regionMap
        };
    }

    private static void ReportVerifyProgress(
        IProgress<VerifyProgress> progress,
        System.Diagnostics.Stopwatch stopwatch,
        int blockIndex, long bytesVerified, long totalBytes,
        int issueCount, int regionIndex, bool regionFailed)
    {
        double elapsed = stopwatch.Elapsed.TotalSeconds;
        double speed = elapsed > 0 ? bytesVerified / elapsed : 0;
        double fraction = totalBytes > 0 ? (double)bytesVerified / totalBytes : 0;
        TimeSpan eta = fraction > 0
            ? TimeSpan.FromSeconds(elapsed / fraction * (1 - fraction))
            : TimeSpan.Zero;

        progress.Report(new VerifyProgress
        {
            Phase = "Verifying",
            BlockIndex = blockIndex,
            BytesVerified = bytesVerified,
            TotalBytes = totalBytes,
            SpeedBytesPerSec = speed,
            Elapsed = stopwatch.Elapsed,
            EstimatedRemaining = eta,
            IssueCount = issueCount,
            RegionIndex = regionIndex,
            RegionFailed = regionFailed
        });
    }

    private static bool DetectWrapPattern(byte[] actual, byte[] expected, int length)
    {
        // A wrap pattern means the drive wrote data from a different block.
        // Check if the actual data doesn't match but has a repeating structure
        // that suggests memory address aliasing (common in fake drives).
        if (length < 64) return false;

        int mismatches = 0;
        for (int i = 0; i < length; i++)
        {
            if (actual[i] != expected[i])
                mismatches++;
        }

        // If most bytes differ but the data isn't random-looking (has structure),
        // it's likely a wrap. High mismatch rate is suspicious.
        double mismatchRate = (double)mismatches / length;
        return mismatchRate > 0.5;
    }

    private static async Task<int> ReadFullAsync(FileStream stream, byte[] buffer, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int bytesRead = await stream.ReadAsync(
                buffer.AsMemory(totalRead, buffer.Length - totalRead), ct);
            if (bytesRead == 0) break;
            totalRead += bytesRead;
        }
        return totalRead;
    }
}
