using System.Net;
using System.Text.Json.Serialization;
using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Common.Exceptions;
using Microsoft.Azure.Devices.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace TelemetryProcessor;


/// HTTP GET function to return the state of a device
public sealed class GetDeviceState(
    RegistryManager registryManager,
    ILogger<GetDeviceState> logger)
{
    [Function(nameof(GetDeviceState))]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "devices/{deviceId}/state")]
        HttpRequestData request,
        string deviceId,
        CancellationToken cancellationToken)
    {
        // device id guard clause
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return await ErrorAsync(request, HttpStatusCode.BadRequest, "deviceId is required.", cancellationToken);
        }

        try
        {
            // retrieve twin, parse, return as JSON
            var twin = await registryManager.GetTwinAsync(deviceId, cancellationToken);
            var response = request.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Cache-Control", "no-store");
            await response.WriteAsJsonAsync(Project(deviceId, twin), cancellationToken);
            return response;
        }
        catch (DeviceNotFoundException)
        {
            return await ErrorAsync(request, HttpStatusCode.NotFound, "Device not found.", cancellationToken);
        }
        catch (IotHubException exception)
        {
            logger.LogError(exception, "IoT Hub could not read state for {DeviceId}.", deviceId);
            return await ErrorAsync(request, HttpStatusCode.BadGateway, "IoT Hub could not read the device state.", cancellationToken);
        }
    }

    // method to map the device twin to DeviceState
    public static DeviceState Project(string deviceId, Twin twin)
    {
        var desired = twin.Properties.Desired;
        var reported = twin.Properties.Reported;
        var desiredRevision = (int)desired["scheduleRevision"];
        var configRevision = (int)reported["configRevision"];
        var configStatus = (string)reported["configStatus"];
        var chargingStartUtc = (string?)desired["chargingStartUtc"];
        var scheduleStatus = desiredRevision == configRevision ? configStatus : "pending";

        return new DeviceState(
            deviceId,
            (int)reported["batteryPercentage"],
            (bool)reported["isCharging"],
            DateTimeOffset.Parse((string)reported["telemetryTimestampUtc"]),
            (bool)desired["chargingScheduleEnabled"],
            chargingStartUtc is null
                ? null
                : DateTimeOffset.Parse(chargingStartUtc),
            scheduleStatus,
            scheduleStatus == "rejected" ? (string?)reported["configError"] : null);
    }

    private static async Task<HttpResponseData> ErrorAsync(
        HttpRequestData request,
        HttpStatusCode status,
        string message,
        CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(status);
        response.Headers.Add("Cache-Control", "no-store");
        await response.WriteAsJsonAsync(new { error = message }, cancellationToken);
        return response;
    }
}

public sealed record DeviceState(
    [property: JsonPropertyName("deviceId")] string DeviceId,
    [property: JsonPropertyName("batteryPercentage")] int? BatteryPercentage,
    [property: JsonPropertyName("isCharging")] bool? IsCharging,
    [property: JsonPropertyName("telemetryTimestampUtc")] DateTimeOffset? TelemetryTimestampUtc,
    [property: JsonPropertyName("scheduleEnabled")] bool? ScheduleEnabled,
    [property: JsonPropertyName("chargingStartUtc")] DateTimeOffset? ChargingStartUtc,
    [property: JsonPropertyName("scheduleStatus")] string ScheduleStatus,
    [property: JsonPropertyName("scheduleError")] string? ScheduleError);
