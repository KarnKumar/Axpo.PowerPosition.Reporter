namespace PowerPosition.Reporter.Services.TimeProvider
    {
    public sealed class ReportTimeProvider ( TimeZoneInfo timeZone ) : ITimeProvider
        {

        private readonly TimeZoneInfo _timeZone = timeZone
        ?? throw new ArgumentNullException(nameof(timeZone));

        public DateTimeOffset LocalNow
         => TimeZoneInfo.ConvertTime (DateTimeOffset.UtcNow, _timeZone);
        public string TimeZoneId => _timeZone.Id;

        public DateTime ToLocalTime ( DateTime dateTime )
            {
            return dateTime.Kind == DateTimeKind.Utc
                ? TimeZoneInfo.ConvertTimeFromUtc (dateTime, _timeZone)
                : dateTime;
            }
        }
    }

