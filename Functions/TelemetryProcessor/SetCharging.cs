using System.Net;
using System.Text.Json;
using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Common.Exceptions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace TelemetryProcessor;

// Direct set charging method, invoked by HTTP POST
public sealed class SetCharging(
    ServiceClient serviceClient,
    ILogger<SetCharging> logger)
{
    [Function(nameof(SetCharging))]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "devices/{deviceId}/charging")]
        HttpRequestData request,
        string deviceId,
        CancellationToken cancellationToken)
    {
        // guard clause for device ID (should refactor into shared method with twin updater later)
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return await ErrorAsync(request, HttpStatusCode.BadRequest, "deviceId is required.", cancellationToken);
        }

        bool isCharging;

        // Parse the request body
        try
        {
            using var reader = new StreamReader(request.Body);
            isCharging = ParseIsCharging(await reader.ReadToEndAsync(cancellationToken));
        }
        catch (JsonException exception)
        {
            return await ErrorAsync(request, HttpStatusCode.BadRequest, exception.Message, cancellationToken);
        }

        // create the IoT Hub method
        var method = new CloudToDeviceMethod("setCharging", TimeSpan.FromSeconds(10)); // response timeout of 10s
        method.SetPayloadJson(JsonSerializer.Serialize(new { isCharging }));

        // invoke the method
        try
        {
            var result = await serviceClient.InvokeDeviceMethodAsync(deviceId, method, cancellationToken);
            var response = request.CreateResponse((HttpStatusCode)result.Status);
            response.Headers.Add("Content-Type", "application/json");
            await response.WriteStringAsync(result.GetPayloadAsJson(), cancellationToken);
            return response;
        }
        catch (DeviceNotFoundException)
        {
            return await ErrorAsync(request, HttpStatusCode.NotFound, "Device not found or offline.", cancellationToken);
        }
        catch (TimeoutException)
        {
            return await ErrorAsync(request, HttpStatusCode.GatewayTimeout, "Device did not respond in time.", cancellationToken);
        }
        catch (IotHubException exception)
        {
            logger.LogError(exception, "IoT Hub could not invoke setCharging on {DeviceId}.", deviceId);
            return await ErrorAsync(request, HttpStatusCode.BadGateway, "IoT Hub could not invoke the charging command.", cancellationToken);
        }
    }

    // helper method to parse the payload
    public static bool ParseIsCharging(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("isCharging", out var value) ||
            value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw new JsonException("Payload must be {\"isCharging\": true|false}.");
        }

        return value.GetBoolean();
    }

    // needs to be refactored - repeated code
    private static async Task<HttpResponseData> ErrorAsync(
        HttpRequestData request,
        HttpStatusCode status,
        string message,
        CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(status);
        await response.WriteAsJsonAsync(new { error = message }, cancellationToken);
        return response;
    }
}
