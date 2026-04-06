using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Options;
using PowerPosition.Reporter.Models;
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
                            ILogger<CsvExportService> logger ) : ICsvReportService
        {
        private readonly ReportSettings _settings = settings.Value;
        private readonly ILogger<CsvExportService> _logger = logger;

        /// <inheritdoc />
        public async Task WriteAsync ( IReadOnlyList<PowerTradePosition> positions, string fileBase )
            {
            var filePath = Path.Combine(_settings.OutputPath, $"{fileBase}.csv");

            try
                {
                var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
                    {
                    HasHeaderRecord = true,
                    Encoding        = Encoding.UTF8,
                    NewLine         = Environment.NewLine
                    };

                await using var writer = new StreamWriter(filePath, append: false, encoding: Encoding.UTF8);
                await using var csv    = new CsvWriter(writer, csvConfig);

                csv.WriteField ("Local Time");
                csv.WriteField ("Volume");
                await csv.NextRecordAsync ();

                foreach ( var position in positions )
                    {
                    csv.WriteField (position.LocalTime);
                    var volumeInteger = Math.Round(position.Volume, MidpointRounding.AwayFromZero); // Round to nearest whole number for CSV output
                    csv.WriteField (volumeInteger.ToString ("0", CultureInfo.InvariantCulture));
                    await csv.NextRecordAsync ();
                    }

                await writer.FlushAsync ();

                _logger.LogInformation ("Power Position Report generated successfully." +
                    " Path: {FullPath}", filePath);
                }
            catch ( DirectoryNotFoundException ex )
                {
                _logger.LogError (ex, "Output directory not found for {FilePath}", filePath);
                throw;
                }
            catch ( IOException ex )
                {
                _logger.LogError (ex, "I/O error writing CSV to {FilePath}", filePath);
                throw;
                }
            catch ( Exception ex )
                {
                _logger.LogError (ex, "Unexpected error writing CSV to {FilePath}", filePath);
                throw;
                }
            }
        }
    }
            
        
