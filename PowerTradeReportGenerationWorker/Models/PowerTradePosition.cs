namespace PowerPosition.Reporter.Models;

/// <summary>
/// Represents the aggregated volume for a single clock hour of the trading day.
/// </summary>
public sealed class PowerTradePosition
    {
    /// <summary>
    /// Wall-clock start time of the hour in Europe/London local time.
    /// Formatted as HH:mm (e.g., "23:00", "00:00", "13:00").
    /// </summary>
    public string LocalTime { get; init; } = string.Empty;

    /// <summary>
    /// Total volume across all trades for this hour.
    /// </summary>
    public double Volume { get; init; }
    }
