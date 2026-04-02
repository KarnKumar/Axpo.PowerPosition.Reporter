
namespace PowerPosition.Reporter.Services.TimeProvider
    {
    public interface ITimeProvider
        {
        DateTimeOffset LocalNow { get; }
        string TimeZoneId { get; }
        DateTime ToLocalTime ( DateTime utcDateTime );
        }
    }
