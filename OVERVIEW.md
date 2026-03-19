# SwiftGrab v3.0 — Comprehensive Project Overview

## 1. What Is SwiftGrab?

**SwiftGrab** is a full-featured, open-source download manager for Windows, built with **.NET 8** (WPF) and a **Rust native download engine** (`swiftgrab_engine`). It targets power users who need reliable, blazing-fast, and intelligent file downloads — combining a Rust-powered HTTP/2 segmented downloader with memory-mapped I/O, video extraction (via yt-dlp), BitTorrent protocol support (via MonoTorrent), and browser integration into a single desktop application.

**Version:** 3.0.0  
**Author:** ABDULLAH IMRAN  
**License:** Proprietary (Copyright © 2025 ABDULLAH IMRAN)  
**Target Platform:** Windows 10+ (x64), with MSIX packaging and Inno Setup installer support.

---

## 2. Architecture Overview

SwiftGrab follows an **MVVM (Model-View-ViewModel)** architecture with a service layer, using `Microsoft.Extensions.DependencyInjection` for DI wiring. The high-performance download pipeline lives in a native Rust library (`swiftgrab_engine.dll`) accessed via C# P/Invoke through a managed bridge layer.

```
┌─────────────────────────────────────────────────────────────────┐
│                         Views (WPF / XAML)                      │
│  MainWindow · SettingsWindow · AddUrlDialog                     │
│  VideoResolutionDialog · BatchDownloadDialog                    │
│  ActiveDownloadWindow · DownloadFileInfoDialog                  │
│  AboutDeveloperDialog · BrowserExtensionDialog                  │
├─────────────────────────────────────────────────────────────────┤
│                          ViewModels                             │
│  MainViewModel · DownloadViewModel                              │
│  DownloadItemViewModel · BaseViewModel                          │
├─────────────────────────────────────────────────────────────────┤
│                       C# Services Layer                         │
│  DownloadManager · DownloadService                              │
│  SegmentedDownloadService · DownloadCoordinator                 │
│  SegmentDownloader · DynamicRebalancer                          │
│  WorkStealingScheduler · SegmentPerformanceMonitor              │
│  YtDlpService · VideoDownloadService · FFmpegHelper             │
│  TorrentEngine · TorrentDownloadService                         │
│  BrowserIntegrationService · NativeMessagingHost                │
│  ProtocolHandler · SettingsService · ThemeService                │
│  ToolsManager · UpdateService · AppDiagnostics                  │
│  HttpCapabilityService · ProcessRunner · ResumeStore            │
├─────────────────────────────────────────────────────────────────┤
│                Rust Interop Bridge (P/Invoke)                   │
│  RustDownloadBridge · NativeMethods                             │
├─────────────────────────────────────────────────────────────────┤
│              swiftgrab_engine (Rust cdylib DLL)                 │
│  lib.rs (FFI exports) · ffi_types.rs · error.rs                │
│  downloader/ (mod, bandwidth, checksum, connection_pool,        │
│               disk_writer, segment_manager)                     │
│  network/ (http_client, dns_cache)                              │
├─────────────────────────────────────────────────────────────────┤
│                          Models                                 │
│  DownloadItem · DownloadSegment · DownloadStatus                │
│  DownloadSettings · AppSettings · VideoInfo                     │
│  VideoFormat · VideoDownloadItem · TorrentDownloadItem          │
│  TorrentInfo · TorrentFileInfo · TorrentSettings                │
│  BrowserDownloadRequest                                         │
├─────────────────────────────────────────────────────────────────┤
│                       Custom Controls                           │
│  EnhancedProgressBar · SpeedGraph · Tilt3DButton                │
├─────────────────────────────────────────────────────────────────┤
│                     Helpers / Converters                         │
│  HttpHelper · FileTypeHelper · RelayCommand                     │
│  AsyncRelayCommand · ThrottledStream · RetryPolicy              │
│  VisibilityConverters                                           │
└─────────────────────────────────────────────────────────────────┘
```

---

## 3. Tech Stack

