
namespace PowerPosition.Reporter
    {
    /// <summary>
    /// Strongly-typed configuration bound from appsettings.json section "ReportSettings"
    /// or overridden via CLI args (--ReportSettings:OutputPath / --ReportSettings:IntervalMinutes).
    /// </summary>
    public sealed class ReportSettings
        {
        /// <summary>
        /// Example: "C:\\Reports\\PowerPosition" or "/var/reports"
        /// </summary>
        public required string OutputPath { get; set; }

        /// <summary>
        /// How often (in minutes) the extract runs after the initial run on startup.
        /// Must be a positive integer. Accuracy is within +/- 1 minute.
        /// </summary>
        public int IntervalMinutes { get; set; }
        }
    }
