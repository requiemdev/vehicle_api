using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace TelemetryProcessor;

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
            logger.LogWarning(exception, "Ignored malformed telemetry: {Message}", message);
        }
    }
}

public sealed record Telemetry(
    string MessageId,
    string DeviceId,
    DateTimeOffset TimestampUtc,
    int BatteryPercentage,
    bool IsCharging,
    int ScheduleRevisionApplied)
{
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
