using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Options;
using PowerPosition.Reporter.Models;
using PowerPosition.Reporter.Services.TimeProvider;
using System.Globalization;
using System.Text;

namespace PowerPosition.Reporter.Services.Csv
{
    /// <summary>
    /// Writes aggregated power positions to a CSV file.
    ///
    /// Output format:
    ///   Local Time,Volume
    ///   23:00,1500
    ///   00:00,1500
    ///   ...
    ///
    /// Filename format: PowerPosition_YYYYMMDD_HHMM.csv
    /// where date/time is the local extract time.
    /// </summary>
    public sealed class CsvExportService ( IOptions<ReportSettings> settings,
                            ILogger<CsvExportService> logger,
                            ITimeProvider timeProvider ) : ICsvExportService
        {
        private readonly ReportSettings _settings = settings.Value;
        private readonly ILogger<CsvExportService> _logger = logger;
        private readonly ITimeProvider _timeProvider = timeProvider;

        /// <inheritdoc />
        public async Task<string> WriteAsync ( IReadOnlyList<PowerTradePosition> positions,
                                             DateTime extractTime )
            {
            var outputDir = _settings.OutputPath;
            Directory.CreateDirectory (outputDir);

            var localExtract = _timeProvider.ToLocalTime(extractTime);

            // ── Build filename: PowerPosition_YYYYMMDD_HHMM.csv ─────────────────
            var fileName = $"PowerPosition_{localExtract:yyyyMMdd_HHmm}.csv";
            var fullPath = Path.Combine(outputDir, fileName);
            var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                HasHeaderRecord = true,
                Encoding        = Encoding.UTF8,
                NewLine         = Environment.NewLine
                };

            await using var writer = new StreamWriter(fullPath, append: false,
                                                  encoding: Encoding.UTF8);
            await using var csv = new CsvWriter(writer, csvConfig);

            // Header row
            csv.WriteField ("Local Time");
            csv.WriteField ("Volume");
            await csv.NextRecordAsync ();

            foreach ( var position in positions )
                {
                csv.WriteField (position.LocalTime);

                var volumeInteger = Math.Round(position.Volume);
                csv.WriteField (volumeInteger.ToString ("0", CultureInfo.InvariantCulture));

                await csv.NextRecordAsync ();
                }

            await writer.FlushAsync ();

            _logger.LogInformation (
                "# Power Position Report Generated successfully! Path : {fullPath}",fullPath);

            return fullPath;
            }
        }
    }
