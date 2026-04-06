using PowerPosition.Reporter.Services.TimeProvider;

namespace PowerPosition.Reporter.Services.Logging
    {

    /// <summary>
    /// IExtractLoggerFactory is a factory interface for creating instances of <see cref="ExtractLogger"/> which log messages to a file with timestamps.
    /// </summary>
    public interface IExtractLoggerFactory
        {
        IExtractLogger Create ( string filePath );
        }

    /// <summary>
    /// ExtractLoggerFactory creates instances of <see cref="ExtractLogger"/> which log messages to a file with timestamps.
    /// </summary>
    public class ExtractLoggerFactory ( ITimeProvider timeProvider ) : IExtractLoggerFactory
        {
        private readonly ITimeProvider _timeProvider = timeProvider
                ?? throw new ArgumentNullException (nameof (timeProvider));

        public IExtractLogger Create ( string filePath )
            => new ExtractLogger (filePath, _timeProvider);
        }
    }