| Layer | Technology | Purpose |
|---|---|---|
| **Runtime** | .NET 8 (net8.0-windows10.0.19041.0) | Application runtime |
| **UI Framework** | WPF + WinForms (hybrid) | Main UI (WPF), System tray (WinForms NotifyIcon) |
| **UI Toolkit** | MahApps.Metro 2.4.11 | Metro-style windowing, MetroWindow base class |
| **Material Design** | MaterialDesignThemes 5.3.0 | Material Design visual styles |
| **MVVM** | CommunityToolkit.Mvvm 8.4.0 | MVVM infrastructure |
| **DI** | Microsoft.Extensions.DependencyInjection 8.0.1 | Service registration and resolution |
| **Logging** | Microsoft.Extensions.Logging 8.0.1 | Structured logging with debug + file providers |
| **Torrent** | MonoTorrent 3.0.2 | BitTorrent protocol (DHT, UPnP, NAT-PMP) |
| **Video** | yt-dlp (external CLI tool) | Video extraction from YouTube, etc. |
| **Muxing** | FFmpeg (external CLI tool) | Audio/video muxing and format conversion |
| **Settings** | System.Text.Json | JSON serialization for app settings |
| **Testing** | xUnit + Microsoft.NET.Test.Sdk | Unit testing framework |
| **Native Engine** | Rust (swiftgrab_engine cdylib) | High-performance download core |
| **Async Runtime** | Tokio 1.44 (Rust) | Multi-threaded async runtime for the Rust engine |
| **HTTP Client** | reqwest 0.12 (Rust) | HTTP/2, rustls TLS, gzip/brotli/zstd decompression |
| **Memory-mapped I/O** | memmap2 0.9 (Rust) | Zero-copy file writes via OS page cache |
| **Bandwidth Shaping** | governor 0.8 (Rust) | Token-bucket rate limiting per segment |
| **Checksums** | blake3 1.6 (Rust) | Parallel SIMD-accelerated file integrity verification |
| **Concurrency** | dashmap 6 / parking_lot 0.12 (Rust) | Lock-free concurrent maps and fast mutexes |
| **Installer** | Inno Setup 6 | Windows installer with protocol/file association registration |

---

## 4. Core Features

### 4.1 Rust-Powered Segmented HTTP Downloads (v3.0 — New)
- **Native Rust engine** (`swiftgrab_engine.dll`) for maximum throughput
- **HTTP/2 multiplexing** with rustls TLS — no OpenSSL dependency
- **Memory-mapped I/O** via `memmap2` for zero-copy writes directly to OS page cache
- **Coalescing write buffer** (512 KB) reduces syscall overhead for small chunks
- **Token-bucket bandwidth shaping** via `governor` — smooth, jitter-free throttling
- **Parallel BLAKE3 checksums** — SIMD-accelerated integrity verification post-download
- **Connection pooling** with async semaphore-based slot management (RAII guards)
- **Caching DNS resolver** with configurable TTL and async offloaded resolution
- Splits large files into configurable segments (default: 8)
- Each segment downloads in parallel for maximum bandwidth utilization
- **Dynamic Rebalancing:** `DynamicRebalancer` monitors segment speeds and steals work from slow segments
- **Work-Stealing Scheduler:** Splits underperforming segments mid-download
- **Resume Support:** Detects server `Accept-Ranges` capability for pause/resume
- **Exponential backoff with jitter** on segment retry (up to configurable max retries)
- **Transparent decompression:** gzip, brotli, zstd handled at the HTTP layer

### 4.2 Video Downloading
- **yt-dlp Integration:** Extracts video info, lists available formats (resolution, codec, bitrate)
- **Format Selection:** Users can pick specific quality or let SwiftGrab auto-select best
- **FFmpeg Muxing:** Merges separate video+audio streams into a single MP4
- **Batch Downloads:** Download multiple videos in one operation
- **Platform Support:** YouTube, Vimeo, Twitter/X, and hundreds of sites via yt-dlp

### 4.3 BitTorrent Protocol
- Full BitTorrent client via MonoTorrent engine
- DHT, UPnP, NAT-PMP for peer discovery and port forwarding
- Magnet link parsing and .torrent file support
- Sequential download mode
- Upload/download speed limits
- Per-torrent statistics (seeds, peers, ratio, ETA)

### 4.4 Browser Integration
- **Custom Protocol Handler:** `swiftgrab://` URL scheme for one-click downloads
- **Native Messaging Host:** Chrome/Edge extension communication via stdin/stdout
- **Capture Rules:** Configurable file extension and minimum size filters
- Browser extension installation manager

### 4.5 Application Features
- **Theming:** Light/Dark/System theme with 8 accent colors (Teal, Blue, Purple, Orange, Red, Green, Pink, Amber)
- **System Tray:** Minimize to tray with notification support
- **Auto-Updates:** Background update checking against a GitHub-hosted manifest
- **MSIX Packaging:** Ready for Microsoft Store distribution
- **Diagnostics:** Centralized error reporting via `AppDiagnostics`
- **Settings Export/Import:** Portable JSON-based configuration

