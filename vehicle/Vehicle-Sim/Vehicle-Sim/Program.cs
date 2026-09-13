using System.Text;
using System.Globalization;
using System.Text.Json;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Spectre.Console;

// Device ID as configured on Azure IoT Hub
var deviceId = Environment.GetEnvironmentVariable("DEVICE_ID") ?? "sim-car-001";

// Connection string, stored in ENV
var connectionString = Environment.GetEnvironmentVariable("IOT_CONNECTION")
    ?? throw new InvalidOperationException("Missing IOT_CONNECTION environment variable (device connection string)");
// Using Device Client package to connect
using var client = DeviceClient.CreateFromConnectionString(
    connectionString,
    TransportType.Mqtt);

// Local twin state instance
var twinState = new TwinState();
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
    var color = status == ConnectionStatus.Connected ? "green" : "yellow";
    AnsiConsole.MarkupLine(
        $"[grey]{DateTime.Now:HH:mm:ss}[/] [{color}]CONNECTION[/] {status} [grey]({reason})[/]");
});

try
{
    // apply changes on desired property update
    await client.SetDesiredPropertyUpdateCallbackAsync(
        (desiredProperties, _) =>
             ApplyDesiredPropertiesAsync(client, desiredProperties, twinState),
        null);

    // direct method handler to set charging state
    await client.SetMethodHandlerAsync(
        "setCharging",
        (request, _) => SetChargingAsync(request, twinState),
        null);

    await client.OpenAsync(stop.Token);

    // read the desired state first, to get any updates while the device was offline
    var twin = await client.GetTwinAsync();
    await ApplyDesiredPropertiesAsync(
        client,
        twin.Properties.Desired,
        twinState);

    // On device connected
    AnsiConsole.MarkupLine(
        $"[green bold]READY[/] Connected as [cyan]{Markup.Escape(deviceId)}[/]");
    AnsiConsole.MarkupLine("[grey]Press Ctrl+C to stop.[/]");

    var batteryPercent = 64;

    
    // Main loop, run while not cancelled
    while (!stop.IsCancellationRequested)
    {
        bool chargingScheduleEnabled;
        bool? chargingOverride; // we want to be able to override a charging schedule
        int scheduleRevisionApplied;
        DateTimeOffset? chargingStartUtc;
        // lock while modifying to make sure the ApplyDesiredPropertiesAsync doesn't cause race conditions
        lock (twinState)
        {
            chargingScheduleEnabled = twinState.ChargingScheduleEnabled;
            chargingOverride = twinState.ChargingOverride;
            scheduleRevisionApplied = twinState.ScheduleRevisionApplied;
            chargingStartUtc = twinState.ChargingStartUtc;
        }

        // Check if we are scheduled to start charging
        var now = DateTimeOffset.UtcNow;
        var scheduleStarted = chargingStartUtc is null ||
            now >= chargingStartUtc.Value;
        // use charging override otherwhise use schedule
        var charging = chargingOverride ??
            (chargingScheduleEnabled && scheduleStarted);

        // Template message JSON
        var telemetry = new
        {
            messageId = Guid.NewGuid().ToString("N"),
            deviceId,
            timestampUtc = DateTimeOffset.UtcNow,
            batteryPercentage = batteryPercent,
            isCharging = charging,
            scheduleRevisionApplied, // this is the version of the last revision of the twin applied
            chargingStartUtc // using the latest charging start time
        };


        // Serialise and encode the message to be sent over 
        var json = JsonSerializer.Serialize(telemetry);

        using var message = new Message(
            Encoding.UTF8.GetBytes(json))
        {
            ContentType = "application/json",
            ContentEncoding = "utf-8",
            MessageId = telemetry.messageId
        };

        await client.SendEventAsync(message, stop.Token);

        var reportedState = new TwinCollection();
        reportedState["batteryPercentage"] = telemetry.batteryPercentage;
        reportedState["isCharging"] = telemetry.isCharging;
        reportedState["telemetryTimestampUtc"] = telemetry.timestampUtc
            .ToUniversalTime()
            .ToString("O", CultureInfo.InvariantCulture);
        await client.UpdateReportedPropertiesAsync(reportedState);

        var batteryColor = telemetry.batteryPercentage <= 20 ? "red" :
            telemetry.batteryPercentage <= 40 ? "yellow" : "green";
        var chargingText = telemetry.isCharging
            ? "[green]CHARGING[/]"
            : "[grey]DISCHARGING[/]";
        var scheduledStart = telemetry.chargingStartUtc?.ToLocalTime()
            .ToString("dd MMM HH:mm", CultureInfo.InvariantCulture) ?? "immediate";

        AnsiConsole.MarkupLine(
            $"[grey]{telemetry.timestampUtc.ToLocalTime():HH:mm:ss}[/] " +
            $"[blue]TELEMETRY[/] [{batteryColor} bold]{telemetry.batteryPercentage,3}%[/] " +
            $"{chargingText} [grey]rev[/] {telemetry.scheduleRevisionApplied} " +
            $"[grey]start[/] {scheduledStart}");

        // Simulate the increase/descrease of battery
        batteryPercent = Math.Clamp(
            batteryPercent + (charging ? 1 : -1),
            0,
            100);

        // 15 seconds delay for demo
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

// Direct method to handle charging
static Task<MethodResponse> SetChargingAsync(MethodRequest request, TwinState state)
{
    try
    {
        // parse the payload
        using var payload = JsonDocument.Parse(request.DataAsJson);
        if (payload.RootElement.ValueKind != JsonValueKind.Object ||
            !payload.RootElement.TryGetProperty("isCharging", out var value) ||
            value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw new JsonException("Payload must be {\"isCharging\": true|false}.");
        }

        var isCharging = value.GetBoolean();
        lock (state)
        {
            state.ChargingOverride = isCharging;
        }

        var chargingText = isCharging ? "enabled" : "disabled";
        AnsiConsole.MarkupLine(
            $"[grey]{DateTime.Now:HH:mm:ss}[/] [magenta]COMMAND[/] Charging override " +
            $"[bold]{chargingText}[/]");
        return Task.FromResult(new MethodResponse(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { isCharging })),
            200));
    }
    catch (JsonException exception)
    {
        return Task.FromResult(new MethodResponse(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { error = exception.Message })),
            400));
    }
}


