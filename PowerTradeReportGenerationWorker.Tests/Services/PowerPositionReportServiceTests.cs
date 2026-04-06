using Axpo;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PowerPosition.Reporter.Services;
using PowerPosition.Reporter.Services.Logging;
using System.Reflection;

namespace PowerPosition.Reporter.Tests.Services
    {
    public class PowerPositionReportServiceTests
        {
        private readonly Mock<IPowerService> _powerServiceMock;
        private readonly Mock<ILogger<PowerPositionReportService>> _loggerMock;
        private readonly Mock<IExtractLogger> _runLogMock;
        private readonly PowerPositionReportService _service;

        public PowerPositionReportServiceTests ( )
            {
            _powerServiceMock = new Mock<IPowerService> ();
            _loggerMock = new Mock<ILogger<PowerPositionReportService>> ();
            _runLogMock = new Mock<IExtractLogger> ();

            _runLogMock
                .Setup (l => l.WriteAsync (It.IsAny<string> (), It.IsAny<string> ()))
                .Returns (Task.CompletedTask);

            _service = new PowerPositionReportService (
                _powerServiceMock.Object,
                _loggerMock.Object);
            }

        [Fact]
        public async Task GetAggregatedPositionsAsync_ShouldReturn24Positions_WhenNoTradesExist ( )
            {
            var tradeDate = DateTime.Today;
            _powerServiceMock.Setup (s => s.GetTradesAsync (tradeDate)).ReturnsAsync ([]);

            var result = await _service.GetAggregatedPositionsAsync(tradeDate, _runLogMock.Object);

            result.Should ().HaveCount (24);
            result.Should ().OnlyContain (p => p.Volume == 0.0);
            result[0].LocalTime.Should ().Be ("23:00", "period 1  maps to 23:00");
            result[23].LocalTime.Should ().Be ("22:00", "period 24 maps to 22:00");
            }

        [Fact]
        public async Task GetAggregatedPositionsAsync_ShouldCorrectlyAggregateMultipleTrades ( )
            {
            var tradeDate = DateTime.Today;

            var trade1 = PowerTrade.Create(tradeDate, 1);
            SetVolume (trade1, 0, 100.5); // period 1

            var trade2 = PowerTrade.Create(tradeDate, 2);
            SetVolume (trade2, 0, 50.0);  // period 1
            SetVolume (trade2, 1, 200.0); // period 2

            _powerServiceMock
                .Setup (s => s.GetTradesAsync (tradeDate))
                .ReturnsAsync (new List<PowerTrade> { trade1, trade2 });

            var result = await _service.GetAggregatedPositionsAsync(tradeDate, _runLogMock.Object);

            result.First (p => p.LocalTime == "23:00").Volume.Should ().Be (150.5,
                "period 1: trade1(100.5) + trade2(50.0)");
            result.First (p => p.LocalTime == "00:00").Volume.Should ().Be (200.0,
                "period 2: only trade2 contributes");
            }

        [Fact]
        public async Task GetAggregatedPositionsAsync_NegativeVolumesAreAggregatedCorrectly ( )
            {
            var tradeDate = DateTime.Today;
            var trade = PowerTrade.Create(tradeDate, 1);
            SetVolume (trade, 0, -500.0);

            _powerServiceMock
                .Setup (s => s.GetTradesAsync (tradeDate))
                .ReturnsAsync (new List<PowerTrade> { trade });

            var result = await _service.GetAggregatedPositionsAsync(tradeDate, _runLogMock.Object);

            result.First (p => p.LocalTime == "23:00").Volume.Should ().Be (-500.0);
            result.Where (p => p.LocalTime != "23:00").Should ().OnlyContain (p => p.Volume == 0.0);
            }

        [Fact]
        public async Task GetAggregatedPositionsAsync_TradeWithNullPeriods_IsSkippedGracefully ( )
            {
            var tradeDate = DateTime.Today;

            var validTrade = PowerTrade.Create(tradeDate, 1);
            SetVolume (validTrade, 0, 999.0);

            var nullPeriodTrade = PowerTrade.Create(tradeDate, 1);
            SetPeriodsToNull (nullPeriodTrade);

            _powerServiceMock
                .Setup (s => s.GetTradesAsync (tradeDate))
                .ReturnsAsync (new List<PowerTrade> { nullPeriodTrade, validTrade });

            // Must not throw
            var result = await _service.GetAggregatedPositionsAsync(tradeDate, _runLogMock.Object);

            result.Should ().HaveCount (24,
                "null-period trade must be skipped; service must still return 24 rows");
            result.First (p => p.LocalTime == "23:00").Volume.Should ().Be (999.0,
                "the valid trade must still be aggregated");
            }

        [Fact]
        public async Task GetAggregatedPositionsAsync_ShouldThrow_WhenPowerServiceFails ( )
            {
            // Arrange
            var tradeDate = DateTime.Today;
            _powerServiceMock
                .Setup (s => s.GetTradesAsync (tradeDate))
                .ThrowsAsync (new Exception ("Network Error"));

            // Act
            Func<Task> act = async () => await _service.GetAggregatedPositionsAsync(tradeDate, _runLogMock.Object);

            // Assert
            await act.Should ().ThrowAsync<Exception> ().WithMessage ("Network Error");
            }

        [Fact]
        public async Task GetAggregatedPositionsAsync_ShouldThrow_WhenPowerServiceThrowsHttpException ( )
            {
            var tradeDate = DateTime.Today;
            _powerServiceMock
                .Setup (s => s.GetTradesAsync (tradeDate))
                .ThrowsAsync (new HttpRequestException ("503 Service Unavailable"));

            Func<Task> act = () =>
                _service.GetAggregatedPositionsAsync(tradeDate, _runLogMock.Object);

            await act.Should ().ThrowAsync<HttpRequestException> ();
            }

        [Fact]
        public async Task GetAggregatedPositionsAsync_RetriesOnPowerServiceException_BeforeThrowing ( )
            {
            var tradeDate = DateTime.Today;
            var callCount = 0;

            _powerServiceMock
                .Setup (s => s.GetTradesAsync (tradeDate))
                .ReturnsAsync (( ) =>
                {
                    callCount++;
                    throw new PowerServiceException ("Transient error");
                });

            Func<Task> act = () => _service.GetAggregatedPositionsAsync(tradeDate, _runLogMock.Object);

            await act.Should ().ThrowAsync<PowerServiceException> ();
            callCount.Should ().Be (4, "initial attempt + 3 retries = 4 total calls");
            }

        [Fact]
        public async Task GetAggregatedPositionsAsync_ShouldThrow_WhenPowerServiceThrowsTaskCanceled ( )
            {
            var tradeDate = DateTime.Today;
            _powerServiceMock
                .Setup (s => s.GetTradesAsync (tradeDate))
                .ThrowsAsync (new TaskCanceledException ("Request timed out"));

            Func<Task> act = () =>
                _service.GetAggregatedPositionsAsync(tradeDate, _runLogMock.Object);

            await act.Should ().ThrowAsync<TaskCanceledException> ();
            }

        private static void SetVolume ( PowerTrade trade, int periodIndex, double volume )
            {
            var period = trade.Periods[periodIndex];
            var type = period.GetType();

            var field = type.GetField("<Volume>k__BackingField",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? type.GetField("_volume", BindingFlags.NonPublic | BindingFlags.Instance);

            if ( field != null )
                {
                object boxedPeriod = period;
                field.SetValue (boxedPeriod, volume);

                trade.Periods[periodIndex] = ( PowerPeriod ) boxedPeriod;
                }
            else
                {
                throw new Exception ($"Could not find a backing field for Volume in {type.Name}");
                }
            }
        private static void SetPeriodsToNull ( PowerTrade trade )
            {
            var type  = trade.GetType();
            var field = type.GetField("<Periods>k__BackingField",
                            BindingFlags.NonPublic | BindingFlags.Instance)
                     ?? type.GetField("_periods",
                            BindingFlags.NonPublic | BindingFlags.Instance);

            field?.SetValue (trade, null);
            }
        }
    }