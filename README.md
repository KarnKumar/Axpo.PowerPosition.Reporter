# ⚡ PowerPosition.Reporter

A .NET 8 background worker service that automatically extracts, aggregates, and exports power trading positions to CSV on a configurable schedule.

---

## 📋 Table of Contents

- [Overview](#overview)
- [How It Works](#how-it-works)
- [Project Structure](#project-structure)
- [Services Explained](#services-explained)
- [Configuration Reference](#configuration-reference)
- [CLI Commands](#cli-commands)
- [Output Files](#output-files)
- [Period → Time Mapping](#period--time-mapping)
- [Running the App](#running-the-app)
- [Running Tests](#running-tests)
- [Design Principles](#design-principles)
- [Dependencies](#dependencies)

---

## Overview

**PowerPosition.Reporter** connects to `PowerService.dll` (an external power trading API), fetches all trades for the current day, aggregates volumes by clock hour, and writes the results to a timestamped CSV file. It then repeats this on a fixed interval — forever, until stopped.

Every run — whether it succeeds or fails — also writes a detailed `.log` file for auditing.

```
PowerService.dll → fetch trades → aggregate by hour → write CSV + log file
                                                              ↑
                                              repeats every N minutes
```

---

## How It Works

```
┌─────────────────────────────────────────────────────────────┐
│                    Application Startup                      │
│  1. Reads config from appsettings.json (+ optional CLI)     │
│  2. Validates settings — fails fast if anything is wrong    │
│  3. Sets timezone to Europe/London (hardcoded by spec)      │
│  4. Starts the background worker                            │
└────────────────────────┬────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────┐
│              PowerPositionReportWorker (loop)               │
│                                                             │
│  ① Run extract immediately on startup                       │
│  ② Wait IntervalMinutes                                     │
│  ③ Run extract again                                        │
│  ④ Go to ②  (repeats until the app is stopped)             │
│                                                             │
│  ⚠ If an extract fails, the error is logged and the        │
│    scheduler continues — it never crashes the service.      │
└────────────────────────┬────────────────────────────────────┘
                         │
                         ▼ (each extract run)
┌─────────────────────────────────────────────────────────────┐
│                     One Extract Run                         │
│                                                             │
│  1. Resolve trade date (see Period → Time Mapping)          │
│  2. Call PowerService.GetTradesAsync(tradeDate)             │
│  3. Aggregate volumes across all trades by period (1–24)    │
│  4. Map period numbers → HH:mm time labels                  │
│  5. Write  PowerPosition_YYYYMMDD_HHmm.csv                  │
│  6. Write  PowerPosition_YYYYMMDD_HHmm.log  (always)        │
└─────────────────────────────────────────────────────────────┘
```

---

## Project Structure

```
Axpo.PowerPosition.Reporter/
│
├── PowerTradeReportGenerationWorker/          ← Main application
│   │
│   ├── Program.cs                             ← App entry point, DI wiring, startup validation
│   ├── PowerPositionReportWorker.cs           ← Background scheduler (runs the loop)
│   ├── ReportSettings.cs                      ← Strongly-typed config model
│   ├── appsettings.json                       ← Default configuration
│   ├── appsettings.Development.json           ← Dev overrides
│   │
│   ├── Models/
│   │   └── PowerTradePosition.cs              ← Data model: { LocalTime, Volume }
│   │
│   ├── Services/
│   │   ├── IPowerPositionReportService.cs     ← Interface for the report service
│   │   ├── PowerPositionReportService.cs      ← Fetches + aggregates trades
│   │   │
│   │   ├── Csv/
│   │   │   ├── ICsvExportService.cs           ← Interface for CSV writing
│   │   │   └── CsvReportService.cs            ← Writes the CSV file
│   │   │
│   │   ├── Logging/
│   │   │   ├── IExtractLogger.cs              ← Interface for per-run log writer
│   │   │   ├── ExtractLogger.cs               ← Writes timestamped lines to a .log file
│   │   │   └── ExtractLoggerFactory.cs        ← Creates an ExtractLogger for each run
│   │   │
│   │   └── TimeProvider/
│   │       ├── ITimeProvider.cs               ← Interface for time abstraction
│   │       └── ReportTimeProvider.cs          ← Returns current time in Europe/London
│   │
│   └── lib/
│       └── PowerService.dll                   ← External trade data provider (pre-built)
│
└── PowerTradeReportGenerationWorker.Tests/    ← Unit tests
    └── Services/
        ├── CsvExportServiceTests.cs
        ├── PowerPositionReportServiceTests.cs
        └── PowerPositionReportWorkerTests.cs
```

---

## Services Explained

### `PowerPositionReportWorker`
**File:** `PowerPositionReportWorker.cs`

The top-level orchestrator. Inherits from .NET's `BackgroundService` and drives the entire loop.

- Runs the extract **immediately** when the app starts
- Then waits `IntervalMinutes` and repeats
- Uses `PeriodicTimer` (efficient — no busy-wait)
- Wraps each run in a try/catch so a single failure **never kills the scheduler**
- Determines the correct trade date based on Europe/London local time

---

### `PowerPositionReportService`
**File:** `Services/PowerPositionReportService.cs`

Handles all the business logic for one extract run.

1. Calls `PowerService.GetTradesAsync(tradeDate)` to fetch raw trades
2. Loops over every trade and every period, summing volumes into a dictionary keyed by period number (1–24)
3. Pre-seeds all 24 periods to `0.0` — so the CSV always has exactly 24 rows, even on quiet days
4. Maps period numbers to `HH:mm` time labels (period 1 = `23:00`, period 2 = `00:00`, etc.)
5. Logs each trade's raw volumes into the per-run `.log` file

> ⚠️ `PowerService` is registered as **transient** (new instance per run), because it is assumed not to be thread-safe.

---

### `CsvExportService`
**File:** `Services/Csv/CsvReportService.cs`

Writes the aggregated positions to a UTF-8 CSV file using [CsvHelper](https://joshclose.github.io/CsvHelper/).

- Output: `{OutputPath}/PowerPosition_YYYYMMDD_HHmm.csv`
- Columns: `Local Time`, `Volume`
- Volumes are rounded to the nearest whole number
- Only written on **successful** extracts — if fetching or aggregating fails, no CSV is created

---

### `ExtractLogger` / `ExtractLoggerFactory`
**Files:** `Services/Logging/ExtractLogger.cs`, `ExtractLoggerFactory.cs`

A lightweight file logger used for **per-run audit logs**, separate from the main application console log (Serilog).

- Every run creates a new `.log` file: `{OutputPath}/logs/PowerPosition_YYYYMMDD_HHmm.log`
- Each line is formatted as: `YYYY-MM-DD HH:mm [LEVEL] Message`
- A log file is **always** created — even if the extract fails — so you always have a record of what happened
- Errors in the log writer itself are silently swallowed so they never interrupt extraction

---

### `ReportTimeProvider`
**File:** `Services/TimeProvider/ReportTimeProvider.cs`

Abstracts the system clock. Returns the current time converted to **Europe/London** timezone (hardcoded per business specification).

This abstraction makes time-dependent logic fully unit-testable by injecting a mock in tests.

---

## Configuration Reference

Configuration lives in `appsettings.json` and can be overridden at runtime via CLI arguments.

### `appsettings.json`

```json
{
  "ReportSettings": {
    "OutputPath": "output",
    "IntervalMinutes": 15
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    }
  }
}
```

### Settings Table

| Setting | Type | Required | Default | Description |
|---|---|---|---|---|
| `ReportSettings:OutputPath` | `string` | ✅ Yes | `"output"` | Folder where CSV reports and log files are written. Can be a relative or absolute path. |
| `ReportSettings:IntervalMinutes` | `int` | ✅ Yes | `15` | How many minutes to wait between extract runs. Must be greater than 0. |
| `Serilog:MinimumLevel:Default` | `string` | ❌ No | `"Information"` | Minimum log level for the console output. Options: `Verbose`, `Debug`, `Information`, `Warning`, `Error`, `Fatal`. |

### Validation Rules

The app **validates settings at startup** and will refuse to start if:
- `OutputPath` is empty or missing → `InvalidOperationException`
- `IntervalMinutes` is 0 or negative → `InvalidOperationException`

---

## CLI Commands

### Build the project

```bash
dotnet build
```

### Run with default settings (from `appsettings.json`)

```bash
dotnet run --project PowerTradeReportGenerationWorker
```

### Run with a custom output folder

```bash
# Windows
dotnet run --project PowerTradeReportGenerationWorker --ReportSettings:OutputPath "C:\Reports\PowerPosition"

# Linux / macOS
dotnet run --project PowerTradeReportGenerationWorker --ReportSettings:OutputPath "/var/reports/power"
```

### Run with a custom interval

```bash
# Run every 30 minutes
dotnet run --project PowerTradeReportGenerationWorker --ReportSettings:IntervalMinutes 30

# Run every 1 minute (useful for testing)
dotnet run --project PowerTradeReportGenerationWorker --ReportSettings:IntervalMinutes 1
```

### Override both settings at once

```bash
dotnet run --project PowerTradeReportGenerationWorker \
  --ReportSettings:OutputPath "/var/reports/power" \
  --ReportSettings:IntervalMinutes 30
```

### Set the log level to Debug (verbose output)

```bash
dotnet run --project PowerTradeReportGenerationWorker \
  --Serilog:MinimumLevel:Default Debug
```

### Run with environment-specific config

```bash
# Uses appsettings.Development.json overrides
DOTNET_ENVIRONMENT=Development dotnet run --project PowerTradeReportGenerationWorker
```

### Publish a self-contained executable

```bash
# Windows x64
dotnet publish PowerTradeReportGenerationWorker -c Release -r win-x64 --self-contained

# Linux x64
dotnet publish PowerTradeReportGenerationWorker -c Release -r linux-x64 --self-contained
```

### Run the published executable with overrides

```bash
# Windows
.\PowerPosition.Reporter.exe --ReportSettings:OutputPath "D:\Output" --ReportSettings:IntervalMinutes 10

# Linux
./PowerPosition.Reporter --ReportSettings:OutputPath /mnt/output --ReportSettings:IntervalMinutes 10
```

---

## Running Tests

```bash
# Run all unit tests
dotnet test

# Run with detailed output
dotnet test --verbosity normal

# Run a specific test project
dotnet test PowerTradeReportGenerationWorker.Tests

# Run and show test coverage (requires coverlet)
dotnet test --collect:"XPlat Code Coverage"
```

---

## Output Files

All files are written to `OutputPath` (default: `./output/`).

### CSV Report

```
output/
└── PowerPosition_20260402_1430.csv
```

**Format:**

```csv
Local Time,Volume
23:00,1500
00:00,800
01:00,950
...
22:00,1200
```

- Always contains exactly **24 rows** (one per clock hour)
- Time labels are in **Europe/London** local time
- Volumes are rounded to the nearest whole number
- Only written on successful runs

### Log File

```
output/
└── logs/
    └── PowerPosition_20260402_1430.log
```

**Format:**

```
2026-04-02 14:30 [INF] ======== Report Extract started at 2026-04-02 14:30 (Europe/London) ========
2026-04-02 14:30 [INF] Trade date resolved to 2026-04-02
2026-04-02 14:30 [INF] Trade 1:
2026-04-02 14:30 [INF] [  100.0 |   50.0 |  -20.0 | ... ]
2026-04-02 14:30 [INF] Aggregation complete: 24 hourly positions produced
2026-04-02 14:30 [INF] === Extract completed successfully. File: PowerPosition_20260402_1430.csv ===
```

- Written **for every run**, including failures
- Contains raw per-trade volumes for full auditability
- Log level is one of: `INF`, `WRN`, `ERR`

---

## Period → Time Mapping

The trading day starts at **23:00** of the previous calendar day:

| Period | Local Time (Europe/London) |
|--------|---------------------------|
| 1      | 23:00                      |
| 2      | 00:00                      |
| 3      | 01:00                      |
| 4      | 02:00                      |
| ...    | ...                        |
| 24     | 22:00                      |

**Trade date resolution:** If the current local time is **23:00 or later**, the trade date is set to **tomorrow's date** — because period 1 (23:00) already belongs to the next trading day.

---

## Design Principles

| Principle | How it's applied |
|---|---|
| **Fail-safe scheduling** | A failed extract logs the error and the timer continues — the service never crashes |
| **Fail-safe logging** | Errors inside the log writer are swallowed — logging never interrupts extraction |
| **Dependency injection** | All services are injected via constructor; easy to mock in tests |
| **Startup validation** | Bad config causes an immediate, descriptive error at launch — not at runtime |
| **Time abstraction** | `ITimeProvider` wraps the clock so all time-dependent code is unit-testable |
| **Separation of concerns** | Scheduling, business logic, CSV writing, and logging are all separate services |

---

## Dependencies

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Extensions.Hosting` | 10.0.5 | Background service host, DI, configuration |
| `Serilog` | 4.3.1 | Structured logging framework |
| `Serilog.Extensions.Hosting` | 10.0.0 | Serilog integration with .NET host |
| `Serilog.Settings.Configuration` | 10.0.0 | Read Serilog config from `appsettings.json` |
| `Serilog.Sinks.Console` | 6.1.1 | Console log output |
| `Serilog.Sinks.File` | 7.0.0 | File log output |
| `CsvHelper` | 33.1.0 | CSV file writing |
| `PowerService.dll` | (local) | External power trade data API |

**Requires:** .NET 8 SDK or later

---

## Author

Karn Kumar — karn2802@gmail.com  
Internal project for power position reporting and automation.
