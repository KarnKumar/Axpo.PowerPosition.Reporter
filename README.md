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
## Sample Output 
Power Trader Worker Console Window 

<img width="857" height="235" alt="image" src="https://github.com/user-attachments/assets/39244197-f3fc-4e03-a757-2995d4020321" />


Power Trader Worker Console Window ( with some failed Extract attempts)

<img width="1272" height="530" alt="Screenshot 2026-04-02 123509" src="https://github.com/user-attachments/assets/69a8fc77-b537-4b79-b23f-b691514ff3bd" />


Sample Power Trade Report ( Sample csv file : PowerPosition_2026xxx_15xx.csv )

<img width="203" height="533" alt="image" src="https://github.com/user-attachments/assets/1e976f7f-a191-4072-add1-ef6fdc3359bd" />


Sample Power Trade Report log ( Sample csv file : PowerPosition_2026xxx_15xx.log )

<img width="1214" height="607" alt="image" src="https://github.com/user-attachments/assets/083018f6-99f9-45aa-8e55-78f3a96b5beb" />



## Author
Karn Kumar - karn2802@gmail.com ,
Internal project for power position reporting and automation.
