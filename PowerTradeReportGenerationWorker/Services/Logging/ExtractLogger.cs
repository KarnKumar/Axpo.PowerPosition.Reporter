using PowerPosition.Reporter.Services.TimeProvider;
using System.Text;

namespace PowerPosition.Reporter.Services.Logging;

public sealed class ExtractLogger : IExtractLogger
    {
    private readonly FileStream   _fs;
    private readonly StreamWriter _writer;
    private readonly ITimeProvider _timeProvider;
    private bool _disposed;

    public ExtractLogger ( string filePath, ITimeProvider timeProvider )
        {
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException (nameof (timeProvider));

        _fs = new FileStream (filePath, FileMode.Create, FileAccess.Write,
                                 FileShare.Read, bufferSize: 4096, useAsync: true);
        _writer = new StreamWriter (_fs, Encoding.UTF8);
        }

    public async Task WriteAsync ( string level, string message )
        {
        if ( _disposed ) return;

        var line = $"{_timeProvider.LocalNow:yyyy-MM-dd HH:mm}Z [{level}] {message}";
        try
            {
            await _writer.WriteLineAsync (line);
            }
        catch ( Exception ex )
            {
            Console.Error.WriteLine ($"[ExtractLogger] Write failed: {ex.Message}");
            }
        }

    public async Task FlushAsync ( )
        {
        if ( _disposed ) return;
        try
            {
            await _writer.FlushAsync ();
            await _fs.FlushAsync ();        
            }
        catch ( Exception ex )
            {
            Console.Error.WriteLine ($"[ExtractLogger] Flush failed: {ex.Message}");
            }
        }

    public async ValueTask DisposeAsync ( )
        {
        if ( _disposed ) return;
        _disposed = true;
        await _writer.DisposeAsync ();   
        }
    }