### 4.6 Custom Controls
- **EnhancedProgressBar:** Gradient fill, pulse animation, segment indicators
- **SpeedGraph:** Real-time 60-second speed visualization with polyline rendering
- **Tilt3DButton:** Hover effect button with perspective transforms

---

## 5. Project Structure

```
SwiftGrab/
├── App.xaml / App.xaml.cs          # Application entry, DI setup, global error handling
├── SwiftGrab.csproj                # Project file (.NET 8, WPF + WinForms hybrid)
├── Assets/                         # Icons and images
│   ├── icon.ico                    # Application icon (embedded in .exe)
│   └── Images/
│       ├── SwiftGrab-Logo.png      # Logo used for installer wizard images
│       └── developer-photo.png
├── BrowserExtension/               # Chrome/Edge extension files
├── build/                          # Build & packaging infrastructure
│   ├── Build-Installer.ps1         # Orchestrates publish + Inno Setup compilation
│   ├── manifest.json               # Auto-update manifest (version, URL, notes)
│   └── installer/
│       ├── SwiftGrab.iss           # Inno Setup installer script (v3.0)
│       ├── GenerateWizardImages.ps1 # Generates wizard BMP images from logo
│       ├── WizardImage.bmp         # 164×314 sidebar image (generated)
│       └── WizardSmallImage.bmp    # 55×58 corner image (generated)
├── Controls/                       # Custom WPF controls
│   ├── EnhancedProgressBar.cs
│   ├── SpeedGraph.cs
│   └── Tilt3DButton.cs
├── Converters/                     # WPF value converters
│   └── VisibilityConverters.cs
├── Helpers/                        # Utility classes
│   ├── AsyncRelayCommand.cs
│   ├── FileTypeHelper.cs
│   ├── HttpHelper.cs
│   ├── RelayCommand.cs
│   ├── RetryPolicy.cs
│   └── ThrottledStream.cs
├── Models/                         # Plain data models
│   ├── AppSettings.cs
│   ├── BrowserDownloadRequest.cs
│   ├── DownloadItem.cs
│   ├── DownloadSegment.cs
│   ├── DownloadSettings.cs
│   ├── DownloadStatus.cs
│   ├── TorrentDownloadItem.cs
│   ├── TorrentFileInfo.cs
│   ├── TorrentInfo.cs
│   ├── TorrentSettings.cs
│   ├── VideoDownloadItem.cs
│   ├── VideoFormat.cs
│   └── VideoInfo.cs
├── Services/                       # Business logic layer
│   ├── AppDiagnostics.cs
│   ├── BrowserExtensionManagerService.cs
│   ├── BrowserIntegrationService.cs
│   ├── DownloadCoordinator.cs
│   ├── DownloadManager.cs
│   ├── DownloadService.cs
│   ├── DynamicRebalancer.cs
│   ├── FFmpegHelper.cs
│   ├── HttpCapabilityService.cs
│   ├── MagnetLinkParser.cs
│   ├── NativeMessagingHost.cs
│   ├── NativeHostBridgeService.cs
│   ├── ProcessRunner.cs
│   ├── ProtocolHandler.cs
│   ├── ResumeStore.cs
│   ├── RustInterop/
│   │   ├── NativeMethods.cs        # P/Invoke declarations for Rust FFI
│   │   └── RustDownloadBridge.cs   # Managed bridge to native engine
│   ├── SegmentDownloader.cs
│   ├── SegmentedDownloadService.cs
│   ├── SegmentPerformanceMonitor.cs
│   ├── SettingsService.cs
│   ├── SwiftGrabBackgroundService.cs
│   ├── ThemeService.cs
│   ├── ToolsManager.cs
│   ├── TorrentDownloadService.cs
│   ├── TorrentEngine.cs
│   ├── VideoDownloadService.cs
│   ├── VideoExtractors/
│   │   ├── GenericVideoExtractor.cs
│   │   ├── VideoExtractorBase.cs
│   │   └── YouTubeExtractor.cs
│   ├── WorkStealingScheduler.cs
│   └── YtDlpService.cs
├── src/Services/
│   └── UpdateService.cs
├── swiftgrab_engine/               # Rust native download engine (cdylib)
│   ├── Cargo.toml                  # Rust package manifest & dependencies
│   └── src/
│       ├── lib.rs                  # FFI exports (P/Invoke entry points)
│       ├── ffi_types.rs            # C-compatible struct/enum definitions
│       ├── error.rs                # Error types with ResultCode mapping
│       ├── downloader/
│       │   ├── mod.rs              # Download pipeline orchestrator
│       │   ├── bandwidth.rs        # Token-bucket bandwidth shaping
│       │   ├── checksum.rs         # BLAKE3 parallel checksums
│       │   ├── connection_pool.rs  # Async semaphore connection pool
│       │   ├── disk_writer.rs      # Memory-mapped & legacy I/O writers
│       │   └── segment_manager.rs  # Segment creation & work-stealing support
│       └── network/
│           ├── mod.rs
│           ├── http_client.rs      # HTTP/2 client with rustls, proxy, decompression
│           └── dns_cache.rs        # TTL-based caching DNS resolver
├── ViewModels/                     # MVVM view models
│   ├── BaseViewModel.cs
│   ├── DownloadItemViewModel.cs
│   ├── DownloadViewModel.cs
│   └── MainViewModel.cs
├── Views/                          # WPF windows and dialogs
│   ├── MainWindow.xaml / .xaml.cs
│   ├── SettingsWindow.xaml / .xaml.cs
│   ├── AboutDeveloperDialog.xaml / .xaml.cs
│   ├── ActiveDownloadWindow.xaml / .xaml.cs
│   ├── AddUrlDialog.xaml / .xaml.cs
│   ├── BatchDownloadDialog.xaml / .xaml.cs
│   ├── BrowserExtensionDialog.xaml / .xaml.cs
│   ├── DownloadFileInfoDialog.xaml / .xaml.cs
│   └── VideoResolutionDialog.xaml / .xaml.cs
├── Styles/                         # Theme XAML resource dictionaries
│   ├── DarkTheme.xaml
│   ├── LightTheme.xaml
│   ├── FuturisticStyles.xaml
│   ├── CinematicControls.xaml
│   ├── EnhancedControls.xaml
│   └── ExtensionStyles.xaml
├── SwiftGrab.Tests/                # xUnit test project
└── .github/
    ├── copilot-instructions.md
    └── workflows/publish.yml
```

