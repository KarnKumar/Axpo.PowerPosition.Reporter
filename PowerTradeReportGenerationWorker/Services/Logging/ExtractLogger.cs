using System.Text;

namespace PowerPosition.Reporter.Services.Logging;

public sealed class ExtractLogger : IExtractLogger
    {
    private readonly FileStream   _fs;
    private readonly StreamWriter _writer;
    private bool _disposed;

    public ExtractLogger ( string filePath )
        {
        _fs = new FileStream (filePath, FileMode.Create, FileAccess.Write,
                                 FileShare.Read, bufferSize: 4096, useAsync: true);
        _writer = new StreamWriter (_fs, Encoding.UTF8);
        }

    public async Task WriteAsync ( string level, string message )
        {
        if ( _disposed ) return;

        var line = $"{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}Z [{level}] {message}";
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