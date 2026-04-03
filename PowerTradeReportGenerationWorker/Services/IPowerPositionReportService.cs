using PowerPosition.Reporter.Models;
using PowerPosition.Reporter.Services.Logging;
namespace PowerPosition.Reporter.Services
    {

    /// <summary>
    /// Retrieves and aggregates power trade positions from the trading system.
    /// </summary>
    public interface IPowerPositionReportService
        {
        /// <summary>
        /// Returns 24 hourly positions for the given trading date,
        /// aggregated across all trades and mapped to Europe/London local time.
        /// </summary>
        /// <param name="tradeDate">
        /// The calendar date for which to retrieve positions.
        /// Period 1 of this date begins at 23:00 on the PREVIOUS day.
        /// </param>
        Task<IReadOnlyList<PowerTradePosition>> GetAggregatedPositionsAsync ( DateTime tradeDate, IExtractLogger runLog );

        }
    }

