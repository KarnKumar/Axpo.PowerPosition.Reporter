using Axpo;
using PowerPosition.Reporter;
using PowerPosition.Reporter.Services;
using PowerPosition.Reporter.Services.Csv;
using PowerPosition.Reporter.Services.Logging;
using PowerPosition.Reporter.Services.TimeProvider;
using Serilog;
using Serilog.Events;


Log.Logger = new LoggerConfiguration ()
    .MinimumLevel.Information ()
    .WriteTo.Console (outputTemplate: "[{Timestamp:HH:mm} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateBootstrapLogger ();

Log.Information ("Starting Power Position Reporter Service.");

try
    {
    var builder = Host.CreateApplicationBuilder(args);

    // Config
    builder.Configuration.AddCommandLine (args);

    builder.Services.AddSerilog (( services, lc ) => lc
    .ReadFrom.Configuration (builder.Configuration)
    .Enrich.FromLogContext ()
    .WriteTo.Console (outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"));

    // validation to check required settings are present and valid at startup
    builder.Services.AddOptions<ReportSettings> ()
        .Bind (builder.Configuration.GetSection ("ReportSettings"))
        .Validate (r =>
        {
            if ( string.IsNullOrWhiteSpace (r.OutputPath) )
                throw new InvalidOperationException (
                    "ReportSettings.OutputPath is missing or empty. " +
                    "Set it in appsettings.json or via --ReportSettings:OutputPath.");

            if ( r.IntervalMinutes <= 0 )
                throw new InvalidOperationException (
                    $"ReportSettings.IntervalMinutes must be greater than 0 (current value: {r.IntervalMinutes}). " +
                    "Set it in appsettings.json or via --ReportSettings:IntervalMinutes.");

            return true;
        })
        .ValidateOnStart ();

    // take system timezone 
    builder.Services.AddSingleton (_ => TimeZoneInfo.Local);
    builder.Services.AddSingleton<ITimeProvider, ReportTimeProvider> ();

    // Assumption : PowerService.dll is not thread-safe, so we register it as transient to get a new instance for each extract run.
    builder.Services.AddTransient<IPowerService, PowerService> ();

     builder.Services.AddSingleton<IPowerPositionReportService, PowerPositionReportService> ();
     builder.Services.AddSingleton<ICsvExportService, CsvExportService> ();
     builder.Services.AddSingleton<IExtractLoggerFactory, ExtractLoggerFactory> ();
     builder.Services.AddHostedService<PowerPositionReportWorker> ();

    await builder.Build ().RunAsync ();
    }
catch ( Exception ex ) when ( ex is not OperationCanceledException )
    {
    Log.Fatal (ex, "Application terminated unexpectedly");
    }
finally
    {
    await Log.CloseAndFlushAsync ();
    }