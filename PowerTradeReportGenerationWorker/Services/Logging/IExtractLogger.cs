using System;
using System.Collections.Generic;
namespace PowerPosition.Reporter.Services.Logger
    {
    public interface IExtractLogger : IAsyncDisposable
        {
        Task WriteAsync ( string level, string message );
        Task FlushAsync ( );
        }
    }
