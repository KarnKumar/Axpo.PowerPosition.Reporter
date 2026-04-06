# ⚡ PowerPosition.Reporter

> A .NET 8 background worker that fetches power trading positions, aggregates them by clock hour,
> and exports the results to a timestamped CSV — automatically, on a configurable schedule,
> with full audit logging on every run.

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
- [Running Tests](#running-tests)
- [Design Principles](#design-principles)
- [Dependencies](#dependencies)

---

## Overview

**PowerPosition.Reporter** connects to the power trading system, fetches all trades for the current day, aggregates volumes by clock hour, and writes the results to a timestamped CSV file. It then waits a configured interval and repeats — forever, until stopped.

Every run — whether it succeeds or fails — also produces a detailed audit log file.

```
Trading System → fetch trades → aggregate by hour → write CSV + audit log
                                                            ↑
                                            repeats every N minutes
```

---

## How It Works

### Startup

1. Loads configuration from the settings file (or CLI overrides)
2. Validates all required settings — refuses to start if anything is missing or invalid
3. Locks the timezone to **Europe/London** as required by the specification
4. Launches the background scheduling loop

### The scheduling loop

```
┌─────────────────────────────────────────────────────────────┐
│  ① Run extract immediately on startup                       │
│  ② Wait IntervalMinutes                                     │
│  ③ Run extract again                                        │
│  ④ Go to ②  (repeats until the app is stopped)             │
│                                                             │
│  ⚠ If an extract fails, the error is logged and the        │
│    scheduler continues — it never crashes the service.      │
└─────────────────────────────────────────────────────────────┘
```

### Each extract run

1. Resolve the correct trade date from the Europe/London local clock
2. Fetch all trades from the trading system for that date
3. Sum volumes across all trades, grouped by period (1–24)
4. Map period numbers to HH:mm time labels
5. Write the CSV report *(only on success)*
6. Write the audit log file *(always)*

---

## Project Structure

```
Axpo.PowerPosition.Reporter/
│
├── PowerTradeReportGenerationWorker/          ← Main application
│   ├── Program.cs                             ← Entry point & startup configuration
│   ├── PowerPositionReportWorker.cs           ← Background scheduling loop
│   ├── ReportSettings.cs                      ← Configuration model
│   ├── appsettings.json                       ← Default settings
│   │
│   ├── Models/                                ← Data models
│   ├── Services/
│   │   ├── PowerPositionReportService.cs      ← Trade fetching & aggregation
│   │   ├── Csv/                               ← CSV export
│   │   ├── Logging/                           ← Per-run audit log writer
│   │   └── TimeProvider/                      ← Europe/London clock abstraction
│   │
│   └── lib/
│       └── PowerService.dll                   ← External trading system API
│
└── PowerTradeReportGenerationWorker.Tests/    ← Unit test suite
    └── Services/
        ├── CsvExportServiceTests.cs
        ├── PowerPositionReportServiceTests.cs
        ├── PowerPositionReportWorkerTests.cs
        └── ReportTimeProviderTests.cs
```

---

## Services Explained

### Scheduler
Drives the entire extract loop. Runs immediately on startup, then repeats on the configured interval. A failed extract is logged and the scheduler carries on — the service never goes down due to a single bad run.

---

### Report Service
Handles all business logic for one extract. Fetches trades from the trading system, sums volumes across all trades per period, and maps each period to its Europe/London clock-hour label. All 24 periods are always included in the output, even on quiet trading days where some hours have zero volume.

---

### CSV Export Service
Writes the aggregated hourly positions to a UTF-8 CSV file. Always produces exactly 24 data rows. Volumes are rounded to the nearest whole number. Only written when the extract completes successfully.

---

### Audit Logger
Writes a separate log file for every single run — success or failure. Captures raw trade volumes, aggregation results, and any errors. Completely independent from the main application console log so the two never interfere with each other.

---

### Time Provider
Abstracts the system clock to always return time in **Europe/London** (UTC+0 in winter, UTC+1 during British Summer Time). The machine's local timezone is never used — the timezone is hardcoded to match the business specification. Only UTC input is accepted when converting times; passing any other kind of datetime is rejected immediately to prevent silent data errors on servers in other timezones. This abstraction also makes all time-dependent logic fully testable without relying on the real system clock.

---

## Configuration Reference

### `appsettings.json`

```json
{
  "ReportSettings": {
    "OutputPath": "output",
    "IntervalMinutes": 15
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information"
    }
  }
}
```

### Settings

| Setting | Type | Default | Description |
|---|---|---|---|
| `ReportSettings:OutputPath` | string | `"output"` | Folder where CSV and log files are written. Relative or absolute path. |
| `ReportSettings:IntervalMinutes` | int | `15` | Minutes between each extract run. Must be greater than 0. |
| `Serilog:MinimumLevel:Default` | string | `"Information"` | Console log verbosity: `Verbose` `Debug` `Information` `Warning` `Error` `Fatal` |

### Startup validation

The app refuses to start and prints a clear error if:
- `OutputPath` is missing or empty
- `IntervalMinutes` is zero or negative

---

## CLI Commands

### Build

```bash
dotnet build
```

### Run with defaults

```bash
dotnet run --project PowerTradeReportGenerationWorker
```

### Custom output folder

```bash
# Windows
dotnet run --project PowerTradeReportGenerationWorker --ReportSettings:OutputPath "C:\Reports\PowerPosition"

# Linux / macOS
dotnet run --project PowerTradeReportGenerationWorker --ReportSettings:OutputPath "/var/reports/power"
```

### Custom interval

```bash
dotnet run --project PowerTradeReportGenerationWorker --ReportSettings:IntervalMinutes 30
```

### Both at once

```bash
dotnet run --project PowerTradeReportGenerationWorker \
  --ReportSettings:OutputPath "/var/reports/power" \
  --ReportSettings:IntervalMinutes 30
```

### Verbose logging

```bash
dotnet run --project PowerTradeReportGenerationWorker --Serilog:MinimumLevel:Default Debug
```

### Publish a self-contained executable

```bash
# Windows
dotnet publish PowerTradeReportGenerationWorker -c Release -r win-x64 --self-contained

# Linux
dotnet publish PowerTradeReportGenerationWorker -c Release -r linux-x64 --self-contained
```

### Run the published binary

```bash
# Windows
.\PowerPosition.Reporter.exe --ReportSettings:OutputPath "D:\Output" --ReportSettings:IntervalMinutes 10

# Linux
./PowerPosition.Reporter --ReportSettings:OutputPath /mnt/output --ReportSettings:IntervalMinutes 10
```

---

## Output Files

All files are written to the configured `OutputPath` (default: `./output/`).

### CSV Report

```
output/
└── PowerPosition_20260402_1430.csv
```

```csv
Local Time,Volume
23:00,1500
00:00,800
01:00,950
...
22:00,1200
```

- Always contains exactly **24 rows** — one per clock hour
- Time labels are in **Europe/London** local time
- Volumes are rounded to the nearest whole number
- Only written on successful runs

### Audit Log

```
output/
└── logs/
    └── PowerPosition_20260402_1430.log
```

```
2026-04-02 14:30 [INF] ======== Report Extract started at 2026-04-02 14:30 (Europe/London) ========
2026-04-02 14:30 [INF] Trade date resolved to 2026-04-02
2026-04-02 14:30 [INF] Trade 1:
2026-04-02 14:30 [INF] [  100.0 |   50.0 |  -20.0 | ... ]
2026-04-02 14:30 [INF] Aggregation complete: 24 hourly positions produced
2026-04-02 14:30 [INF] === Extract completed successfully. File: PowerPosition_20260402_1430.csv ===
```

- Written for **every run**, including failures
- Log levels: `INF`, `WRN`, `ERR`
- Contains raw per-trade volumes for full auditability

---

## Period → Time Mapping

The trading day starts at **23:00 on the previous calendar day**:

| Period | Local Time | Period | Local Time |
|--------|-----------|--------|-----------|
| 1      | 23:00     | 13     | 11:00     |
| 2      | 00:00     | 14     | 12:00     |
| 3      | 01:00     | 15     | 13:00     |
| 4      | 02:00     | 16     | 14:00     |
| 5      | 03:00     | 17     | 15:00     |
| 6      | 04:00     | 18     | 16:00     |
| 7      | 05:00     | 19     | 17:00     |
| 8      | 06:00     | 20     | 18:00     |
| 9      | 07:00     | 21     | 19:00     |
| 10     | 08:00     | 22     | 20:00     |
| 11     | 09:00     | 23     | 21:00     |
| 12     | 10:00     | 24     | 22:00     |

**Trade date rule:** If the local clock reads **23:00 or later**, the trade date is set to tomorrow — because period 1 (23:00) already belongs to the next trading day.

---

## Running Tests

```bash
# Run all tests
dotnet test

# Detailed output
dotnet test --verbosity normal

# With code coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Test coverage summary

| Test File | Tests | What's Covered |
|---|---|---|
| `PowerPositionReportServiceTests` | 13 | Aggregation logic, all 24 period mappings, full spec example, edge cases |
| `PowerPositionReportWorkerTests` | 13 | Scheduling, trade date resolution, CSV filename format, directory creation |
| `CsvExportServiceTests` | 10 | File output, header row, volume rounding, overwrite behaviour |
| `ReportTimeProviderTests` | 9 | Timezone conversion, DST transitions, clock correctness, invalid input rejection |

---

## Design Principles

| Principle | How it's applied |
|---|---|
| **Fail-safe scheduling** | A failed extract logs the error and the timer continues — the service never crashes |
| **Fail-safe logging** | Errors inside the log writer are swallowed — logging never interrupts an extraction |
| **Always complete output** | All 24 periods are pre-seeded to zero so the CSV is always a full 24-row report |
| **Startup validation** | Bad config causes an immediate, descriptive error at launch — not silently at runtime |
| **Timezone safety** | Europe/London is always enforced regardless of the machine's local timezone setting |
| **Testable by design** | The clock, trading system, CSV writer, and logger are all swappable — fully mockable in tests |
| **Separation of concerns** | Scheduling, aggregation, CSV writing, timezone handling, and logging are all independent |

---

## Dependencies

| Package | Purpose |
|---|---|
| `Microsoft.Extensions.Hosting` | Background service host, dependency injection, configuration |
| `Serilog` + sinks | Structured console and file logging |
| `CsvHelper` | CSV file writing |
| `PowerService.dll` | External power trading system API (pre-built) |
| `xunit` + `Moq` + `FluentAssertions` | Unit testing |

**Requires:** .NET 8 SDK or later

---

## Author

**Karn Kumar** — karn2802@gmail.com  
Internal project for power position reporting and automation.