---

## 6. DI Registration Map

| Registration | Lifetime | Description |
|---|---|---|
| `SettingsService` | Singleton | App-wide settings (lazy-loaded) |
| `ThemeService` | Singleton | Theme/accent management |
| `DownloadManager` | Singleton | Download lifecycle orchestrator |
| `TorrentDownloadService` | Singleton | Torrent download management |
| `BrowserIntegrationService` | Singleton | Browser native messaging |
| `UpdateService` | Singleton | Auto-update checking |
| `AppDiagnostics` | Singleton | Centralized diagnostics |
| `SegmentDownloader` | Transient | Per-download segment worker |
| `DownloadService` | Transient | Single HTTP download handler |
| `DownloadCoordinator` | Transient | Segment coordination |
| `YtDlpService` | Transient | Video info extraction |
| `VideoDownloadService` | Transient | Video download orchestration |
| `MainViewModel` | Transient | Main window data context |
| `MainWindow` | Transient | Main application window |

---

## 7. Data Flow

### HTTP Download Flow
```
User → MainViewModel.AddDownloadFromUrl()
  → DownloadManager.AddDownload()
    → DownloadManager.TrackDownload()
      → DownloadCoordinator.InitializeAsync() [HEAD request, range check, file prealloc]
      → DownloadCoordinator.StartAsync()
        → SegmentDownloader.DownloadAsync() × N segments [parallel]
        → DynamicRebalancer.Rebalance() [monitoring loop]
          → WorkStealingScheduler.TrySteal() [split slow segments]
```

