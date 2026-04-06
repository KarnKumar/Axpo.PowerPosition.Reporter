using Axpo;
using Microsoft.Extensions.Options;
using PowerPosition.Reporter;
using PowerPosition.Reporter.Services;
using PowerPosition.Reporter.Services.Csv;
using PowerPosition.Reporter.Services.Logging;
using PowerPosition.Reporter.Services.TimeProvider;
using Serilog;

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
     .Validate (r => !string.IsNullOrWhiteSpace (r.OutputPath),
               "ReportSettings.OutputPath is missing or empty.")
     .Validate (r => r.IntervalMinutes > 0,
               "ReportSettings.IntervalMinutes must be > 0.")
     .Validate (r => r.IntervalMinutes <= 1440,
               "ReportSettings.IntervalMinutes must be <= 1440 (24 hours).");

    // The business specification mandates Europe/ London (wall - clock / local time).
    // TimeZoneInfo.Local is intentionally NOT used.
    const string RequiredTimeZoneId = "Europe/London";
    builder.Services.AddSingleton (_ => TimeZoneInfo.FindSystemTimeZoneById (RequiredTimeZoneId));
    builder.Services.AddSingleton<ITimeProvider, ReportTimeProvider> ();

    // Assumption : PowerService.dll is not thread-safe, so we register it as transient to get a new instance for each extract run.
    builder.Services.AddTransient<IPowerService, PowerService> ();
    builder.Services.AddTransient<IPowerPositionReportService, PowerPositionReportService> ();

    // Singleton is efficient → no need to recreate every time
    builder.Services.AddSingleton<ICsvReportService, CsvExportService> ();

    // standard pattern in .NET
    builder.Services.AddSingleton<IExtractLoggerFactory, ExtractLoggerFactory> ();

    builder.Services.AddHostedService<PowerPositionReportWorker> ();

    var app = builder.Build();
    // Eagerly resolve to trigger ValidateOnStart before the host runs
    _ = app.Services.GetRequiredService<IOptionsMonitor<ReportSettings>> ().CurrentValue;

    await app.RunAsync ();

    }
   catch ( OptionsValidationException ex )
    {
    foreach ( var failure in ex.Failures )
        Log.Fatal ("  [INVALID] {Failure}", failure);

    Log.Fatal ("Power Position Reporter could not start — one or more required settings are missing or invalid. Correct the configuration and restart the service.");
    Environment.Exit (1);
    }
   catch ( Exception ex ) when ( ex is not OperationCanceledException )
    {
    Log.Fatal (ex, "Power Position Reporter stopped unexpectedly due to an unhandled {ExceptionType}. No further reports will be generated until the service is restarted.",
        ex.GetType ().Name);
    Environment.Exit (1);
    }
   finally
    {
    await Log.CloseAndFlushAsync ();
    }