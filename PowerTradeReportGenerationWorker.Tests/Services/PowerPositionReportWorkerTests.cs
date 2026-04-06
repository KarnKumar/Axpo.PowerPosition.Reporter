using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PowerPosition.Reporter.Models;
using PowerPosition.Reporter.Services;
using PowerPosition.Reporter.Services.Csv;
using PowerPosition.Reporter.Services.Logging;
using PowerPosition.Reporter.Services.TimeProvider;

namespace PowerPosition.Reporter.Tests.Services;

public sealed class PowerPositionReportWorkerTests : IDisposable
    {
    private readonly Mock<IPowerPositionReportService>        _positionServiceMock = new();
    private readonly Mock<IExtractLoggerFactory>              _loggerFactoryMock   = new();
    private readonly Mock<IExtractLogger>                     _runLogMock          = new();
    private readonly Mock<ICsvReportService>                  _csvServiceMock      = new();
    private readonly Mock<ITimeProvider>                      _timeProviderMock    = new();
    private readonly Mock<IOptions<ReportSettings>>           _optionsMock         = new();
    private readonly Mock<ILogger<PowerPositionReportWorker>> _appLoggerMock       = new();

    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public PowerPositionReportWorkerTests ( )
        {
        _optionsMock.Setup (o => o.Value).Returns (new ReportSettings
            {
            OutputPath = _tempDir,
            IntervalMinutes = 60
            });

        SetupTimeProvider (new DateTimeOffset (2024, 6, 15, 10, 30, 0, TimeSpan.Zero));

        // Run-log returned by the factory
        _loggerFactoryMock.Setup (f => f.Create (It.IsAny<string> ()))
                          .Returns (_runLogMock.Object);
        _runLogMock.Setup (l => l.WriteAsync (It.IsAny<string> (), It.IsAny<string> ()))
                   .Returns (Task.CompletedTask);
        _runLogMock.Setup (l => l.FlushAsync ()).Returns (Task.CompletedTask);
        _runLogMock.Setup (l => l.DisposeAsync ()).Returns (ValueTask.CompletedTask);

        // Happy-path defaults
        _positionServiceMock
            .Setup (s => s.GetAggregatedPositionsAsync (It.IsAny<DateTime> (), It.IsAny<IExtractLogger> ()))
            .ReturnsAsync (Array.Empty<PowerTradePosition> ());

        _csvServiceMock
            .Setup (c => c.WriteAsync (
                It.IsAny<IReadOnlyList<PowerTradePosition>> (),
                It.IsAny<string> ()))
            .Returns (Task.CompletedTask);
        }

    private void SetupTimeProvider ( DateTimeOffset localNow )
        {
        _timeProviderMock.Setup (t => t.LocalNow).Returns (localNow);
        _timeProviderMock.Setup (t => t.TimeZoneId).Returns ("Europe/London");
        }

    private PowerPositionReportWorker CreateWorker ( ) => new (
        _positionServiceMock.Object,
        _loggerFactoryMock.Object,
        _csvServiceMock.Object,
        _timeProviderMock.Object,
        _optionsMock.Object,
        _appLoggerMock.Object);

    public void Dispose ( )
        {
        if ( Directory.Exists (_tempDir) )
            Directory.Delete (_tempDir, recursive: true);
        }

    [Fact]
    public async Task OnStart_ExtractRunsImmediately_WithoutWaitingForTimerTick ( )
        {
        // Arrange
        var firstExtractFired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _positionServiceMock
            .Setup (s => s.GetAggregatedPositionsAsync (It.IsAny<DateTime> (), It.IsAny<IExtractLogger> ()))
            .ReturnsAsync (Array.Empty<PowerTradePosition> ())
            .Callback<DateTime, IExtractLogger> (( _, _ ) => firstExtractFired.TrySetResult ());

        var worker = CreateWorker();

        // Act
        await worker.StartAsync (CancellationToken.None);
        var winner = await Task.WhenAny(firstExtractFired.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        await worker.StopAsync (CancellationToken.None);

        // Assert
        winner.Should ().Be (firstExtractFired.Task,
            "the extract must fire immediately on startup, not after the 60-minute timer");

        _positionServiceMock.Verify (
            s => s.GetAggregatedPositionsAsync (It.IsAny<DateTime> (), It.IsAny<IExtractLogger> ()),
            Times.AtLeastOnce ());
        }

    [Fact]
    public async Task OnPowerExtractServiceFailure_WorkerSurvivesAndRemainsRunning ( )
        {
        // Arrange
        _positionServiceMock
            .Setup (s => s.GetAggregatedPositionsAsync (It.IsAny<DateTime> (), It.IsAny<IExtractLogger> ()))
            .ThrowsAsync (new InvalidOperationException ("Power service unavailable"));

        var worker = CreateWorker();

        // Act
        await worker.StartAsync (CancellationToken.None);
        await Task.Delay (300);   // enough time for the first (failing) extract to complete

        // Assert
        worker.ExecuteTask.Should ().NotBeNull ();
        worker.ExecuteTask!.IsCompleted.Should ().BeFalse (
            "a failed extract must not propagate and terminate the background service");

        await worker.StopAsync (CancellationToken.None);
        }

    [Fact]
        public async Task TradeDate_WhenLocalHourIs23_ResolvesToNextCalendarDay()
        {
            // Local clock reads 23:05 → period 1 of the NEXT trading day has started
            var localNow = new DateTimeOffset(2024, 6, 15, 23, 5, 0, TimeSpan.Zero);
            SetupTimeProvider(localNow);
 
            DateTime? capturedTradeDate = null;
            var extractDone = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
 
            _positionServiceMock
                .Setup(s => s.GetAggregatedPositionsAsync(
                    It.IsAny<DateTime>(), It.IsAny<IExtractLogger>()))
                .ReturnsAsync(Array.Empty<PowerTradePosition>())
                .Callback<DateTime, IExtractLogger>((date, _) =>
                {
                    capturedTradeDate = date;
                    extractDone.TrySetResult();
                });
 
            var worker = CreateWorker();
 
            await worker.StartAsync(CancellationToken.None);
            await extractDone.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await worker.StopAsync(CancellationToken.None);
 
            capturedTradeDate.Should().Be(localNow.Date.AddDays(1),
                "at 23:xx, period 1 of the next trading day has begun");
        }
    [Fact]
    public async Task TradeDate_WhenLocalHourIs22_ResolvesToSameCalendarDay ( )
        {
        // 22:59 – still within the current trading day
        var localNow = new DateTimeOffset(2024, 6, 15, 22, 59, 0, TimeSpan.Zero);
        SetupTimeProvider (localNow);

        DateTime? capturedTradeDate = null;
        var extractDone = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

        _positionServiceMock
            .Setup (s => s.GetAggregatedPositionsAsync (
                It.IsAny<DateTime> (), It.IsAny<IExtractLogger> ()))
            .ReturnsAsync (Array.Empty<PowerTradePosition> ())
            .Callback<DateTime, IExtractLogger> (( date, _ ) =>
            {
                capturedTradeDate = date;
                extractDone.TrySetResult ();
            });

        var worker = CreateWorker();

        await worker.StartAsync (CancellationToken.None);
        await extractDone.Task.WaitAsync (TimeSpan.FromSeconds (5));
        await worker.StopAsync (CancellationToken.None);

        capturedTradeDate.Should ().Be (localNow.Date,
            "before 23:00, the trading day is still the current calendar day");
        }

    [Fact]
    public async Task MultipleConsecutiveFailures_DoNotStopTheScheduler ( )
        {
        // Every call throws – the scheduler must keep running regardless
        _positionServiceMock
            .Setup (s => s.GetAggregatedPositionsAsync (
                It.IsAny<DateTime> (), It.IsAny<IExtractLogger> ()))
            .ThrowsAsync (new TimeoutException ("Timeout"));

        var worker = CreateWorker();

        await worker.StartAsync (CancellationToken.None);
        await Task.Delay (500);

        worker.ExecuteTask!.IsCompleted.Should ().BeFalse (
            "multiple consecutive failures must never terminate the background service");

        await worker.StopAsync (CancellationToken.None);
        }

    [Fact]
    public async Task OnCancellation_WorkerStopsGracefully_WithoutThrowingException ( )
        {
        var worker = CreateWorker();
        using var cts = new CancellationTokenSource();

        await worker.StartAsync (cts.Token);
        await Task.Delay (200);

        cts.Cancel ();
        Func<Task> act = () => worker.StopAsync(CancellationToken.None);

        await act.Should ().NotThrowAsync (
            "OperationCanceledException from the timer must be caught inside ExecuteAsync");
        }

    [Fact]
    public async Task CsvFileBase_HasCorrectFormat_PowerPositionYYYYMMDD_HHMM ( )
        {
        // Arrange: local clock = 2024-06-15 14:37
        var localNow = new DateTimeOffset(2024, 6, 15, 14, 37, 0, TimeSpan.Zero);
        SetupTimeProvider (localNow);

        string? capturedFileBase = null;
        _csvServiceMock
            .Setup (c => c.WriteAsync (
                It.IsAny<IReadOnlyList<PowerTradePosition>> (),
                It.IsAny<string> ()))
            .Callback<IReadOnlyList<PowerTradePosition>, string> (( _, fb ) =>
                capturedFileBase = fb)
            .Returns (Task.CompletedTask);

        var extractDone = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        _positionServiceMock
            .Setup (s => s.GetAggregatedPositionsAsync (
                It.IsAny<DateTime> (), It.IsAny<IExtractLogger> ()))
            .ReturnsAsync (Array.Empty<PowerTradePosition> ())
            .Callback<DateTime, IExtractLogger> (( _, _ ) => extractDone.TrySetResult ());

        var worker = CreateWorker();
        await worker.StartAsync (CancellationToken.None);
        await extractDone.Task.WaitAsync (TimeSpan.FromSeconds (5));
        await worker.StopAsync (CancellationToken.None);

        capturedFileBase.Should ().NotBeNull ();
        capturedFileBase.Should ().Be ("PowerPosition_20240615_1437",
            "filename must follow the PowerPosition_YYYYMMDD_HHMM format from the spec");
        }

    [Fact]
    public async Task OutputDirectory_IsCreated_IfItDoesNotExist ( )
        {
        // The temp dir itself doesn't exist yet
        var extractDone = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        _positionServiceMock
            .Setup (s => s.GetAggregatedPositionsAsync (
                It.IsAny<DateTime> (), It.IsAny<IExtractLogger> ()))
            .ReturnsAsync (Array.Empty<PowerTradePosition> ())
            .Callback<DateTime, IExtractLogger> (( _, _ ) => extractDone.TrySetResult ());

        var worker = CreateWorker();
        await worker.StartAsync (CancellationToken.None);
        await extractDone.Task.WaitAsync (TimeSpan.FromSeconds (5));
        await worker.StopAsync (CancellationToken.None);

        Directory.Exists (_tempDir).Should ().BeTrue (
            "the worker must create the configured output directory on first run");
        }

    [Fact]
    public async Task LogsSubDirectory_IsCreated_AlongsideOutputDirectory ( )
        {
        var extractDone = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        _positionServiceMock
            .Setup (s => s.GetAggregatedPositionsAsync (
                It.IsAny<DateTime> (), It.IsAny<IExtractLogger> ()))
            .ReturnsAsync (Array.Empty<PowerTradePosition> ())
            .Callback<DateTime, IExtractLogger> (( _, _ ) => extractDone.TrySetResult ());

        var worker = CreateWorker();
        await worker.StartAsync (CancellationToken.None);
        await extractDone.Task.WaitAsync (TimeSpan.FromSeconds (5));
        await worker.StopAsync (CancellationToken.None);

        var logsDir = Path.Combine(_tempDir, "logs");
        Directory.Exists (logsDir).Should ().BeTrue (
            "a 'logs' sub-directory must be created for per-run log files");
        }

    [Fact]
    public async Task ExtractLogFile_IsAlwaysCreated_EvenWhenExtractFails ( )
        {
        _positionServiceMock
            .Setup (s => s.GetAggregatedPositionsAsync (
                It.IsAny<DateTime> (), It.IsAny<IExtractLogger> ()))
            .ThrowsAsync (new Exception ("Deliberate failure"));

        var extractAttempted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        _runLogMock
            .Setup (l => l.WriteAsync (It.IsAny<string> (), It.IsAny<string> ()))
            .Callback (( ) => extractAttempted.TrySetResult ())
            .Returns (Task.CompletedTask);

        var worker = CreateWorker();
        await worker.StartAsync (CancellationToken.None);
        await extractAttempted.Task.WaitAsync (TimeSpan.FromSeconds (5));
        await worker.StopAsync (CancellationToken.None);

        _loggerFactoryMock.Verify (
            f => f.Create (It.IsAny<string> ()),
            Times.AtLeastOnce (),
            "the per-run log file must be created even when the extract fails");
        }
    [Fact]
    public async Task OnSuccessfulExtract_PositionsArePassedToCsvService ( )
        {
        var expectedPositions = new[]
            {
                new PowerTradePosition { LocalTime = "23:00", Volume = 150 },
                new PowerTradePosition { LocalTime = "00:00", Volume = 150 }
            };

        _positionServiceMock
            .Setup (s => s.GetAggregatedPositionsAsync (
                It.IsAny<DateTime> (), It.IsAny<IExtractLogger> ()))
            .ReturnsAsync (expectedPositions);

        IReadOnlyList<PowerTradePosition>? capturedPositions = null;
        var extractDone = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

        _csvServiceMock
            .Setup (c => c.WriteAsync (
                It.IsAny<IReadOnlyList<PowerTradePosition>> (),
                It.IsAny<string> ()))
            .Callback<IReadOnlyList<PowerTradePosition>, string> (( positions, _ ) =>
            {
                capturedPositions = positions;
                extractDone.TrySetResult ();
            })
            .Returns (Task.CompletedTask);

        var worker = CreateWorker();
        await worker.StartAsync (CancellationToken.None);
        await extractDone.Task.WaitAsync (TimeSpan.FromSeconds (5));
        await worker.StopAsync (CancellationToken.None);

        capturedPositions.Should ().NotBeNull ();
        capturedPositions.Should ().BeEquivalentTo (expectedPositions,
            "the worker must forward exactly the positions returned by the report service to CSV");
        }
    }