### Rust Engine Download Flow (v3.0 — Native Path)
```
C# RustDownloadBridge.StartDownloadAsync()
  → P/Invoke → swiftgrab_start_download(url, path, config, callback)
    → Rust execute_download() pipeline:
      Phase 1: HttpDownloadClient.probe_file() [HEAD → content-length, ranges, HTTP/2]
      Phase 2: disk_writer::preallocate_file() [sparse file allocation]
      Phase 3: segment_manager::create_segments() [split into N byte ranges]
      Phase 4: Initialize subsystems:
               → ConnectionPool (async semaphore, N slots)
               → BandwidthShaper (governor token-bucket, per-segment limits)
               → MmapWriter (memmap2, zero-copy writes)
      Phase 5: tokio::spawn() × N segment download tasks [parallel]:
               → download_segment_mmap() [memory-mapped I/O path]
                 → CoalescingWriter (512KB buffer → mmap flush)
               → download_segment_legacy() [tokio file I/O fallback]
               → BandwidthShaper.acquire() [token wait per chunk]
               → Exponential backoff + jitter on retry
      Phase 6: report_progress_loop() [100ms interval → FFI callback → C#]
      Phase 7: Await all segment tasks
      Phase 8: checksum::verify_file() [BLAKE3 parallel SIMD verification]
    → ResultCode returned to C# via P/Invoke
```

### Video Download Flow
```
User → AddUrlDialog → VideoResolutionDialog (format picker)
  → MainViewModel.AddVideoDownload()
    → DownloadManager.AddVideoDownload()
      → YtDlpService.DownloadAsync() [yt-dlp CLI process]
        → FFmpegHelper [mux if needed]
```

### Torrent Download Flow
```
User → .torrent file / magnet link
  → TorrentDownloadService → TorrentEngine
    → MonoTorrent ClientEngine [BitTorrent protocol]
```

---

## 8. Key Design Decisions

| Decision | Rationale |
|---|---|
| **Rust native engine** | Maximum download throughput via zero-copy mmap I/O, HTTP/2, and token-bucket shaping — impossible to match in managed C# alone |
| **P/Invoke FFI bridge** | Minimal overhead interop; `#[repr(C)]` structs map directly to C# `StructLayout.Sequential` |
| **Tokio multi-thread runtime** | Scales to 16 worker threads (capped by `available_parallelism()`), `enable_all` for timers + I/O |
| **reqwest + rustls** | No OpenSSL dependency; HTTP/2 adaptive window; built-in gzip/brotli/zstd decompression |
| **memmap2 with CoalescingWriter** | Writes go directly to OS page cache; 512 KB coalescing buffer reduces syscall overhead |
| **governor token-bucket** | Smooth per-segment bandwidth shaping with burst allowance (256 KB) — no sleep-loop jitter |
| **BLAKE3 checksums** | SIMD-accelerated, parallelized across threads; ~10× faster than SHA-256 for large files |
| WPF + WinForms hybrid | WPF for main UI; WinForms for `NotifyIcon` system tray (no WPF equivalent) |
| Singleton services with DI | `SettingsService.Instance` and `ThemeService.Instance` predate DI adoption; registered via factory lambda |
| External yt-dlp/FFmpeg | CLI tools auto-downloaded at runtime; avoids bundling large binaries |
| Segmented downloads with rebalancing | Maximizes bandwidth by splitting slow segments to faster threads |
| ConcurrentDictionary for task tracking | Thread-safe tracking of active downloads and cancellation tokens |
| MahApps.Metro + MaterialDesignThemes | Provides polished Metro-style chrome and Material Design controls |
| MSIX + unpackaged dual mode | Supports both Store distribution and traditional .exe deployment |
| Inno Setup installer | Full Windows installer with protocol handlers, file associations, native messaging registration |

---

## 9. Identified Improvement Areas

### 9.1 CommunityToolkit.Mvvm Is Referenced but Underused
The project references `CommunityToolkit.Mvvm 8.2.2` but implements its own `BaseViewModel`, `RelayCommand`, and `AsyncRelayCommand` instead of using the toolkit's `ObservableObject`, `RelayCommand`, and `AsyncRelayCommand`. This is redundant.

### 9.2 Model Layer Depends on ViewModel Layer
`DownloadSegment` and `TorrentDownloadItem` inherit from `BaseViewModel` (a ViewModel class). Models should not depend on ViewModels — this creates a circular architectural dependency.

### 9.3 Singleton Anti-Pattern
`SettingsService.Instance` and `ThemeService.Instance` use `Lazy<T>` singletons but are also registered in DI. This dual-access pattern makes testing difficult and bypasses DI.

### 9.4 Silent Exception Swallowing
Several `catch` blocks silently ignore exceptions (e.g., `ThemeService.GetSystemTheme()`, `SettingsService.Load()`). These should at minimum log to the diagnostics service.

### 9.5 Outdated Package Versions
NuGet packages are at older versions — updating them brings bug fixes, performance improvements, and security patches.

