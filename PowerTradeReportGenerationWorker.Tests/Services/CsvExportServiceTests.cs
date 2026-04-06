using CsvHelper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PowerPosition.Reporter.Models;
using PowerPosition.Reporter.Services.Csv;
using System.Globalization;

namespace PowerPosition.Reporter.Tests.Services
    {
    /// <summary>
    /// Unit tests for <see cref="CsvExportService"/>.
    /// </summary>
    public sealed class CsvExportServiceTests : IDisposable
        {
        private readonly string _tempDir =
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        private readonly Mock<ILogger<CsvExportService>> _loggerMock = new();

        public CsvExportServiceTests ( )
            {
            Directory.CreateDirectory (_tempDir);
            }

        public void Dispose ( )
            {
            if ( Directory.Exists (_tempDir) )
                Directory.Delete (_tempDir, recursive: true);
            }

        private ICsvReportService CreateService ( string? outputPath = null )
            {
            var settings = new ReportSettings
                {
                OutputPath      = outputPath ?? _tempDir,
                IntervalMinutes = 15
                };

            var optionsMock = new Mock<IOptions<ReportSettings>>();
            optionsMock.Setup (o => o.Value).Returns (settings);

            return new CsvExportService (optionsMock.Object, _loggerMock.Object);
            }


        [Fact]
        public async Task WriteAsync_WritesHeaderRow_LocalTimeAndVolume ( )
            {
            var service  = CreateService();
            var fileBase = "PowerPosition_20240615_1430";
            var filePath = Path.Combine(_tempDir, $"{fileBase}.csv");

            await service.WriteAsync ([], fileBase);

            var lines = await File.ReadAllLinesAsync(filePath);
            lines.Should ().NotBeEmpty ();
            lines[0].Should ().Be ("Local Time,Volume",
                "the first row must be the header as required by the spec");
            }


        [Fact]
        public async Task WriteAsync_WritesCorrectDataRows_LocalTimeAndRoundedVolume ( )
            {
            var service  = CreateService();
            var fileBase = "PowerPosition_20240615_1430";
            var filePath = Path.Combine(_tempDir, $"{fileBase}.csv");

            var positions = new[]
            {
                new PowerTradePosition { LocalTime = "23:00", Volume = 1500.0 },
                new PowerTradePosition { LocalTime = "00:00", Volume = 80.0   }
            };

            await service.WriteAsync (positions, fileBase);

            var lines = await File.ReadAllLinesAsync(filePath);
            lines.Should ().HaveCount (3, "1 header + 2 data rows");
            lines[1].Should ().Be ("23:00,1500");
            lines[2].Should ().Be ("00:00,80");
            }

        [Fact]
        public async Task WriteAsync_EmptyPositions_WritesHeaderOnly ( )
            {
            var service  = CreateService();
            var fileBase = "PowerPosition_20240615_1430";
            var filePath = Path.Combine(_tempDir, $"{fileBase}.csv");

            await service.WriteAsync ([], fileBase);

            var lines = await File.ReadAllLinesAsync(filePath);
            // Header + possible empty trailing line
            lines.Where (l => l.Length > 0).Should ().HaveCount (1,
                "an empty positions list must produce a header-only CSV");
            }

        [Fact]
        public async Task WriteAsync_WritesAllTwentyFourRows_WhenFullDayProvided ( )
            {
            var service  = CreateService();
            var fileBase = "PowerPosition_20240615_1430";
            var filePath = Path.Combine(_tempDir, $"{fileBase}.csv");

            // Build a full 24-row day
            var times = new[]
            {
                "23:00","00:00","01:00","02:00","03:00","04:00",
                "05:00","06:00","07:00","08:00","09:00","10:00",
                "11:00","12:00","13:00","14:00","15:00","16:00",
                "17:00","18:00","19:00","20:00","21:00","22:00"
            };
            var positions = times
                .Select(t => new PowerTradePosition { LocalTime = t, Volume = 100 })
                .ToArray();

            await service.WriteAsync (positions, fileBase);

            var lines = await File.ReadAllLinesAsync(filePath);
            lines.Count (l => l.Length > 0).Should ().Be (25,
                "1 header + 24 data rows for a full trading day");
            }

        [Theory]
        [InlineData (150.5, "151")]   // rounds up
        [InlineData (150.4, "150")]   // rounds down
        [InlineData (0.0, "0")]     // zero is written as "0"
        [InlineData (-80.0, "-80")]   // negative volumes are preserved
        [InlineData (-0.6, "-1")]    // negative rounds away
        [InlineData (1499.9, "1500")]  // large value rounds correctly
        public async Task WriteAsync_VolumeIsRoundedToNearestInteger (
            double rawVolume, string expectedCsvValue )
            {
            var service  = CreateService();
            var fileBase = $"test_volume_{Guid.NewGuid():N}";
            var filePath = Path.Combine(_tempDir, $"{fileBase}.csv");

            var positions = new[]
            {
                new PowerTradePosition { LocalTime = "23:00", Volume = rawVolume }
            };

            await service.WriteAsync (positions, fileBase);

            var lines = await File.ReadAllLinesAsync(filePath);
            lines[1].Should ().Be ($"23:00,{expectedCsvValue}",
                $"volume {rawVolume} should be rounded to {expectedCsvValue}");
            }

        [Fact]
        public async Task WriteAsync_CreatesFileWithCorrectName_BasedOnFileBaseParameter ( )
            {
            var service  = CreateService();
            var fileBase = "PowerPosition_20141220_1837";   // from the spec example
            var filePath = Path.Combine(_tempDir, $"{fileBase}.csv");

            await service.WriteAsync ([], fileBase);

            File.Exists (filePath).Should ().BeTrue (
                "the CSV must be written to OutputPath/{fileBase}.csv");
            }

        [Fact]
        public async Task WriteAsync_OverwritesExistingFile_NotAppends ( )
            {
            var service  = CreateService();
            var fileBase = "PowerPosition_20240615_1430";
            var filePath = Path.Combine(_tempDir, $"{fileBase}.csv");

            var firstPositions = new[]
            {
                new PowerTradePosition { LocalTime = "23:00", Volume = 999 }
            };
            await service.WriteAsync (firstPositions, fileBase);

            var secondPositions = new[]
            {
                new PowerTradePosition { LocalTime = "23:00", Volume = 42 }
            };
            await service.WriteAsync (secondPositions, fileBase);

            var lines = await File.ReadAllLinesAsync(filePath);
            lines.Should ().HaveCount (2,
                "the second write must overwrite, not append to the first write");
            lines[1].Should ().Be ("23:00,42",
                "only the second write's data must be present in the file");
            }

        [Fact]
        public async Task WriteAsync_ThrowsDirectoryNotFoundException_WhenOutputPathMissing ( )
            {
            var nonExistentDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var service  = CreateService(outputPath: nonExistentDir);
            var fileBase = "PowerPosition_20240615_1430";

            Func<Task> act = () => service.WriteAsync([], fileBase);

            await act.Should ().ThrowAsync<DirectoryNotFoundException> (
                "CsvExportService must not silently swallow DirectoryNotFoundException – " +
                "the worker is responsible for ensuring the directory exists beforehand");
            }

        [Fact]
        public async Task WriteAsync_SpecExampleOutput_MatchesRequiredFormat ( )
            {
            // These are the expected volumes from the requirements document example
            var positions = new[]
            {
                new PowerTradePosition { LocalTime = "23:00", Volume = 1500 },
                new PowerTradePosition { LocalTime = "00:00", Volume = 1500 },
                new PowerTradePosition { LocalTime = "01:00", Volume = 1500 },
                new PowerTradePosition { LocalTime = "02:00", Volume = 1500 },
                new PowerTradePosition { LocalTime = "03:00", Volume = 1500 },
                new PowerTradePosition { LocalTime = "04:00", Volume = 1500 },
                new PowerTradePosition { LocalTime = "05:00", Volume = 1500 },
                new PowerTradePosition { LocalTime = "06:00", Volume = 1500 },
                new PowerTradePosition { LocalTime = "07:00", Volume = 1500 },
                new PowerTradePosition { LocalTime = "08:00", Volume = 1500 },
                new PowerTradePosition { LocalTime = "09:00", Volume = 1500 },
                new PowerTradePosition { LocalTime = "10:00", Volume = 1500 },
                new PowerTradePosition { LocalTime = "11:00", Volume = 80   },
                new PowerTradePosition { LocalTime = "12:00", Volume = 80   },
                new PowerTradePosition { LocalTime = "13:00", Volume = 80   },
                new PowerTradePosition { LocalTime = "14:00", Volume = 80   },
                new PowerTradePosition { LocalTime = "15:00", Volume = 80   },
                new PowerTradePosition { LocalTime = "16:00", Volume = 80   },
                new PowerTradePosition { LocalTime = "17:00", Volume = 80   },
                new PowerTradePosition { LocalTime = "18:00", Volume = 80   },
                new PowerTradePosition { LocalTime = "19:00", Volume = 80   },
                new PowerTradePosition { LocalTime = "20:00", Volume = 80   },
                new PowerTradePosition { LocalTime = "21:00", Volume = 80   },
                new PowerTradePosition { LocalTime = "22:00", Volume = 80   }
            };

            var service  = CreateService();
            var fileBase = "PowerPosition_20150401_0000";
            var filePath = Path.Combine(_tempDir, $"{fileBase}.csv");

            await service.WriteAsync (positions, fileBase);

            var lines = await File.ReadAllLinesAsync(filePath);
            lines.Count (l => l.Length > 0).Should ().Be (25, "1 header + 24 data rows");
            lines[0].Should ().Be ("Local Time,Volume");

            // Spot-check a selection from each group
            lines[1].Should ().Be ("23:00,1500");
            lines[12].Should ().Be ("10:00,1500");
            lines[13].Should ().Be ("11:00,80");
            lines[24].Should ().Be ("22:00,80");
            }

        [Fact]
        public async Task WriteAsync_OutputIsValidCsv_ParseableByStandardReader ( )
            {
            var service  = CreateService();
            var fileBase = "PowerPosition_20240615_1430";
            var filePath = Path.Combine(_tempDir, $"{fileBase}.csv");

            var positions = new[]
            {
                new PowerTradePosition { LocalTime = "23:00", Volume = 1500 },
                new PowerTradePosition { LocalTime = "00:00", Volume = 80   }
            };

            await service.WriteAsync (positions, fileBase);

            // Parse with CsvHelper to confirm the file is well-formed
            using var reader = new StreamReader(filePath);
            using var csv    = new CsvReader(reader, CultureInfo.InvariantCulture);

            Func<List<dynamic>> readAll = () =>
            {
                csv.Read(); csv.ReadHeader();
                var records = new List<dynamic>();
                while (csv.Read()) records.Add(csv.GetRecord<dynamic>()!);
                return records;
            };

            readAll.Should ().NotThrow ("the produced CSV must be parseable by a standard CSV reader");
            }
        }
    }