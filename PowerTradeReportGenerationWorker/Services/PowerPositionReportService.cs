using Axpo;
using PowerPosition.Reporter.Models;
using PowerPosition.Reporter.Services.Logging;
using Polly;
using Polly.Retry;

namespace PowerPosition.Reporter.Services
    {

    /// <summary>
    /// Calls the provided PowerService.dll to fetch trades and aggregates
    /// volumes per configured timezone clock hour.
    ///
    /// PERIOD → LOCAL TIME MAPPING
    /// ───────────────────────────
    /// The configured timezone starts at 23:00 (11 pm) the previous day.
    /// Period 1  → 23:00
    /// Period 2  → 00:00
    /// Period 3  → 01:00
    /// ...
    /// Period 24 → 22:00
    public sealed class PowerPositionReportService ( IPowerService powerService,
                                ILogger<PowerPositionReportService> logger) : IPowerPositionReportService
        {

        private readonly IPowerService _powerService = powerService
            ?? throw new ArgumentNullException(nameof(powerService));

        private readonly ILogger<PowerPositionReportService> _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));

        // Retries up to 3 times with exponential backoff: 2s, 4s, 8s
        // Only retries on transient failures — not on business logic exceptions
        private static readonly ResiliencePipeline RetryPipeline =
            new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                    {
                    MaxRetryAttempts = 3,
                    Delay            = TimeSpan.FromSeconds(2),
                    BackoffType      = DelayBackoffType.Exponential,
                    UseJitter        = true,
                    ShouldHandle     = new PredicateBuilder()
                                           .Handle<HttpRequestException>()
                                           .Handle<TaskCanceledException>()
                                           .Handle<TimeoutException>(),
                    OnRetry = args =>
                    {
                        // logged inside ExecuteAsync via _logger — see FetchTradesAsync
                        return ValueTask.CompletedTask;
                    }
                    })
                .Build();

        /// <inheritdoc />
        public async Task<IReadOnlyList<PowerTradePosition>> GetAggregatedPositionsAsync (
            DateTime tradeDate, IExtractLogger runLog )
            {

            var trades = await FetchTradesAsync(tradeDate,runLog);

            await LogTradeVolumesAsync (trades, runLog);

            var aggregated = AggregateByPeriod(trades);

            var positions = MapToPositions(aggregated);

            _logger.LogInformation (
                "Aggregation complete: {Count} hourly positions produced.", positions.Count);

            return positions.AsReadOnly ();
            }
        private async Task<List<PowerTrade>> FetchTradesAsync ( DateTime tradeDate, IExtractLogger runLog )
            {
            _logger.LogInformation (
                "Fetching trades from PowerService for date {TradeDate:yyyy-MM-dd}.", tradeDate);

            var attempt = 0;

            try
                {

                var trades = await RetryPipeline.ExecuteAsync(async ct =>
                {
                    attempt++;
                    if (attempt > 1)
                        {
                        _logger.LogWarning(
                            "Retry attempt {Attempt} of 3 for trade date {TradeDate:yyyy-MM-dd}.",
                            attempt, tradeDate);

                        await runLog.WriteAsync("WRN",
                            $"Retry attempt {attempt} of 3 for {tradeDate:yyyy-MM-dd}.");
                        }

                    return await _powerService.GetTradesAsync(tradeDate);
                });

                var tradeList = trades?.ToList() ?? [];

                _logger.LogInformation (
                    "Received {TradeCount} trade(s) from PowerService.", tradeList.Count);

                  return tradeList;
                }
            catch ( Exception ex )
                {
                _logger.LogError (ex,
                      "Failed to fetch trades for {TradeDate:yyyy-MM-dd} after all retry attempts. " +
                      "No CSV will be written for this run.", tradeDate);

                await runLog.WriteAsync ("ERR",
                    $"Failed to fetch trades for {tradeDate:yyyy-MM-dd} after all retry attempts. No CSV will be written.");
                throw;

                }
            }

        /// <summary>
        /// Iterates every trade and every period, accumulating volumes into a
        /// dictionary keyed by period number (1–24).
        /// All 24 periods are pre-seeded to 0.0 so the CSV always contains a
        /// full 24-row table, even when some periods have no trades.
        /// </summary>
        private Dictionary<int, double> AggregateByPeriod ( List<PowerTrade> trades )
            {
            var aggregated = Enumerable.Range(1, 24).ToDictionary(p => p, _ => 0.0);

            foreach ( var trade in trades )
                {
                if ( trade?.Periods is null )
                    {
                    _logger.LogInformation ("Trade with null Periods encountered skipping.");
                    continue;
                    }

                foreach ( var period in trade.Periods )
                    {
                    if ( period.Period < 1 || period.Period > 24 )
                        {
                        _logger.LogInformation ("Skipping out-of-range period {Period}.", period.Period);
                        continue;
                        }
                    aggregated[period.Period] += period.Volume;
                    }
                }
            return aggregated;
            }

        /// <summary>
        /// Writes every trade's raw period volumes to the per-run log file.
        /// Skips trades with null Periods — they are already warned about in AggregateByPeriod.
        /// </summary>
        private static async Task LogTradeVolumesAsync (
                   List<PowerTrade> trades, IExtractLogger runLog )
            {
            foreach ( var trade in trades.Where (t => t?.Periods is not null) )
                {
                var volumes = trade.Periods
            .OrderBy(p => p.Period)
            .Select(p => p.Volume.ToString("F1").PadLeft(7));

                await runLog.WriteAsync ("INF", $"Trade {trade.TradeId}:");
                await runLog.WriteAsync ("INF", $"[ {string.Join (" | ", volumes)} ]");
                }
            }

        /// <summary>
        /// Converts the aggregated period dictionary into an ordered list of
        /// <see cref="PowerTradePosition"/> records with HH:mm labels.
        /// </summary>
        private static List<PowerTradePosition> MapToPositions ( Dictionary<int, double> aggregated )
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
                }

            return result;
            }
        }
    }