using Axpo;
using PowerPosition.Reporter.Models;
using PowerPosition.Reporter.Services.Logger;

namespace PowerPosition.Reporter.Services
    {

    /// <summary>
    /// Calls the provided PowerService.dll to fetch trades and aggregates
    /// volumes per configured timezone clock hour.
    ///
    /// PERIOD → LOCAL TIME MAPPING
    /// ───────────────────────────
    /// The energy "gas day" for the configured timezone starts at 23:00 (11 pm) the previous day.
    /// Period 1  → 23:00
    /// Period 2  → 00:00
    /// Period 3  → 01:00
    /// ...
    /// Period 24 → 22:00
    ///
    /// Formula: localHour = (22 + periodNumber) % 24
    /// This is DST-safe because we only produce a string label, not a real DateTime.
    /// </summary>
    /// <remarks>
    /// If timeZone is null the system local timezone (TimeZoneInfo.Local) is used.
    /// </remarks>
    public sealed class PowerPositionReportService ( IPowerService powerService,
                                ILogger<PowerPositionReportService> logger) : IPowerPositionReportService
        {

        private readonly IPowerService _powerService = powerService
            ?? throw new ArgumentNullException(nameof(powerService));

        private readonly ILogger<PowerPositionReportService> _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));

        /// <inheritdoc />
        public async Task<IReadOnlyList<PowerTradePosition>> GetAggregatedPositionsAsync (
            DateTime tradeDate, IExtractLogger runLog )
            {

            var trades = await FetchTradesAsync(tradeDate);

            var aggregated = await AggregateByPeriodAsync(trades, runLog);

            var positions = MapToPositions(aggregated);

            _logger.LogInformation (
                "Aggregation complete: {Count} hourly positions produced.", positions.Count);

            return positions.AsReadOnly ();
            }

        private async Task<List<PowerTrade>> FetchTradesAsync ( DateTime tradeDate )
            {
            _logger.LogInformation (
                "Fetching trades from PowerService for date {TradeDate:yyyy-MM-dd}.", tradeDate);

            try
                {
                var trades = await _powerService.GetTradesAsync(tradeDate);

                var tradeList = trades?.ToList() ?? [];

                _logger.LogInformation (
                    "Received {TradeCount} trade(s) from PowerService.", tradeList.Count);

                return tradeList;
                }
            catch ( Exception ex )
                {
                _logger.LogError (ex,
                    "Failed to fetch trades for {TradeDate:yyyy-MM-dd}. " +
                    "No CSV will be written for this run.", tradeDate);
                throw;
                }
            }

        /// <summary>
        /// Iterates every trade and every period, accumulating volumes into a
        /// dictionary keyed by period number (1–24).
        /// All 24 periods are pre-seeded to 0.0 so the CSV always contains a
        /// full 24-row table, even when some periods have no trades.
        /// </summary>
        private async Task<Dictionary<int, double>> AggregateByPeriodAsync (
            List<PowerTrade> trades, IExtractLogger runLog )
            {
            var aggregated = Enumerable.Range(1, 24)
                                       .ToDictionary(p => p, _ => 0.0);

            foreach ( var trade in trades )
                {

                if ( trade?.Periods is null )
                    {
                    _logger.LogWarning ("Trade with null Periods encountered – skipping.");
                    continue;
                    }

                await LogTradeVolumesAsync (trade, runLog);

                foreach ( var period in trade.Periods )
                    {

                    if ( period.Period < 1 || period.Period > 24 )
                        {
                        _logger.LogWarning (
                            "Skipping out-of-range period {Period} on trade {TradeId}.",
                            period.Period, trade.TradeId);
                        continue;
                        }

                    aggregated[period.Period] += period.Volume;
                    }
                }

            return aggregated;
            }

        /// <summary>
        /// Writes a single trade's raw period volumes as a readable bar
        /// into the per-run log file.
        /// Example:  Trade 42: [  100.0 |   50.0 |  -20.0 | ... ]
        /// </summary>
        private static async Task LogTradeVolumesAsync ( PowerTrade trade, IExtractLogger runLog )
            {
            var volumes = trade.Periods
                .OrderBy(p => p.Period)
                .Select(p => p.Volume.ToString("F1").PadLeft(7)); // F1 is to give space padding.

            await runLog.WriteAsync ("INF", $"Trade {trade.TradeId}:");
            await runLog.WriteAsync ("INF", $"[ {string.Join (" | ", volumes)} ]");
            }

        /// <summary>
        /// Converts the aggregated period dictionary into an ordered list of
        /// <see cref="PowerTradePosition"/> records with HH:mm labels.
        /// </summary>
        private List<PowerTradePosition> MapToPositions ( Dictionary<int, double> aggregated )
            {
            var dayStart = new TimeOnly(23, 0);
            var result = new List<PowerTradePosition>(24);

            foreach ( var (periodNumber, totalVolume) in aggregated.OrderBy (p => p.Key) )
                {
                // period 1 → 23:00, period 2 → 00:00 ... period 24 → 22:00
                var    periodTime = dayStart.AddHours(periodNumber - 1);
                string timeLabel  = periodTime.ToString("HH:mm");

                result.Add (new PowerTradePosition
                    {
                    LocalTime = timeLabel,
                    Volume = totalVolume
                    });

                _logger.LogDebug (
                    "Period {Period:D2} → {LocalTime}  Volume = {Volume:F2}",
                    periodNumber, timeLabel, totalVolume);
                }

            return result;
            }
        }
    }