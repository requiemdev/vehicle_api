using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace TelemetryProcessor;

/// <summary>
/// Class to process incoming telemetry from IoT hub
/// </summary>
/// <param name="logger"></param>
public sealed class ProcessTelemetry(ILogger<ProcessTelemetry> logger)
{
    [Function(nameof(ProcessTelemetry))]
    public void Run(
        [EventHubTrigger("messages/events", Connection = "IotHubEventsConnection", ConsumerGroup = "%TelemetryConsumerGroup%", IsBatched = false)]
        string message)
    {
        try
        {
            var telemetry = Telemetry.Parse(message);

            logger.LogInformation(
                "Received telemetry {MessageId} from {DeviceId} at {TimestampUtc}: battery {BatteryPercentage}%, charging {IsCharging}",
                telemetry.MessageId,
                telemetry.DeviceId,
                telemetry.TimestampUtc,
                telemetry.BatteryPercentage,
                telemetry.IsCharging);
        }
        catch (JsonException exception)
        {
            var preview = message is null ? "<null>" : message[..Math.Min(message.Length, 512)];
            logger.LogWarning(exception, "Ignored malformed telemetry (preview): {MessagePreview}", preview);
        }
    }
}
/// <summary>
/// Record to hold the telemetry data (record since fields are init-only)
/// </summary>
/// <param name="MessageId"> message ID from the device </param>
/// <param name="DeviceId"> device ID from IoT Hub</param>
/// <param name="TimestampUtc"> timestamp message was sent from, time from UTC </param>
/// <param name="BatteryPercentage"> battery % of device </param>
/// <param name="IsCharging"> battery charging state </param>
/// <param name="ScheduleRevisionApplied"> has battery charging schedule been applied from the device twin </param>
public sealed record Telemetry(
    string MessageId,
    string DeviceId,
    DateTimeOffset TimestampUtc,
    int BatteryPercentage,
    bool IsCharging,
    int ScheduleRevisionApplied)
{
    /// <summary>
    /// Parse the serialised message and return as Telemetry record
    /// </summary>
    /// <param name="message"></param>
    /// <returns></returns>
    /// <exception cref="JsonException"></exception>
    public static Telemetry Parse(string message)
    {
        var telemetry = JsonSerializer.Deserialize<Telemetry>(message, JsonSerializerOptions.Web)
            ?? throw new JsonException("The telemetry body was empty.");

        if (string.IsNullOrWhiteSpace(telemetry.MessageId) ||
            string.IsNullOrWhiteSpace(telemetry.DeviceId) ||
            telemetry.TimestampUtc == default ||
            telemetry.BatteryPercentage is < 0 or > 100)
        {
            throw new JsonException("Telemetry has missing or invalid required values.");
        }

        return telemetry;
    }

    /// <summary>
    /// Method to check itself using a hard-coded JSON 
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    public static void SelfCheck()
    {
        var parsed = Parse("""{"messageId":"1","deviceId":"sim-car-001","timestampUtc":"2026-09-05T02:15:00Z","batteryPercentage":67,"isCharging":true,"scheduleRevisionApplied":0}""");
        if (parsed.BatteryPercentage != 67 || !parsed.IsCharging)
        {
            throw new InvalidOperationException("Telemetry parsing self-check failed.");
        }

        Console.WriteLine("Telemetry parsing self-check passed.");
    }
}
