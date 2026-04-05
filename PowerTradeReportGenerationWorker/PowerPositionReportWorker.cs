using Microsoft.Extensions.Options;
using PowerPosition.Reporter.Services;
using PowerPosition.Reporter.Services.Csv;
using PowerPosition.Reporter.Services.Logging;
using PowerPosition.Reporter.Services.TimeProvider;

namespace PowerPosition.Reporter;

/// <summary>
/// Background worker that runs the power position extract on a schedule.
/// Runs immediately on start, then repeats every <see cref="ReportSettings.IntervalMinutes"/>.
/// </summary>
public sealed class PowerPositionReportWorker (
    IPowerPositionReportService _positionReportService,
    IExtractLoggerFactory _loggerFactory,
    ICsvExportService csvExportService,
    ITimeProvider timeProvider,
    IOptions<ReportSettings> settings,
    ILogger<PowerPositionReportWorker> logger) : BackgroundService
    {

    private readonly IPowerPositionReportService  _positionReportService = _positionReportService;
    private readonly IExtractLoggerFactory         _loggerFactory = _loggerFactory;
    private readonly ICsvExportService            _csvExportService = csvExportService;
    private readonly ReportSettings               _settings = settings.Value;
    private readonly ILogger<PowerPositionReportWorker> _logger = logger;
    private readonly ITimeProvider               _timeProvider = timeProvider;
    protected override async Task ExecuteAsync ( CancellationToken stoppingToken )
        {

          LogWorkerStarted ();

          using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_settings.IntervalMinutes));

        _logger.LogInformation (
            "First extract will run immediately, then every {IntervalMinutes} minute(s).",
            _settings.IntervalMinutes);

        try
            {
            await RunExtractSafeAsync (stoppingToken);

            while ( await timer.WaitForNextTickAsync (stoppingToken) )
                await RunExtractSafeAsync (stoppingToken);
            }
        catch ( OperationCanceledException ) when ( stoppingToken.IsCancellationRequested )
            {
            
            }

        _logger.LogInformation ("Power Position Report Worker stopped gracefully.");
        }

    /// <summary>
    /// Wraps <see cref="RunExtractAsync"/> so a single failed run never stops the scheduler.
    /// Re-throws only on cancellation.
    /// </summary>
    private async Task RunExtractSafeAsync ( CancellationToken stoppingToken)
        {
        try
            {
            await RunExtractAsync (stoppingToken);
            }
        catch ( OperationCanceledException ) when ( stoppingToken.IsCancellationRequested )
            {
            throw;
            }
        catch ( Exception ex )
            {
            _logger.LogError (ex, "Extract failed; scheduler will continue.");
            }
        }

    /// <summary>
    /// Performs one full extract: resolves positions, writes the CSV, and writes a per-run log.
    /// </summary>
    private async Task RunExtractAsync ( CancellationToken stoppingToken )
        {

        var extractLocal = _timeProvider.LocalNow;
        _logger.LogInformation (
            "======== Power Trade Extract started at {ExtractLocalTime:yyyy-MM-dd HH:mm} ========",
            extractLocal);

        // file base name and log file factory are created before the try/catch so we get a log file even if the extract fails early.
        EnsureDirectoriesExist (out var outputDir, out var logsDir);
        var fileBase    = BuildFileBase(extractLocal);
        var runLogFile  = Path.Combine(logsDir, $"{fileBase}.log");

        await using var runLog = _loggerFactory.Create(runLogFile);

        await runLog.WriteAsync ("INF",
            $"======== Report Extract started at {extractLocal:yyyy-MM-dd HH:mm} ({_timeProvider.TimeZoneId}) ========");

        try
            {
            var tradeDate = ResolveTradeDate(extractLocal);
            await runLog.WriteAsync ("INF", $"Trade date resolved to {tradeDate:yyyy-MM-dd}");

            // Service call and aggregation logic are in PowerPositionReportService
            var positions = await _positionReportService.GetAggregatedPositionsAsync(tradeDate,runLog);

            await runLog.WriteAsync ("INF",
                $"Aggregation complete: {positions.Count} hourly positions produced");

            // Create CSV file - Power Position Report.
            await _csvExportService.WriteAsync (positions,fileBase);

            await runLog.WriteAsync ("INF", $"=== Extract completed successfully. File: {fileBase}.csv ===");

            }
        catch ( OperationCanceledException ) when ( stoppingToken.IsCancellationRequested )
            {
            _logger.LogWarning ("Extract cancelled due to shutdown.");
            await runLog.WriteAsync ("WRN", "Extract cancelled due to shutdown.");
            throw;
            }
        catch ( Exception ex )
            {
            _logger.LogError (ex,
     "Extract failed at {ExtractLocalTime:yyyy-MM-dd HH:mm} ({TimeZoneId}). Scheduler will re-run after the configured interval.",
     extractLocal, _timeProvider.TimeZoneId);

            await runLog.WriteAsync ("ERR",
                $"Extract FAILED at {extractLocal:yyyy-MM-dd HH:mm} ({_timeProvider.TimeZoneId}). Exception: {ex}");
            }
        finally
            {
            await runLog.FlushAsync ();
            _logger.LogInformation ("Log file: {RunLogFile}", runLogFile);
            }
        }

    private void LogWorkerStarted ( )
        {
        var localNow = _timeProvider.LocalNow;

        _logger.LogInformation (
            "Power Position Report Worker started. OutputPath={OutputPath}, IntervalMinutes={Interval}",
            _settings.OutputPath, _settings.IntervalMinutes);

        _logger.LogInformation (
            "Timezone = {TimeZoneId}. Time now = {LocalTime:yyyy-MM-dd HH:mm}",
            _timeProvider.TimeZoneId, localNow);
        }

    private void EnsureDirectoriesExist ( out string outputDir, out string logsDir )
        {
        outputDir = _settings.OutputPath;
        logsDir = Path.Combine (outputDir, "logs");

        Directory.CreateDirectory (outputDir);
        Directory.CreateDirectory (logsDir);
        }

    private static string BuildFileBase ( DateTimeOffset extractLocal )
        => $"PowerPosition_{extractLocal:yyyyMMdd_HHmm}";

    /// <summary>
    /// Period 1 starts at 23:00 of the previous calendar day,
    /// so when the local clock reads 23:xx we treat the next calendar date as the trade date.
    /// </summary>
    private static DateTime ResolveTradeDate ( DateTimeOffset extractLocal )
     => extractLocal.Hour >= 23
         ? extractLocal.Date.AddDays (1)
         : extractLocal.Date;

    }