### 9.6 Duplicate Tool Management
Both `YtDlpService` and `ToolsManager` have independent logic for downloading yt-dlp. Only `ToolsManager` should own tool lifecycle.

### 9.7 UpdateService Uses Separate HttpClient
`UpdateService` creates its own `HttpClient` instead of using `HttpHelper.SharedClient`, risking socket exhaustion.

### 9.8 File-Scoped Namespaces Not Used
The codebase uses block-scoped namespaces throughout. Modern C# (10+) file-scoped namespaces reduce indentation.

### 9.9 Test Project Is Empty
`SwiftGrab.Tests` has no test files — only auto-generated scaffolding.

---

## 10. Applied Fixes (Error Handling & Core Functionality)

### 10.1 DownloadCoordinator — Fire-and-Forget Segment Tasks
Segment download tasks were started with `Task.Run` without observing exceptions. Failures in individual segments were silently lost. Fixed to propagate segment exceptions via the `ErrorOccurred` event and properly await segment completion.

### 10.2 DownloadManager.CancelDownloadAsync — CTS Disposal Leak
The `CancellationTokenSource` was removed from `_cancellationTokens` but never disposed on the cancel path. Fixed to always dispose after removal.

### 10.3 DownloadCoordinator.InitializeAsync — Zero-Size File Handling
When the server reports `Content-Length: 0` or omits it, segment ranges were calculated incorrectly (division by zero or negative ranges). Fixed to fall back to a single-segment download when the total size is unknown or zero.

### 10.4 DownloadService.StartAsync — CTS Disposal Ordering
The linked `CancellationTokenSource` was declared with `using var`, causing it to be disposed *after* the source CTS in the `finally` block — the wrong order. Fixed to manually dispose both in the correct order: linked CTS first, then source CTS.

### 10.5 DownloadManager — Disk Space Validation
Added pre-download disk space check after the coordinator reports the total file size. Throws a clear `IOException` if available space is insufficient, preventing partial downloads that would fail mid-stream.

### 10.6 DownloadManager.AddDownload — URL Validation
Added `Uri.TryCreate` validation that rejects non-HTTP/HTTPS URLs upfront with a descriptive `UriFormatException`.

### 10.7 MainViewModel — Async Command Error Handling
`StartSelectedDownloadAsync` now catches exceptions to set proper failure status and status message instead of letting them propagate unhandled. `AddDownloadFromUrl` catches `UriFormatException` and `ArgumentException` to report validation errors via the status bar.

### 10.8 DownloadManager.StartDownloadAsync — CTS Disposal
The `CancellationTokenSource` created in `StartDownloadAsync` was never disposed. Fixed the `finally` block to dispose it after removal from the dictionary.

---

## 11. Rust Download Engine Deep-Dive (`swiftgrab_engine`)

### 11.1 Overview

The Rust download engine is a native `cdylib` (C-compatible dynamic library) compiled to `swiftgrab_engine.dll`. It provides a high-performance download pipeline that the .NET application calls via P/Invoke. The engine is designed for:

- **Maximum throughput** via HTTP/2 multiplexing and parallel segment downloads
- **Minimal memory overhead** via zero-copy memory-mapped file I/O
- **Smooth bandwidth control** via token-bucket rate limiting
- **Reliability** via exponential backoff retries with jitter

### 11.2 Crate Structure

```
swiftgrab_engine/
├── Cargo.toml              # Package: v3.0.0, crate-type = ["cdylib"]
└── src/
    ├── lib.rs              # FFI entry points (extern "C" functions)
    ├── ffi_types.rs        # #[repr(C)] structs/enums for P/Invoke
    ├── error.rs            # EngineError enum with thiserror + ResultCode mapping
    ├── downloader/
    │   ├── mod.rs          # Download struct + execute_download() pipeline
    │   ├── bandwidth.rs    # BandwidthShaper (governor token-bucket)
    │   ├── checksum.rs     # BLAKE3 parallel hash + verify
    │   ├── connection_pool.rs # Async semaphore connection pool
    │   ├── disk_writer.rs  # MmapWriter + CoalescingWriter + preallocate
    │   └── segment_manager.rs # Segment creation + work-stealing metadata
    └── network/
        ├── mod.rs
        ├── http_client.rs  # HttpDownloadClient (reqwest, HTTP/2, rustls)
        └── dns_cache.rs    # CachingDnsResolver (TTL-based)
```

### 11.3 Key Dependencies