// method to apply the changed desired properties from the device twin
static async Task ApplyDesiredPropertiesAsync(
    DeviceClient client,
    TwinCollection desiredProperties,
    TwinState state)
{

    // guard clause for desired properties missing properties 
    if (!desiredProperties.Contains("chargingScheduleEnabled") &&
        !desiredProperties.Contains("scheduleRevision") &&
        !desiredProperties.Contains("chargingStartUtc"))
    {
        return;
    }

    int? configRevision = null;

    try
    {
        if (desiredProperties.Contains("scheduleRevision"))
        {
            configRevision = Convert.ToInt32(desiredProperties["scheduleRevision"]);
        }

        bool chargingScheduleEnabled;
        int scheduleRevision;
        DateTimeOffset? chargingStartUtc;
        // lock state so local read won't try access half-changed states
        lock (state)
        {
            chargingScheduleEnabled = state.ChargingScheduleEnabled;
            scheduleRevision = state.ScheduleRevisionApplied;
            chargingStartUtc = state.ChargingStartUtc;

            // Set charging enabled
            if (desiredProperties.Contains("chargingScheduleEnabled"))
            {
                chargingScheduleEnabled = Convert.ToBoolean(
                    desiredProperties["chargingScheduleEnabled"]);
            }

            // Set the schedule revision number most recently applied
            if (desiredProperties.Contains("scheduleRevision"))
            {
                scheduleRevision = configRevision!.Value;
            }

            configRevision ??= scheduleRevision;

            // set the charge time
            if (desiredProperties.Contains("chargingStartUtc"))
            {
                var rawStart = desiredProperties["chargingStartUtc"];

                if (rawStart is null)
                {
                    chargingStartUtc = null;
                }
                else if (!DateTimeOffset.TryParse(
                    Convert.ToString(rawStart, CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTimeOffset parsedStart))
                {
                    throw new FormatException(
                        "chargingStartUtc must be a valid ISO 8601 timestamp.");
                }
                else
                {
                    chargingStartUtc = parsedStart;
                }
            }

            state.ChargingScheduleEnabled = chargingScheduleEnabled;
            state.ScheduleRevisionApplied = scheduleRevision;
            state.ChargingStartUtc = chargingStartUtc;
            state.ChargingOverride = null;
        }

        var reportedProperties = new TwinCollection();
        reportedProperties["chargingScheduleEnabled"] = chargingScheduleEnabled;
        reportedProperties["scheduleRevision"] = scheduleRevision;
        reportedProperties["configRevision"] = scheduleRevision;
        reportedProperties["chargingStartUtc"] = chargingStartUtc?
            .ToUniversalTime()
            .ToString("O", CultureInfo.InvariantCulture);
        reportedProperties["configStatus"] = "applied";
        reportedProperties["configError"] = null;
        reportedProperties["lastConfigUpdateUtc"] = DateTime.UtcNow;

        // update IoT hub with our reported properties
        await client.UpdateReportedPropertiesAsync(reportedProperties);

        var start = chargingStartUtc?.ToLocalTime()
            .ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture) ?? "immediate";
        AnsiConsole.MarkupLine(
            $"[grey]{DateTime.Now:HH:mm:ss}[/] [cyan]CONFIG[/] " +
            $"Schedule [bold]{(chargingScheduleEnabled ? "enabled" : "disabled")}[/], " +
            $"start {start}, revision {scheduleRevision}");
    }
    catch (Exception exception) when (exception is FormatException or
        InvalidCastException or
        OverflowException)
    {
        var reportedProperties = new TwinCollection();
        reportedProperties["configRevision"] = configRevision;
        reportedProperties["configStatus"] = "rejected";
        reportedProperties["configError"] = exception.Message;
        reportedProperties["lastConfigUpdateUtc"] = DateTime.UtcNow;

        await client.UpdateReportedPropertiesAsync(reportedProperties);

        AnsiConsole.MarkupLine(
            $"[grey]{DateTime.Now:HH:mm:ss}[/] [red bold]CONFIG REJECTED[/] " +
            Markup.Escape(exception.Message));
    }
}

// Twin state of the device
sealed class TwinState
{
    public bool ChargingScheduleEnabled { get; set; }

    public bool? ChargingOverride { get; set; }

    public int ScheduleRevisionApplied { get; set; }

    public DateTimeOffset? ChargingStartUtc { get; set  ; }
}
