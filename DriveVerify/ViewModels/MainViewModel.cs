using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DriveVerify.Helpers;
using DriveVerify.Models;
using DriveVerify.Services;

namespace DriveVerify.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly DispatcherTimer _elapsedTimer;
    private DateTime _testStartTime;
    private string? _lastReportPath;
    private string? _lastTestFolderPath;

    public MainViewModel()
    {
        RefreshDrivesCommand = new RelayCommand(RefreshDrives);
        StartTestCommand = new AsyncRelayCommand(RunTestAsync, () => SelectedDrive is not null && !IsRunning);
        CancelCommand = new RelayCommand(CancelTest, () => IsRunning);
        OpenReportCommand = new RelayCommand(OpenReport, () => _lastReportPath is not null);
        CleanUpCommand = new RelayCommand(CleanUpTestFiles, () => _lastTestFolderPath is not null && !IsRunning);

        StartTestCommand.IsRunningChanged += (_, _) =>
        {
            IsRunning = StartTestCommand.IsRunning;
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsNotRunning));
            OnPropertyChanged(nameof(WindowTitle));
        };

        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick += (_, _) =>
        {
            if (IsRunning)
            {
                ElapsedText = TimeFormatter.Format(DateTime.UtcNow - _testStartTime);
                OnPropertyChanged(nameof(WindowTitle));
            }
        };

        SelectedTestMode = TestMode.FullCapacity;
        BlockSizeMB = 4;
        VerifyThreadCount = Math.Min(Environment.ProcessorCount, 8);
        StatusMessage = "Ready";

        RefreshDrives();
    }

    // ── Drive Selection ──
    public ObservableCollection<DriveItem> Drives { get; } = [];

    private DriveItem? _selectedDrive;
    public DriveItem? SelectedDrive
    {
        get => _selectedDrive;
        set
        {
            if (_selectedDrive != value)
            {
                _selectedDrive = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DriveInfoText));
                OnPropertyChanged(nameof(DriveWarning));
                StartTestCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string DriveInfoText => SelectedDrive is { } d
        ? $"{d.FileSystem}  |  Total: {SizeFormatter.Format(d.TotalSize)}  |  Free: {SizeFormatter.Format(d.FreeSpace)}"
        : "No drive selected";

    public string DriveWarning => SelectedDrive is { IsRemovable: false }
        ? "⚠ This is a fixed (non-removable) drive. Use with caution."
        : string.Empty;

    // ── Test Settings ──
    private TestMode _selectedTestMode;
    public TestMode SelectedTestMode
    {
        get => _selectedTestMode;
        set { _selectedTestMode = value; OnPropertyChanged(); }
    }

    public TestMode[] TestModes { get; } = [TestMode.FullCapacity, TestMode.QuickSampling];

    private int _blockSizeMB;
    public int BlockSizeMB
    {
        get => _blockSizeMB;
        set { _blockSizeMB = value; OnPropertyChanged(); }
    }

    private double? _customTestSizeGB;
    public double? CustomTestSizeGB
    {
        get => _customTestSizeGB;
        set { _customTestSizeGB = value; OnPropertyChanged(); }
    }

    private int _verifyThreadCount;
    public int VerifyThreadCount
    {
        get => _verifyThreadCount;
        set { _verifyThreadCount = value; OnPropertyChanged(); }
    }

    // ── Progress ──
    private double _progressPercent;
    public double ProgressPercent
    {
        get => _progressPercent;
        set { _progressPercent = value; OnPropertyChanged(); }
    }

    private string _phaseText = string.Empty;
    public string PhaseText
    {
        get => _phaseText;
        set { _phaseText = value; OnPropertyChanged(); OnPropertyChanged(nameof(WindowTitle)); }
    }

    private string _speedText = string.Empty;
    public string SpeedText
    {
        get => _speedText;
        set { _speedText = value; OnPropertyChanged(); }
    }

    private string _elapsedText = string.Empty;
    public string ElapsedText
    {
        get => _elapsedText;
        set { _elapsedText = value; OnPropertyChanged(); }
    }

    private string _etaText = string.Empty;
    public string EtaText
    {
        get => _etaText;
        set { _etaText = value; OnPropertyChanged(); }
    }

    private string _blockProgressText = string.Empty;
    public string BlockProgressText
    {
        get => _blockProgressText;
        set { _blockProgressText = value; OnPropertyChanged(); }
    }

    // ── State ──
    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set { _isRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotRunning)); }
    }

    public bool IsNotRunning => !IsRunning;

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    // ── Verdict ──
    private string _verdictText = string.Empty;
    public string VerdictText
    {
        get => _verdictText;
        set { _verdictText = value; OnPropertyChanged(); }
    }

    private Brush _verdictBrush = Brushes.Transparent;
    public Brush VerdictBrush
    {
        get => _verdictBrush;
        set { _verdictBrush = value; OnPropertyChanged(); }
    }

    private Brush _verdictForeground = Brushes.White;
    public Brush VerdictForeground
    {
        get => _verdictForeground;
        set { _verdictForeground = value; OnPropertyChanged(); }
    }

    private Visibility _verdictVisibility = Visibility.Collapsed;
    public Visibility VerdictVisibility
    {
        get => _verdictVisibility;
        set { _verdictVisibility = value; OnPropertyChanged(); }
    }

    // ── Log ──
    public ObservableCollection<string> LogEntries { get; } = [];

    // ── Heatmap ──
    private RegionStatus[]? _regionStatuses;
    public RegionStatus[]? RegionStatuses
    {
        get => _regionStatuses;
        set { _regionStatuses = value; OnPropertyChanged(); }
    }

    // ── Last Result ──
    private TestResult? _lastResult;
    public TestResult? LastResult
    {
        get => _lastResult;
        set { _lastResult = value; OnPropertyChanged(); }
    }

    public string WindowTitle
    {
        get
        {
            if (IsRunning && !string.IsNullOrEmpty(PhaseText))
                return $"FlashDriveTester — {PhaseText} {ProgressPercent:F0}%";
            return "FlashDriveTester";
        }
    }

    public bool CanCleanUp => _lastTestFolderPath is not null && !IsRunning;

    // ── Commands ──
    public RelayCommand RefreshDrivesCommand { get; }
    public AsyncRelayCommand StartTestCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand OpenReportCommand { get; }
    public RelayCommand CleanUpCommand { get; }

    // ── Command Implementations ──

    private void RefreshDrives()
    {
        Drives.Clear();
        foreach (var drive in DriveDetectionService.GetDrives(includeFixed: true))
            Drives.Add(drive);

        StatusMessage = $"Found {Drives.Count} drive(s)";
        Log("Drives refreshed");
    }

    private async Task RunTestAsync(CancellationToken ct)
    {
        if (SelectedDrive is null) return;

        // Confirmation
        var result = MessageBox.Show(
            $"This will write test data to {SelectedDrive.DriveLetter}.\n" +
            "Existing data in the test folder will be overwritten.\n\n" +
            "Continue?",
            "FlashDriveTester",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        // Validate free space
        if (SelectedDrive.FreeSpace < BlockSizeMB * 1024L * 1024)
        {
            MessageBox.Show(
                "Insufficient free space on the selected drive.\n" +
                $"Free space: {SizeFormatter.Format(SelectedDrive.FreeSpace)}\n" +
                $"Minimum required: {BlockSizeMB} MB",
                "FlashDriveTester",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        // Reset state
        LogEntries.Clear();
        VerdictVisibility = Visibility.Collapsed;
        VerdictText = string.Empty;
        ProgressPercent = 0;
        _lastReportPath = null;
        _lastTestFolderPath = null;
        OpenReportCommand.RaiseCanExecuteChanged();
        CleanUpCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanCleanUp));

        // Build TestPlan
        long testSizeBytes = CustomTestSizeGB.HasValue
            ? (long)(CustomTestSizeGB.Value * 1024 * 1024 * 1024)
            : SelectedDrive.FreeSpace;

        string testFolderPath = Path.Combine(SelectedDrive.DriveLetter + "\\", "FlashDriveTester_Test");

        var plan = new TestPlan
        {
            SessionId = Guid.NewGuid(),
            TargetDrive = SelectedDrive,
            Mode = SelectedTestMode,
            TestSizeBytes = testSizeBytes,
            BlockSizeBytes = BlockSizeMB * 1024 * 1024,
            VerifyThreadCount = VerifyThreadCount,
            TestFolderPath = testFolderPath
        };

        _lastTestFolderPath = testFolderPath;

        int totalBlocks = plan.ComputeTotalBlocks();
        RegionStatuses = new RegionStatus[totalBlocks];
        Array.Fill(RegionStatuses, RegionStatus.Untested);
        OnPropertyChanged(nameof(RegionStatuses));

        _testStartTime = DateTime.UtcNow;
        _elapsedTimer.Start();

        Log($"Test started: {SelectedDrive.DisplayName}");
        Log($"Mode: {plan.Mode}, Block size: {SizeFormatter.Format(plan.BlockSizeBytes)}, Test size: {SizeFormatter.Format(plan.TestSizeBytes)}");
        Log($"Total blocks: {totalBlocks}, Session: {plan.SessionId}");

        WritePhaseResult? writeResult = null;
        VerifyPhaseResult? verifyResult = null;
        Verdict verdict = Verdict.Cancelled;

        try
        {
            // ── Write Phase ──
            PhaseText = "Writing";
            StatusMessage = "Writing test data...";
            Log("=== Write Phase Started ===");

            var writeProgress = new Progress<WriteProgress>(p =>
            {
                double pct = p.TotalBytes > 0 ? (double)p.BytesWritten / p.TotalBytes * 100 : 0;
                ProgressPercent = pct;
                SpeedText = $"{SizeFormatter.Format((long)p.SpeedBytesPerSec)}/s";
                ElapsedText = TimeFormatter.Format(p.Elapsed);
                EtaText = TimeFormatter.Format(p.EstimatedRemaining);
                BlockProgressText = $"Block {p.BlockIndex + 1}/{totalBlocks}";

                if (p.RegionIndex >= 0 && p.RegionIndex < RegionStatuses!.Length)
                {
                    RegionStatuses[p.RegionIndex] = RegionStatus.Good;
                    // Force WPF to detect change by creating new array reference
                    RegionStatuses = (RegionStatus[])RegionStatuses.Clone();
                }

                if (p.BlockIndex % 100 == 0)
                    Log($"Writing block {p.BlockIndex + 1}/{totalBlocks} — {SizeFormatter.Format(p.BytesWritten)} — {SizeFormatter.Format((long)p.SpeedBytesPerSec)}/s");
            });

            var writer = new FileTestWriterService();
            writeResult = await writer.WriteAsync(plan, writeProgress, ct);

            Log($"=== Write Phase Complete: {SizeFormatter.Format(writeResult.TotalBytesWritten)} in {TimeFormatter.Format(writeResult.ElapsedTime)} ({SizeFormatter.Format((long)writeResult.AverageSpeedBytesPerSec)}/s) ===");

            if (writeResult.Exceptions.Count > 0)
            {
                foreach (var ex in writeResult.Exceptions)
                    Log($"Write error: {ex.Message}");
            }

            ct.ThrowIfCancellationRequested();

            // ── Verify Phase ──
            PhaseText = "Verifying";
            StatusMessage = "Verifying test data...";
            ProgressPercent = 0;
            Log("=== Verify Phase Started ===");

            var verifyProgress = new Progress<VerifyProgress>(p =>
            {
                double pct = p.TotalBytes > 0 ? (double)p.BytesVerified / p.TotalBytes * 100 : 0;
                ProgressPercent = pct;
                SpeedText = $"{SizeFormatter.Format((long)p.SpeedBytesPerSec)}/s";
                ElapsedText = TimeFormatter.Format(p.Elapsed);
                EtaText = TimeFormatter.Format(p.EstimatedRemaining);
                BlockProgressText = $"Block {p.BlockIndex + 1}/{totalBlocks} ({p.IssueCount} issues)";

                if (p.RegionIndex >= 0 && p.RegionIndex < RegionStatuses!.Length)
                {
                    if (p.IsVerifying)
                    {
                        // Set to orange "Verifying" status
                        RegionStatuses[p.RegionIndex] = RegionStatus.Verifying;
                    }
                    else
                    {
                        // Set final status after verification complete
                        RegionStatuses[p.RegionIndex] = p.RegionFailed ? RegionStatus.Bad : RegionStatus.Good;
                    }
                    // Force WPF to detect change by creating new array reference
                    RegionStatuses = (RegionStatus[])RegionStatuses.Clone();
                }

                if (p.BlockIndex % 100 == 0)
                    Log($"Verifying block {p.BlockIndex + 1}/{totalBlocks} — {SizeFormatter.Format(p.BytesVerified)} — {p.IssueCount} issues");
            });

            var verifier = new FileTestVerifierService();
            verifyResult = await verifier.VerifyAsync(plan, verifyProgress, ct);

            Log($"=== Verify Phase Complete: {SizeFormatter.Format(verifyResult.TotalBytesVerified)} in {TimeFormatter.Format(verifyResult.ElapsedTime)} ({SizeFormatter.Format((long)verifyResult.AverageSpeedBytesPerSec)}/s) ===");

            RegionStatuses = verifyResult.RegionMap;
            OnPropertyChanged(nameof(RegionStatuses));

            // Determine verdict
            verdict = DetermineVerdict(verifyResult);
        }
        catch (OperationCanceledException)
        {
            verdict = Verdict.Cancelled;
            Log("Test cancelled by user.");
        }
        catch (UnauthorizedAccessException ex)
        {
            verdict = Verdict.Cancelled;
            Log($"Access denied: {ex.Message}");
            MessageBox.Show(
                $"Access denied:\n{ex.Message}\n\nTry running as administrator or check write protection.",
                "FlashDriveTester",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (IOException ex)
        {
            verdict = Verdict.Cancelled;
            Log($"IO Error: {ex.Message}");
            MessageBox.Show(
                $"An IO error occurred:\n{ex.Message}\n\nThe drive may have been removed.",
                "FlashDriveTester",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _elapsedTimer.Stop();
        }

        // Build TestResult
        var testResult = BuildTestResult(plan, writeResult, verifyResult, verdict);
        LastResult = testResult;

        // Show verdict
        SetVerdict(verdict);
        ProgressPercent = 100;
        PhaseText = verdict.ToString();
        StatusMessage = $"Test complete — {verdict}";

        Log($"Verdict: {verdict}");
        if (verifyResult?.Issues.Count > 0)
        {
            Log($"Total issues: {verifyResult.Issues.Count}");
            if (verifyResult.FirstFailureOffset.HasValue)
                Log($"First failure at offset: {SizeFormatter.Format(verifyResult.FirstFailureOffset.Value)}");
        }

        // Generate report
        try
        {
            var reportModel = BuildReportModel(plan, testResult);
            string html = HtmlReportService.GenerateReport(reportModel);
            _lastReportPath = await HtmlReportService.SaveAndOpenAsync(html, plan.TestFolderPath);
            OpenReportCommand.RaiseCanExecuteChanged();
            Log($"Report saved: {_lastReportPath}");

            // Also save log file
            await SaveLogFileAsync(plan.TestFolderPath);
        }
        catch (Exception ex)
        {
            Log($"Failed to generate report: {ex.Message}");
        }

        // Update cleanup button state
        CleanUpCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanCleanUp));
    }

    private void CancelTest()
    {
        StartTestCommand.Cancel();
        Log("Cancellation requested...");
    }

    private void OpenReport()
    {
        if (_lastReportPath is not null)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_lastReportPath)
                {
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    private void CleanUpTestFiles()
    {
        if (_lastTestFolderPath is null) return;

        var result = MessageBox.Show(
            $"This will permanently delete all test files in:\n{_lastTestFolderPath}\n\n" +
            "The HTML report and log file will also be deleted.\n\n" +
            "Continue?",
            "Clean Up Test Files",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            if (Directory.Exists(_lastTestFolderPath))
            {
                Directory.Delete(_lastTestFolderPath, recursive: true);
                Log($"Test files deleted: {_lastTestFolderPath}");
                StatusMessage = "Test files cleaned up";
                MessageBox.Show(
                    "Test files have been successfully deleted.",
                    "Clean Up Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Reset state
                _lastTestFolderPath = null;
                _lastReportPath = null;
                CleanUpCommand.RaiseCanExecuteChanged();
                OpenReportCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CanCleanUp));
            }
            else
            {
                Log($"Test folder not found: {_lastTestFolderPath}");
                MessageBox.Show(
                    "Test folder not found. It may have already been deleted.",
                    "Clean Up",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                _lastTestFolderPath = null;
                CleanUpCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CanCleanUp));
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            Log($"Access denied during cleanup: {ex.Message}");
            MessageBox.Show(
                $"Access denied:\n{ex.Message}\n\nTry running as administrator.",
                "Clean Up Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (IOException ex)
        {
            Log($"IO error during cleanup: {ex.Message}");
            MessageBox.Show(
                $"An error occurred:\n{ex.Message}\n\nThe drive may be in use or disconnected.",
                "Clean Up Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            Log($"Unexpected error during cleanup: {ex.Message}");
            MessageBox.Show(
                $"An unexpected error occurred:\n{ex.Message}",
                "Clean Up Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // ── Helpers ──

    private void Log(string message)
    {
        string entry = $"{DateTime.UtcNow:HH:mm:ss}  {message}";
        if (Application.Current?.Dispatcher.CheckAccess() == true)
            LogEntries.Add(entry);
        else
            Application.Current?.Dispatcher.BeginInvoke(() => LogEntries.Add(entry));
    }

    private static Verdict DetermineVerdict(VerifyPhaseResult verifyResult)
    {
        if (verifyResult.Issues.Count == 0)
            return Verdict.Verified;

        bool hasFakeIndicators = verifyResult.Issues.Any(i => i.IssueKind == IssueKind.DuplicateWrap);
        if (hasFakeIndicators)
            return Verdict.FakeCapacityDetected;

        bool hasCorruption = verifyResult.Issues.Any(i =>
            i.IssueKind == IssueKind.ChecksumMismatch || i.IssueKind == IssueKind.HeaderMismatch);
        if (hasCorruption)
        {
            double failureRate = (double)verifyResult.Issues.Count /
                (verifyResult.RegionMap.Length > 0 ? verifyResult.RegionMap.Length : 1);
            return failureRate > 0.1 ? Verdict.FakeCapacityDetected : Verdict.CorruptionDetected;
        }

        return Verdict.Suspect;
    }

    private void SetVerdict(Verdict verdict)
    {
        VerdictVisibility = Visibility.Visible;
        switch (verdict)
        {
            case Verdict.Verified:
                VerdictText = "✓ VERIFIED";
                VerdictBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
                VerdictForeground = Brushes.White;
                break;
            case Verdict.Suspect:
                VerdictText = "⚠ SUSPECT";
                VerdictBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
                VerdictForeground = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
                break;
            case Verdict.FakeCapacityDetected:
                VerdictText = "✗ FAKE CAPACITY DETECTED";
                VerdictBrush = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
                VerdictForeground = Brushes.White;
                break;
            case Verdict.CorruptionDetected:
                VerdictText = "✗ CORRUPTION DETECTED";
                VerdictBrush = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
                VerdictForeground = Brushes.White;
                break;
            case Verdict.Cancelled:
                VerdictText = "— CANCELLED";
                VerdictBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
                VerdictForeground = Brushes.White;
                break;
        }
    }

    private static TestResult BuildTestResult(TestPlan plan, WritePhaseResult? write, VerifyPhaseResult? verify, Verdict verdict)
    {
        return new TestResult
        {
            SessionId = plan.SessionId,
            DriveLetter = plan.TargetDrive.DriveLetter,
            VolumeLabel = plan.TargetDrive.VolumeLabel,
            FileSystem = plan.TargetDrive.FileSystem,
            TotalSize = plan.TargetDrive.TotalSize,
            FreeSpace = plan.TargetDrive.FreeSpace,
            IsRemovable = plan.TargetDrive.IsRemovable,
            Mode = plan.Mode,
            ConfiguredTestSize = plan.TestSizeBytes,
            ActualBytesWritten = write?.TotalBytesWritten ?? 0,
            ActualBytesVerified = verify?.TotalBytesVerified ?? 0,
            WriteSpeedBytesPerSec = write?.AverageSpeedBytesPerSec ?? 0,
            ReadSpeedBytesPerSec = verify?.AverageSpeedBytesPerSec ?? 0,
            Duration = (write?.ElapsedTime ?? TimeSpan.Zero) + (verify?.ElapsedTime ?? TimeSpan.Zero),
            FirstFailureOffset = verify?.FirstFailureOffset,
            VerifiedGoodBytes = ComputeVerifiedGoodBytes(verify, plan.BlockSizeBytes),
            Issues = verify?.Issues ?? [],
            RegionMap = verify?.RegionMap ?? [],
            Verdict = verdict
        };
    }

    private static long ComputeVerifiedGoodBytes(VerifyPhaseResult? verify, int blockSizeBytes)
    {
        if (verify is null) return 0;
        int goodCount = verify.RegionMap.Count(r => r == RegionStatus.Good);
        return (long)goodCount * blockSizeBytes;
    }

    private static HtmlReportModel BuildReportModel(TestPlan plan, TestResult testResult)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return new HtmlReportModel
        {
            AppVersion = version?.ToString() ?? "1.0.0",
            ReportDateUtc = DateTime.UtcNow,
            DriveDisplayName = plan.TargetDrive.DisplayName,
            SessionId = testResult.SessionId,
            DriveLetter = testResult.DriveLetter,
            VolumeLabel = testResult.VolumeLabel,
            FileSystem = testResult.FileSystem,
            TotalSize = testResult.TotalSize,
            FreeSpace = testResult.FreeSpace,
            IsRemovable = testResult.IsRemovable,
            Mode = testResult.Mode,
            ConfiguredTestSize = testResult.ConfiguredTestSize,
            BlockSizeBytes = plan.BlockSizeBytes,
            VerifyThreadCount = plan.VerifyThreadCount,
            ActualBytesWritten = testResult.ActualBytesWritten,
            ActualBytesVerified = testResult.ActualBytesVerified,
            WriteSpeedBytesPerSec = testResult.WriteSpeedBytesPerSec,
            ReadSpeedBytesPerSec = testResult.ReadSpeedBytesPerSec,
            Duration = testResult.Duration,
            Verdict = testResult.Verdict,
            FirstFailureOffset = testResult.FirstFailureOffset,
            VerifiedGoodBytes = testResult.VerifiedGoodBytes,
            Issues = testResult.Issues,
            HeatmapSvgFragment = HeatmapService.GenerateSvg(testResult.RegionMap)
        };
    }

    private async Task SaveLogFileAsync(string folderPath)
    {
        try
        {
            string logPath = Path.Combine(folderPath, "FlashDriveTester_Log.txt");
            var lines = LogEntries.ToArray();
            await File.WriteAllLinesAsync(logPath, lines);
        }
        catch { }
    }

    // ── INotifyPropertyChanged ──

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
