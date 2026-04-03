using PowerPosition.Reporter.Models;

namespace PowerPosition.Reporter.Services.Csv
{
    /// <summary>
    /// Writes aggregated hourly positions to a CSV file.
    /// </summary>
    public interface ICsvExportService
        {
        /// <summary>
        /// WriteAsync writes the given positions to a CSV file in the configured output directory.
        /// </summary>
        /// <param name="positions"></param>
        /// <param name="fileName"></param>
        /// <returns></returns>
        Task WriteAsync ( IReadOnlyList<PowerTradePosition> positions, string fileName);
        }
    }

