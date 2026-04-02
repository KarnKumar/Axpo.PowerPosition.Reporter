using PowerPosition.Reporter.Models;

namespace PowerPosition.Reporter.Services.Csv
{
    /// <summary>
    /// Writes aggregated hourly positions to a CSV file.
    /// </summary>
    public interface ICsvExportService
        {
        /// <summary>
        /// Writes <paramref name="positions"/> to a CSV file named
        /// <c>PowerPosition_YYYYMMDD_HHMM.csv</c> in the configured output folder.
        /// </summary>
        /// <param name="positions">The 24 hourly positions to write.</param>
        /// <param name="extractTime">
        /// The local time of the extract, used to build the filename.
        /// </param>
        /// <returns>The full path of the written CSV file.</returns>
        Task<string> WriteAsync ( IReadOnlyList<PowerTradePosition> positions,
                                DateTime extractTime );
        }
    }
