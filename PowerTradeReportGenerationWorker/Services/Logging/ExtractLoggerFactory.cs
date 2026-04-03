
namespace PowerPosition.Reporter.Services.Logging
    {
    public interface IExtractLoggerFactory
        {
        IExtractLogger Create ( string filePath );
        }

    public class ExtractLoggerFactory : IExtractLoggerFactory
        {
        public IExtractLogger Create ( string filePath ) => new ExtractLogger (filePath);
        }
    }
