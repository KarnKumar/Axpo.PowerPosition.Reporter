namespace PowerPosition.Reporter.Services.Logging
    {
    public interface IExtractLogger : IAsyncDisposable
        {
        Task WriteAsync ( string level, string message );
        Task FlushAsync ( );
        }
    }
