using System.Text;

namespace PowerPosition.Reporter.Services.Logging
    {
    public sealed class ExtractLogger : IExtractLogger
        {
        private readonly StreamWriter _writer;

        public ExtractLogger ( string filePath )
            {
            var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter (fs, Encoding.UTF8) { AutoFlush = true };
            }

        public async Task WriteAsync ( string level, string message )
            {
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm} [{level}] {message}";
            try { await _writer.WriteLineAsync (line); }
            catch {  }
            }

        public async Task FlushAsync ( )
            {
            try { await _writer.FlushAsync (); }
            catch { }
            }

        public async ValueTask DisposeAsync ( )
            {
            await _writer.DisposeAsync ();
            }
        }
    }