| Crate | Version | Purpose |
|---|---|---|
| `tokio` | 1.44 | Multi-threaded async runtime (full features) |
| `reqwest` | 0.12 | HTTP/2 client with rustls TLS, gzip/brotli/zstd |
| `memmap2` | 0.9 | Memory-mapped file I/O for zero-copy writes |
| `governor` | 0.8 | Token-bucket rate limiter for bandwidth shaping |
| `blake3` | 1.6 | SIMD-accelerated parallel checksumming |
| `dashmap` | 6 | Lock-free concurrent hash map |
| `parking_lot` | 0.12 | Fast synchronization primitives (Mutex, RwLock) |
| `bytes` | 1.9 | Zero-copy buffer management |
| `thiserror` | 2 | Derive macro for error types |
| `tracing` | 0.1 | Structured logging/instrumentation |
| `backon` | 1 | Retry strategies with exponential backoff |
| `async-compression` | 0.4 | Transparent async decompression |

### 11.4 FFI Interface (lib.rs)

The engine exposes these `extern "C"` functions called from C# via `NativeMethods.cs`:

| Function | Description |
|---|---|
| `swiftgrab_init()` | Initialize engine with defaults (Tokio runtime + HTTP client) |
| `swiftgrab_init_with_config(*EngineConfig)` | Initialize with custom proxy, user agent, DNS TTL |
| `swiftgrab_start_download(url, path, *config, callback)` | Start a download, returns handle |
| `swiftgrab_get_progress(handle, *progress)` | Poll current progress for a handle |
| `swiftgrab_pause(handle)` | Pause a download |
| `swiftgrab_resume(handle)` | Resume a paused download |
| `swiftgrab_cancel(handle)` | Cancel a download |
| `swiftgrab_set_progress_callback(callback)` | Set global progress callback |
| `swiftgrab_shutdown()` | Graceful shutdown of the engine |

All FFI types use `#[repr(C)]` for binary layout compatibility with C# `StructLayout.Sequential`.

### 11.5 Download Pipeline (execute_download)

The download follows an 8-phase pipeline:

```
Phase 1: Server Probe
  └── HEAD request → content_length, supports_ranges, is_http2

Phase 2: File Pre-allocation
  └── Create sparse file on disk (set_len) for known-size downloads

Phase 3: Segment Creation
  └── Split total_bytes into N segments (or single segment if no range support)

Phase 4: Subsystem Initialization
  ├── ConnectionPool (semaphore with N slots, RAII guards)
  ├── BandwidthShaper (governor token-bucket, per-segment limits)
  └── MmapWriter (memory-mapped file, zero-copy writes)

Phase 5: Parallel Segment Downloads (tokio::spawn × N)
  ├── Mmap path: CoalescingWriter → 512KB buffer → mmap flush
  ├── Legacy path: tokio async file I/O (unknown-size fallback)
  ├── Bandwidth: shaper.acquire(bytes) per chunk
  └── Retry: exponential backoff with ±25% jitter

Phase 6: Progress Reporting (100ms interval)
  └── FFI callback → DownloadProgress struct → C# event

Phase 7: Completion
  └── Await all segment tasks, handle errors/cancellation

Phase 8: Integrity Verification
  └── BLAKE3 checksum (if configured) → verify against expected hash
```

### 11.6 Memory-Mapped I/O Architecture

The `MmapWriter` provides zero-copy writes:

1. **Pre-allocation:** File created at full size via `set_len()` (sparse file)
2. **Memory mapping:** `memmap2::MmapMut::map_mut()` maps the file into virtual memory
3. **Coalescing:** `CoalescingWriter` buffers small writes (512 KB) before flushing to mmap
4. **Concurrent writes:** Multiple segments write to different byte ranges without locking
5. **OS page cache:** Writes go directly to the page cache; OS handles disk flushing

**Fallback:** If mmap fails (e.g., network file systems), the engine falls back to `download_segment_legacy()` using tokio async file I/O.

### 11.7 Bandwidth Shaping

The `BandwidthShaper` uses the `governor` crate's token-bucket algorithm:

- **Global limit** (KB/s) divided evenly across segments
- **Burst allowance:** min(per_segment_limit, 256 KB) for smooth throughput
- **Large chunk splitting:** Chunks > 16 KB are split into 16 KB token requests
- **Zero limit (unlimited):** `acquire()` returns immediately with no overhead
- **Thread-safe:** Uses `Arc<RateLimiter>` shared across all segment tasks

