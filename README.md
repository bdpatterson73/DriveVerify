# DriveVerify

A powerful **drive integrity verification tool** for Windows that detects fake/counterfeit storage devices, verifies data integrity, and identifies drive corruption through comprehensive read-write testing.

![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![WPF](https://img.shields.io/badge/WPF-Windows-0078D4)
![License](https://img.shields.io/badge/license-MIT-green)

## 🎯 What is DriveVerify?

DriveVerify is a **storage verification utility** designed to combat the growing problem of counterfeit storage devices (USB drives, SD cards, etc.) and detect drive corruption. It writes test data to your drive, then verifies that data to ensure your storage device is authentic and functioning correctly.

### Key Problems It Solves

- **Fake Capacity Detection**: Identifies drives that report false capacity (e.g., a 32GB drive masquerading as 256GB)
- **Data Corruption Detection**: Finds drives that silently corrupt data during storage
- **Drive Health Verification**: Validates that data written to a drive can be reliably read back
- **Bad Sector Mapping**: Visual heatmap showing exactly which regions of your drive are unreliable

## ✨ Features

### 🔍 Comprehensive Testing
- **Full Capacity Mode**: Tests the entire available space on the drive
- **Quick Sampling Mode**: Rapid testing of representative data regions for faster verification
- **Configurable Block Sizes**: Adjust block size (1-128 MB) to optimize for different drive types

### 📊 Real-Time Visualization
- **Live Heatmap**: Color-coded visualization showing tested regions (green=good, red=failed)
- **Progress Tracking**: Real-time speed, ETA, and progress metrics
- **Detailed Statistics**: Write/read speeds, bytes verified, and issue counts

### 📝 Detailed Reporting
- **HTML Reports**: Beautiful, shareable HTML reports with full test results
- **Verdict System**: Clear pass/fail verdicts including:
  - ✅ **Verified**: Drive passed all tests
  - ⚠️ **Suspect**: Some issues detected
  - 🚨 **Fake Capacity Detected**: Drive is reporting false capacity
  - 💥 **Corruption Detected**: Severe data corruption found

### 🛡️ Safety Features
- **Fixed Drive Warning**: Alerts when testing non-removable drives
- **Cancellation Support**: Cancel tests at any time
- **Test File Cleanup**: Easy cleanup of test data after completion

## 🚀 How It Works

DriveVerify uses a sophisticated multi-phase testing approach:

### 1. **Write Phase**
- Generates unique, verifiable test patterns using a session-specific seed
- Writes test data in blocks across the drive (distributed or sequential based on test mode)
- Each block includes:
  - **Session ID**: Unique identifier for this test run
  - **Block metadata**: Index, offset, timestamp
  - **Checksum**: CRC32 validation of payload data
  - **Test pattern**: Deterministic pseudo-random data

### 2. **Verification Phase**
- Reads back the test data from disk
- Multi-threaded verification for optimal performance
- Validates each block by:
  - Checking the session ID matches
  - Regenerating the expected test pattern
  - Comparing checksums
  - Verifying data integrity byte-by-byte

### 3. **Analysis & Reporting**
- Analyzes verification results to determine drive verdict
- Generates a visual heatmap showing good/bad regions
- Creates detailed HTML report with:
  - Test parameters and drive information
  - Performance metrics (read/write speeds)
  - List of all verification issues with offsets
  - Visual region map

## 🎨 Architecture

DriveVerify follows **MVVM architecture** for clean separation of concerns:

```
DriveVerify/
├── Models/           # Data models (TestResult, DriveItem, TestPlan, etc.)
├── ViewModels/       # View logic and state (MainViewModel)
├── Views/            # UI components (HeatmapControl)
├── Services/         # Core business logic
│   ├── FileTestWriterService    # Write phase implementation
│   ├── FileTestVerifierService  # Verification phase implementation
│   ├── TestPatternService       # Deterministic pattern generation
│   ├── ChecksumService          # CRC32 checksum computation
│   ├── HtmlReportService        # Report generation
│   └── DriveDetectionService    # Drive enumeration
└── Helpers/          # Utilities (formatters, commands, etc.)
```

### Key Technical Details

- **File System Support**: Handles FAT32 (250MB file limit) and NTFS/exFAT (1GB file limit)
- **Multi-threaded Verification**: Uses configurable thread count (1-16 threads)
- **Memory Efficient**: Streams data in blocks to handle large drives
- **Async/Await**: Fully asynchronous I/O operations
- **Checksum Validation**: CRC32 checksums ensure data integrity

## 📋 Requirements

- **Platform**: Windows 10/11
- **.NET**: .NET 10 Runtime
- **UI Framework**: WPF (Windows Presentation Foundation)

## 🔧 Getting Started

### Installation

1. Clone the repository:
   ```bash
   git clone https://github.com/bdpatterson73/DriveVerify.git
   ```

2. Build the project:
   ```bash
   cd DriveVerify
   dotnet build
   ```

3. Run the application:
   ```bash
   dotnet run --project DriveVerify
   ```

### Usage

1. **Select a Drive**: Choose the drive you want to test from the dropdown
2. **Configure Test Settings**:
   - Select test mode (Full Capacity or Quick Sampling)
   - Adjust block size (recommended: 4-8 MB for USB drives)
   - Set verification thread count
3. **Start Test**: Click "Start Test" and wait for completion
4. **Review Results**: Check the verdict and review the HTML report
5. **Clean Up**: Use "Clean Up Test Files" to remove test data

> ⚠️ **Warning**: Testing will write data to the selected drive. Ensure you have a backup of any important data first!

## 📊 Understanding Test Results

### Verdicts

| Verdict | Meaning |
|---------|---------|
| ✅ **Verified** | All data verified successfully - drive is good |
| ⚠️ **Suspect** | Some issues detected - drive may be failing |
| 🚨 **Fake Capacity** | Drive reports more space than actually available |
| 💥 **Corruption** | Severe data corruption detected - replace drive |

### Heatmap Colors

- 🟩 **Green**: Region verified successfully
- 🟥 **Red**: Region failed verification
- ⬜ **Gray**: Region not yet tested/verified

## 🤝 Contributing

Contributions are welcome! Please feel free to submit pull requests, report bugs, or suggest features.

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## 📝 License

This project is licensed under the MIT License - see the LICENSE file for details.

## 🙏 Acknowledgments

- Inspired by **h2testw**, the classic fake flash detection tool, but with a modern WPF interface
- Built from the ground up with **.NET 10** to provide a contemporary user experience
- Also inspired by F3 and other storage verification utilities
- Designed to help consumers identify counterfeit storage devices with an intuitive, visual interface

## 📧 Contact

**Project Link**: [https://github.com/bdpatterson73/DriveVerify](https://github.com/bdpatterson73/DriveVerify)

---

**⭐ If you find DriveVerify useful, please consider starring the repository!**
