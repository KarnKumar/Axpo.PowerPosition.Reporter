using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PowerPosition.Reporter.Models;
using PowerPosition.Reporter.Services;
using PowerPosition.Reporter.Services.Csv;
using PowerPosition.Reporter.Services.Logger;
using PowerPosition.Reporter.Services.Logging;
using PowerPosition.Reporter.Services.TimeProvider;

namespace PowerPosition.Reporter.Tests;

public sealed class PowerPositionReportWorkerTests : IDisposable
    {
    private readonly Mock<IPowerPositionReportService>        _positionServiceMock = new();
    private readonly Mock<IExtractLoggerFactory>              _loggerFactoryMock   = new();
    private readonly Mock<IExtractLogger>                     _runLogMock          = new();
    private readonly Mock<ICsvExportService>                  _csvServiceMock      = new();
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
                It.IsAny<DateTime> ()))
            .ReturnsAsync (Path.Combine (_tempDir, "PowerPosition_20240615_1030.csv"));
        }

    private void SetupTimeProvider ( DateTimeOffset localNow )
        {
        _timeProviderMock.Setup (t => t.LocalNow).Returns (localNow);
        _timeProviderMock.Setup (t => t.TimeZoneId).Returns ("UTC");
        _timeProviderMock.Setup (t => t.ToString ()).Returns ("UTC (Coordinated Universal Time)");
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
    public async Task OnExtractFailure_WorkerSurvivesAndRemainsRunning ( )
        {
        // Arrange
        _positionServiceMock
            .Setup (s => s.GetAggregatedPositionsAsync (It.IsAny<DateTime> (), It.IsAny<IExtractLogger> ()))
            .ThrowsAsync (new InvalidOperationException ("Axle service unavailable"));

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
    public async Task OnCancellation_WorkerStopsGracefully_WithoutThrowingException ( )
        {
        // Arrange
        var worker = CreateWorker();
        using var cts = new CancellationTokenSource();

        await worker.StartAsync (cts.Token);
        await Task.Delay (200);    // let the first extract complete

        // Act
        cts.Cancel ();
        var act = async () => await worker.StopAsync(CancellationToken.None);

        // Assert
        await act.Should ().NotThrowAsync (
            "OperationCanceledException from the timer must be caught inside ExecuteAsync");
        }

    [Fact]
    public async Task TradeDate_WhenLocalHourIs23_ResolvesToNextCalendarDay ( )
        {
        // Arrange – clock at 23:05
        var localNow = new DateTimeOffset(2024, 6, 15, 23, 05, 0, TimeSpan.Zero);
        SetupTimeProvider (localNow);

        DateTime? capturedTradeDate = null;
        var extractDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _positionServiceMock
            .Setup (s => s.GetAggregatedPositionsAsync (It.IsAny<DateTime> (), It.IsAny<IExtractLogger> ()))
            .ReturnsAsync (Array.Empty<PowerTradePosition> ())
            .Callback<DateTime, IExtractLogger> (( date, _ ) =>
            {
                capturedTradeDate = date;
                extractDone.TrySetResult ();
            });

        var worker = CreateWorker();

        // Act
        await worker.StartAsync (CancellationToken.None);
        await extractDone.Task.WaitAsync (TimeSpan.FromSeconds (5));
        await worker.StopAsync (CancellationToken.None);

        // Assert
        var expectedTradeDate = localNow.Date.AddDays(1); // 2024-06-16
        capturedTradeDate.Should ().Be (expectedTradeDate,
            "when the clock reads 23:xx, period 1 belongs to the following trading day");
        }

    [Fact]
    public async Task RunExtract_WritesCsv_WithExactPositionsReturnedByService ( )
        {
        // Arrange
        var positions = new PowerTradePosition[]
    {
        new() { LocalTime = "23:00", Volume = 100.0 },
        new() { LocalTime = "00:00", Volume = 200.0 }
    };

        var csvWritten = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _positionServiceMock
            .Setup (s => s.GetAggregatedPositionsAsync (It.IsAny<DateTime> (), It.IsAny<IExtractLogger> ()))
            .ReturnsAsync (positions);

        _csvServiceMock
            .Setup (c => c.WriteAsync (It.IsAny<IReadOnlyList<PowerTradePosition>> (), It.IsAny<DateTime> ()))
            .ReturnsAsync (Path.Combine (_tempDir, "out.csv"))
            .Callback (( ) => csvWritten.TrySetResult ());

        var worker = CreateWorker();

        // Act
        await worker.StartAsync (CancellationToken.None);
        await csvWritten.Task.WaitAsync (TimeSpan.FromSeconds (5));
        await worker.StopAsync (CancellationToken.None);

        // Assert
        _csvServiceMock.Verify (
            c => c.WriteAsync (
                It.Is<IReadOnlyList<PowerTradePosition>> (list =>
                    list.Count == 2 &&
                    list[0].LocalTime == "23:00" &&
                    list[0].Volume == 100.0 &&
                    list[1].LocalTime == "00:00" &&
                    list[1].Volume == 200.0),
                It.IsAny<DateTime> ()),
            Times.Once (),
            "CSV must be written with the exact aggregated positions, unmodified");
        }
    }