### 11.8 C# Interop Bridge

`RustDownloadBridge.cs` provides the managed wrapper:

- **Singleton pattern** with `Lazy<T>` initialization
- **GC-pinned callback delegate** prevents collection while native code holds the pointer
- **ConcurrentDictionary** maps native handles to `DownloadItem` for progress routing
- **Events:** `ProgressChanged` and `DownloadCompleted` fire on the thread pool
- **Graceful fallback:** If `swiftgrab_engine.dll` is missing, the app falls back to the pure C# download pipeline

---

## 12. Installer & Packaging (v3.0)

### 12.1 Inno Setup Installer

The installer is built with **Inno Setup 6** and lives in `build/installer/SwiftGrab.iss`. Features:

| Feature | Details |
|---|---|
| **Compression** | LZMA2/ultra64, solid compression |
| **Wizard UI** | Modern wizard style with custom branded images |
| **Logo** | Generated from `Assets/Images/SwiftGrab-Logo.png` via `GenerateWizardImages.ps1` |
| **Privileges** | Admin required (with user override dialog) |
| **Min Windows** | 10.0 (Windows 10+) |
| **Close apps** | Force-closes running SwiftGrab instances before install |

**Integration tasks (user-selectable):**

| Task | Default | Description |
|---|---|---|
| Desktop icon | Unchecked | Creates desktop shortcut |
| `swiftgrab://` protocol | Checked | URL protocol handler for browser intercept |
| `magnet:` protocol | Unchecked | Magnet link handler for torrent downloads |
| `.torrent` association | Unchecked | File association for .torrent files |
| Native messaging | Checked | Chrome/Edge/Firefox extension communication |
| Startup entry | Unchecked | Start SwiftGrab at Windows login |

**Files packaged:**
- Self-contained .NET 8 publish output (all assemblies + runtime)
- `swiftgrab_engine.dll` (Rust native engine)
- Browser extension files
- Native messaging manifest (`com.swiftgrab.downloader.json`)

### 12.2 Build Pipeline

```powershell
# Full build: publish → wizard images → compile installer
.\build\Build-Installer.ps1

# With options:
.\build\Build-Installer.ps1 -Version 3.0.0 -Configuration Release -Platform win-x64

# Skip publish (reuse existing):
.\build\Build-Installer.ps1 -SkipPublish

# Prerequisites:
#   - .NET 8 SDK
#   - Rust toolchain (for swiftgrab_engine)
#   - Inno Setup 6 (iscc.exe)
```

**Pipeline steps:**
1. `dotnet publish` — self-contained, ReadyToRun, no single-file (installer bundles)
2. `cargo build --release` — Rust engine DLL (triggered by MSBuild pre-build target)
3. `GenerateWizardImages.ps1` — renders logo into Inno Setup BMP format
4. `iscc SwiftGrab.iss` — compiles the installer EXE

### 12.3 Auto-Update

The app checks `build/manifest.json` for new versions:

```json
{
  "Version": "3.0.0",
  "Url": "https://github.com/.../SwiftGrabInstaller-3.0.0.exe",
  "Notes": "Release notes..."
}
```

`UpdateService` compares the manifest version against the running version and prompts the user to download the new installer.

---

## 13. What's New in v3.0

| Feature | Description |
|---|---|
| 🦀 **Rust Download Engine** | Native `swiftgrab_engine.dll` with HTTP/2, mmap I/O, BLAKE3 checksums |
| ⚡ **Zero-Copy Downloads** | Memory-mapped file writes via `memmap2` — data goes directly to OS page cache |
| 🌊 **Token-Bucket Shaping** | Smooth bandwidth limiting via `governor` — no jitter or sleep loops |
| 🔒 **rustls TLS** | No OpenSSL dependency; modern TLS 1.3 support |
| 📦 **Transparent Decompression** | gzip, brotli, zstd handled automatically at the HTTP layer |
| 🔍 **BLAKE3 Integrity** | SIMD-accelerated parallel file checksumming post-download |
| 🏊 **Connection Pooling** | Async semaphore-based slot management with RAII guards |
| 🔄 **DNS Caching** | TTL-based caching resolver with async offloaded resolution |
| 📊 **Real-time Progress** | 100ms FFI callback to C# with speed, segments, and status |
| 🎨 **Installer Branding** | Custom wizard images generated from SwiftGrab logo |
| 🔧 **Build Pipeline** | Single-command build: publish + Rust + wizard images + Inno Setup |
