# PowerPosition.Reporter

## Overview

PowerPosition.Reporter is a .NET background worker application designed to extract, aggregate, and export power trading positions on a scheduled basis. The system generates CSV reports and detailed log files for each execution.

---

## Features

* Scheduled background execution using `BackgroundService`
* Immediate run on startup, followed by interval-based execution
* Aggregation of power trade positions
* CSV export of hourly positions
* Per-run log file generation
* Clean separation of concerns (Services, Logging, Time Provider)
* Test coverage for core services and worker

---

## Project Structure

```
src/
  PowerPosition.Reporter/
    Models/                  # Domain models
    Services/
      Csv/                   # CSV export logic
      Logging/               # File-based logging services
      TimeProvider/          # Time abstraction
    PowerPositionReportWorker.cs
    PowerPositionReportService.cs
    ReportSettings.cs
    Program.cs

tests/
  PowerPosition.Reporter.Tests/
    Services/                # Unit tests
```

---

## How It Works

1. Worker starts and logs startup information
2. Runs immediately, then repeats based on configured interval
3. For each run:

   * Resolves trade date
   * Fetches aggregated positions
   * On success:

     * Writes CSV file
     * Writes log file
   * On failure:

     * Writes log file only

---

## Configuration

Configuration is managed via `appsettings.json`.

### Example:

```json
{
  "ReportSettings": {
    "OutputPath": "output",
    "IntervalMinutes": 2
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

### Settings

| Key                            | Description                              |
| ------------------------------ | ---------------------------------------- |
| ReportSettings:OutputPath      | Directory where CSV and logs are written |
| ReportSettings:IntervalMinutes | Execution interval in minutes            |

### Notes

* `OutputPath` can be overridden via CLI:

  ```bash
  --Reporting:OutputPath "C:\Reports"
  ```

* `IntervalMinutes` can be overridden via CLI:

  ```bash
  --Reporting:IntervalMinutes 30
  ```

* Serilog controls application-wide logging levels (console or future sinks).

-----|------------|
| OutputPath | Directory where CSV and logs are written |
| IntervalMinutes | Execution interval in minutes |

---

## Logging

* Each execution creates a dedicated log file
* Log files are stored under:

```
/output/logs/
```

* Log format:

```
YYYY-MM-DD HH:mm [LEVEL] Message
```

---

## CSV Output

* CSV files are generated only on successful extraction
* File naming format:

```
PowerPosition_yyyyMMdd_HHmm.csv
```

---

## Running the Application

### Prerequisites

* .NET 6 or later

### Run

```bash
dotnet run --project src/PowerPosition.Reporter
```

---

## Testing

Run unit tests using:

```bash
dotnet test
```

---

## Design Principles

* Separation of concerns
* Dependency injection
* Resilient background processing
* Fail-safe logging (logging never breaks execution)

---

## Future Improvements

* Retry policies for transient failures
* Structured logging (e.g., Serilog)
* Metrics and monitoring
* Configurable file naming strategy

---

## Author
Karn Kumar - karn2802@gmail.com ,
Internal project for power position reporting and automation.
