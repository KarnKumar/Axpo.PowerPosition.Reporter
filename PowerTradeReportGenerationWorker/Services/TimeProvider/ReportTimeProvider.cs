namespace PowerPosition.Reporter.Services.TimeProvider
    {
    public sealed class ReportTimeProvider ( TimeZoneInfo timeZone ) : ITimeProvider
        {

        private readonly TimeZoneInfo _timeZone = timeZone
        ?? throw new ArgumentNullException(nameof(timeZone));

        public DateTimeOffset LocalNow
         => TimeZoneInfo.ConvertTime (DateTimeOffset.UtcNow, _timeZone);
        public string TimeZoneId => _timeZone.Id;

        public DateTime ToLocalTime ( DateTime dateTime ) => dateTime.Kind switch
            {
                DateTimeKind.Utc => TimeZoneInfo.ConvertTimeFromUtc (dateTime, _timeZone),
                _ => throw new ArgumentException (
                         $"Only UTC DateTimes are supported. Received Kind={dateTime.Kind}.",
                         nameof (dateTime))
                };
        }
    }
