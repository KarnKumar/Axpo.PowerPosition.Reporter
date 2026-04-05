using FluentAssertions;
using PowerPosition.Reporter.Services.TimeProvider;

namespace PowerPosition.Reporter.Tests.Services
    {
    /// <summary>
    /// Unit tests for <see cref="ReportTimeProvider"/>.
    ///   • LocalNow: returns current time in Europe/London, not machine-local
    /// </summary>
    public sealed class ReportTimeProviderTests
        {
        // Europe/London is the required timezone per the spec.
        private static readonly TimeZoneInfo LondonTz = TryFindTimeZone(
            "Europe/London", "GMT Standard Time");

        private static TimeZoneInfo TryFindTimeZone ( params string[] ids )
            {
            foreach ( var id in ids )
                {
                try { return TimeZoneInfo.FindSystemTimeZoneById (id); }
                catch ( TimeZoneNotFoundException ) { /* try next */ }
                }
            throw new TimeZoneNotFoundException (
                $"None of the attempted timezone IDs were found: {string.Join (", ", ids)}");
            }

        [Fact]
        public void Constructor_NullTimeZone_ThrowsArgumentNullException ( )
            {
            Action act = () => _ = new ReportTimeProvider(null!);
            act.Should ().Throw<ArgumentNullException> ()
                .WithParameterName ("timeZone");
            }

        [Fact]
        public void TimeZoneId_ReturnsConfiguredTimeZoneId ( )
            {
            var provider = new ReportTimeProvider(LondonTz);
            provider.TimeZoneId.Should ().Be (LondonTz.Id);
            }

        [Fact]
        public void LocalNow_IsInEuropeLondonTimezone ( )
            {
            var provider = new ReportTimeProvider(LondonTz);

            var before = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, LondonTz);
            var localNow = provider.LocalNow;
            var after  = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, LondonTz);

            localNow.Should ().BeOnOrAfter (before, "LocalNow must not be earlier than now");
            localNow.Should ().BeOnOrBefore (after, "LocalNow must not be later than now");
            }

        [Fact]
        public void LocalNow_OffsetMatchesEuropeLondon ( )
            {
            var provider = new ReportTimeProvider(LondonTz);
            var localNow = provider.LocalNow;
            var expectedOffset = LondonTz.GetUtcOffset(DateTimeOffset.UtcNow);

            localNow.Offset.Should ().Be (expectedOffset,
                "the returned DateTimeOffset must carry the Europe/London UTC offset, " +
                "not the machine-local offset");
            }

        [Fact]
        public void ToLocalTime_ConvertsUtcToLondonTime_WinterTime_OffsetZero ( )
            {
            // 15 Jan 2024 12:00 UTC → London is UTC+0 in winter → 12:00 local
            var utcTime = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc);
            var provider = new ReportTimeProvider(LondonTz);

            var local = provider.ToLocalTime(utcTime);

            local.Hour.Should ().Be (12,
                "in winter (UTC+0) London time equals UTC");
            local.Minute.Should ().Be (0);
            }

        [Fact]
        public void ToLocalTime_ConvertsUtcToLondonTime_SummerTime_OffsetPlusOne ( )
            {
            // 15 Jul 2024 12:00 UTC → London is UTC+1 in summer (BST) → 13:00 local
            var utcTime = new DateTime(2024, 7, 15, 12, 0, 0, DateTimeKind.Utc);
            var provider = new ReportTimeProvider(LondonTz);

            var local = provider.ToLocalTime(utcTime);

            local.Hour.Should ().Be (13,
                "in summer (BST = UTC+1) London time is one hour ahead of UTC");
            }

        [Fact]
        public void ToLocalTime_PreservesDateComponents_AfterConversion ( )
            {
            // 31 Dec 2023 23:30 UTC → in London (UTC+0 in winter) = still 31 Dec 2023
            var utcTime = new DateTime(2023, 12, 31, 23, 30, 0, DateTimeKind.Utc);
            var provider = new ReportTimeProvider(LondonTz);

            var local = provider.ToLocalTime(utcTime);

            local.Year.Should ().Be (2023);
            local.Month.Should ().Be (12);
            local.Day.Should ().Be (31);
            }

        [Fact]
        public void ToLocalTime_MidnightCrossing_SummerTime ( )
            {
            // 30 Jun 2024 23:30 UTC → BST (UTC+1) → 00:30 on 1 Jul
            var utcTime = new DateTime(2024, 6, 30, 23, 30, 0, DateTimeKind.Utc);
            var provider = new ReportTimeProvider(LondonTz);

            var local = provider.ToLocalTime(utcTime);

            local.Day.Should ().Be (1, "BST offset pushes the time past midnight");
            local.Month.Should ().Be (7);
            local.Hour.Should ().Be (0);
            local.Minute.Should ().Be (30);
            }

        [Fact]
        public void ToLocalTime_LocalKind_ThrowsArgumentException ( )
            {
            var localTime = new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Local);
            var provider  = new ReportTimeProvider(LondonTz);

            Action act = () => provider.ToLocalTime(localTime);

            act.Should ().Throw<ArgumentException> ()
                .WithParameterName ("dateTime")
                .WithMessage ("*Kind=Local*");
            }

        [Fact]
        public void ToLocalTime_UnspecifiedKind_ThrowsArgumentException ( )
            {
            var unspecifiedTime = new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Unspecified);
            var provider        = new ReportTimeProvider(LondonTz);

            Action act = () => provider.ToLocalTime(unspecifiedTime);

            act.Should ().Throw<ArgumentException> ()
                .WithParameterName ("dateTime")
                .WithMessage ("*Kind=Unspecified*");
            }
        }
    }