using System.Text;
using System.Text.Json;
using Microsoft.Azure.Devices.Client;

// Device ID as configured on Azure IoT Hub
const string deviceId = "sim-car-001";

// Connection string, stored in ENV
var connectionString = Environment.GetEnvironmentVariable("IOT_CONNECTION") ?? throw new InvalidOperationException("Missing the connection string");

// Using Device Client package to connect
using var client = DeviceClient.CreateFromConnectionString(
    connectionString,
    TransportType.Mqtt);

using var stop = new CancellationTokenSource();

// On client stop
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stop.Cancel();
};


// On Hub connected
client.SetConnectionStatusChangesHandler((status, reason) =>
{
    Console.WriteLine($"IoT Hub connection: {status} ({reason})");
});


try
{
    await client.OpenAsync(stop.Token);
    // On device connected
    Console.WriteLine($"Connected as {deviceId}");
    Console.WriteLine("Press Ctrl+C to stop.");

    var batteryPercent = 64;
    var charging = false;

    while (!stop.IsCancellationRequested)
    {
        // Template message JSON
        var telemetry = new
        {
            messageId = Guid.NewGuid().ToString("N"),
            deviceId,
            timestamp = DateTimeOffset.UtcNow,
            batteryPercent,
            charging
        };

        var json = JsonSerializer.Serialize(telemetry);

        using var message = new Message(
            Encoding.UTF8.GetBytes(json))
        {
            ContentType = "application/json",
            ContentEncoding = "utf-8",
            MessageId = telemetry.messageId
        };

        await client.SendEventAsync(message, stop.Token);

        Console.WriteLine($"Sent: {json}");

        batteryPercent = Math.Clamp(
            batteryPercent + (charging ? 1 : -1),
            0,
            100);

        // 15 seconds keeps one device below the F1 daily quota.
        await Task.Delay(TimeSpan.FromSeconds(15), stop.Token);
    }
}
catch (OperationCanceledException) when (stop.IsCancellationRequested)
{
    // Normal shutdown.
}
finally
{
    await client.CloseAsync();
}