using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Common.Exceptions;
using Microsoft.Azure.Devices.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace TelemetryProcessor;

// HTTP Patch triggered function to update the desired properties of a device
public sealed partial class UpdateDesiredProperties(
    RegistryManager registryManager,
    ILogger<UpdateDesiredProperties> logger)
{
    // maximum number of attempts to update the properties before failing
    private const int MaxAttempts = 3;

    [Function(nameof(UpdateDesiredProperties))]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "patch", Route = "devices/{deviceId}/desired-properties")]
        HttpRequestData request,
        string deviceId,
        CancellationToken cancellationToken)
    {

        // guard clause for null device id
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return await ErrorAsync(request, HttpStatusCode.BadRequest, "deviceId is required.", cancellationToken);
        }

        DesiredPropertiesPatch desired;
        try
        {
            // read the body and map to DesiredPropertiesPatch obj
            using var reader = new StreamReader(request.Body);
            desired = DesiredPropertiesPatch.Parse(await reader.ReadToEndAsync(cancellationToken));
        }
        catch (JsonException exception) // return ErrorAsync class for bad request
        {
            return await ErrorAsync(request, HttpStatusCode.BadRequest, exception.Message, cancellationToken);
        }

        // attempt to update the twin
        try
        {
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                // each attempt we'll get the current twin
                var current = await registryManager.GetTwinAsync(deviceId, cancellationToken);
                var revision = DesiredPropertiesPatch.NextRevision(current.Properties.Desired);
                var patch = desired.ToTwinPatch(revision); // convert it to a twin patch
                // we then attempt to update the twin
                try
                {
                    await registryManager.UpdateTwinAsync(deviceId, patch, current.ETag, cancellationToken);

                    var response = request.CreateResponse(HttpStatusCode.Accepted);
                    await response.WriteAsJsonAsync(new
                    {
                        deviceId,
                        status = "pending",
                        scheduleRevision = revision,
                        desired = desired.ToResponse()
                    }, cancellationToken);
                    return response;
                }
                catch (PreconditionFailedException) when (attempt < MaxAttempts) // this means something else has changed the twin before this one, so we fail
                {
                    logger.LogInformation(
                        "Desired property update for {DeviceId} conflicted; retrying ({Attempt}/{MaxAttempts}).",
                        deviceId,
                        attempt,
                        MaxAttempts);
                }
            }

            return await ErrorAsync(
                request,
                HttpStatusCode.Conflict,
                "The device twin changed during the update. Try again.",
                cancellationToken);
        }
        catch (DeviceNotFoundException) // normal handling for no device in IoT hub
        {
            return await ErrorAsync(request, HttpStatusCode.NotFound, "Device not found.", cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return await ErrorAsync(request, HttpStatusCode.Conflict, exception.Message, cancellationToken);
        }
        catch (PreconditionFailedException)
        {
            return await ErrorAsync(
                request,
                HttpStatusCode.Conflict,
                "The device twin changed during the update. Try again.",
                cancellationToken);
        }
        catch (IotHubException exception)
        {
            logger.LogError(exception, "IoT Hub rejected a desired property update for {DeviceId}.", deviceId);
            return await ErrorAsync(
                request,
                HttpStatusCode.BadGateway,
                "IoT Hub could not process the update.",
                cancellationToken);
        }
    }


    // asynchronous HTTP error response msg
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


    // Class to parse incoming JSON data into object
    public sealed partial class DesiredPropertiesPatch
    {
        // hashset for properties we can change
        private static readonly HashSet<string> AllowedProperties =
            ["chargingEnabled", "chargingStartUtc"];
        
        private DesiredPropertiesPatch(
            bool hasChargingEnabled,
            bool chargingEnabled,
            bool hasChargingStartUtc,
            DateTimeOffset? chargingStartUtc)
        {
            HasChargingEnabled = hasChargingEnabled;
            ChargingEnabled = chargingEnabled;
            HasChargingStartUtc = hasChargingStartUtc;
            ChargingStartUtc = chargingStartUtc;
        }

        public bool HasChargingEnabled { get; }
        public bool ChargingEnabled { get; }
        public bool HasChargingStartUtc { get; }
        public DateTimeOffset? ChargingStartUtc { get; }



        // Parse json method
        public static DesiredPropertiesPatch Parse(string json)
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("The request body must be a JSON object.");
            }

            var seen = new HashSet<string>();
            var hasChargingEnabled = false;
            var chargingEnabled = false;
            var hasChargingStartUtc = false;
            DateTimeOffset? chargingStartUtc = null;

            foreach (var property in document.RootElement.EnumerateObject())
            {

                // Invalid property names
                if (!AllowedProperties.Contains(property.Name) || !seen.Add(property.Name))
                {
                    throw new JsonException($"Property '{property.Name}' is not allowed or appears more than once.");
                }


                // Setting the charging state
                if (property.Name == "chargingEnabled")
                {
                    if (property.Value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                    {
                        throw new JsonException("chargingEnabled must be a boolean.");
                    }

                    hasChargingEnabled = true;
                    chargingEnabled = property.Value.GetBoolean();
                    continue;
                }


                // Otherwise it's charging start time
                hasChargingStartUtc = true;
                if (property.Value.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    throw new JsonException("chargingStartUtc must be an ISO 8601 timestamp with an offset, or null.");
                }

                var value = property.Value.GetString()!;
                if (!OffsetSuffixRegex().IsMatch(value) || !property.Value.TryGetDateTimeOffset(out var parsed))
                {
                    throw new JsonException("chargingStartUtc must be an ISO 8601 timestamp with an offset, or null.");
                }

                chargingStartUtc = parsed.ToUniversalTime();
            }

            if (!hasChargingEnabled && !hasChargingStartUtc)
            {
                throw new JsonException("At least one desired property is required.");
            }

            // return the object with the parsed values
            return new DesiredPropertiesPatch(
                hasChargingEnabled,
                chargingEnabled,
                hasChargingStartUtc,
                chargingStartUtc);
        }

        // Local method to calculate the next revision
        public static int NextRevision(TwinCollection desired)
        {
            if (!desired.Contains("scheduleRevision"))
            {
                return 1;
            }

            if (desired["scheduleRevision"] is not JValue { Type: JTokenType.Integer } value ||
                value.Value is not IConvertible convertible)
            {
                throw new InvalidOperationException("The existing scheduleRevision is invalid.");
            }

            long current;
            try
            {
                current = convertible.ToInt64(CultureInfo.InvariantCulture);
            }
            catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
            {
                throw new InvalidOperationException("The existing scheduleRevision is invalid.", exception);
            }

            return current is >= 0 and < int.MaxValue
                ? checked((int)current + 1)
                : throw new InvalidOperationException("The existing scheduleRevision is invalid.");
        }


        // Helper method to convert obj to twin
        public Twin ToTwinPatch(int revision)
        {
            var twin = new Twin();
            if (HasChargingEnabled)
            {
                twin.Properties.Desired["chargingEnabled"] = ChargingEnabled;
            }

            if (HasChargingStartUtc)
            {
                twin.Properties.Desired["chargingStartUtc"] = ChargingStartUtc?.ToString("O");
            }

            twin.Properties.Desired["scheduleRevision"] = revision;
            return twin;
        }

        // convert to a dictionary (obj) json response
        public Dictionary<string, object?> ToResponse()
        {
            var response = new Dictionary<string, object?>();
            if (HasChargingEnabled)
            {
                response["chargingEnabled"] = ChargingEnabled;
            }

            if (HasChargingStartUtc)
            {
                response["chargingStartUtc"] = ChargingStartUtc?.ToString("O");
            }

            return response;
        }

        [GeneratedRegex(@"(?:[zZ]|[+-]\d{2}:\d{2})$")]
        private static partial Regex OffsetSuffixRegex();
    }
}
