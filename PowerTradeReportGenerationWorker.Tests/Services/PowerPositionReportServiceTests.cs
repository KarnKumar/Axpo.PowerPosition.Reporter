using Axpo;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PowerPosition.Reporter.Services;
using PowerPosition.Reporter.Services.Logger;
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

            _service = new PowerPositionReportService (
                _powerServiceMock.Object,
                _loggerMock.Object);
            }

        [Fact]
        public async Task GetAggregatedPositionsAsync_ShouldReturn24Positions_WhenNoTradesExist ( )
            {
            // Arrange
            var tradeDate = DateTime.Today;
            _powerServiceMock
                .Setup (s => s.GetTradesAsync (tradeDate))
                .ReturnsAsync (new List<PowerTrade> ());

            // Act
            var result = await _service.GetAggregatedPositionsAsync(tradeDate, _runLogMock.Object);

            // Assert
            result.Should ().HaveCount (24);
            result.Should ().OnlyContain (p => p.Volume == 0.0);
            result[0].LocalTime.Should ().Be ("23:00"); // Period 1
            result[23].LocalTime.Should ().Be ("22:00"); // Period 24
            }
        
        [Fact]
        public async Task GetAggregatedPositionsAsync_ShouldCorrectlyAggregateMultipleTrades ( )
            {
            // Arrange
            var tradeDate = DateTime.Today;

            // Trade 1: 1 period, Volume = 100.5
            var trade1 = PowerTrade.Create(tradeDate, 1);
            SetVolume (trade1, 0, 100.5); // Use the new helper

            // Trade 2: 2 periods
            var trade2 = PowerTrade.Create(tradeDate, 2);
            SetVolume (trade2, 0, 50.0);
            SetVolume (trade2, 1, 200.0);

            var trades = new List<PowerTrade> { trade1, trade2 };

            _powerServiceMock
                .Setup (s => s.GetTradesAsync (tradeDate))
                .ReturnsAsync (trades);

            // Act
            var result = await _service.GetAggregatedPositionsAsync(tradeDate, _runLogMock.Object);

            // Assert
            result.First (p => p.LocalTime == "23:00").Volume.Should ().Be (150.5);
            }

        [Fact]
        public async Task GetAggregatedPositionsAsync_ShouldReturn24RowsEvenIfEmpty ( )
            {
            // Arrange
            var tradeDate = DateTime.Today;
            _powerServiceMock
                .Setup (s => s.GetTradesAsync (tradeDate))
                .ReturnsAsync (new List<PowerTrade> ());

            // Act
            var result = await _service.GetAggregatedPositionsAsync(tradeDate, _runLogMock.Object);

            // Assert
            result.Should ().HaveCount (24);
            result.All (p => p.Volume == 0).Should ().BeTrue ();
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

        private void SetVolume ( PowerTrade trade, int periodIndex, double volume )
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
        }
    }