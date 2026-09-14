using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Backend_API.Dto;
using Backend_API.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend_API.Controllers;

[ApiController]
[Authorize]
[Route("vehicles")]
public sealed class VehicleController(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IVehicleRepository vehicleRepository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetVehicles(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return VehicleAccessDenied();
        }
        var vehicles = await vehicleRepository.GetByOwnerAsync(userId, cancellationToken);

        return Ok(vehicles);
    }

    // method to report the state of a device
    [HttpGet("{deviceId}/state")]
    public async Task<IActionResult> GetVehicleState(
        string deviceId, CancellationToken cancellationToken)
    {
        // we dont want to cache the result since we will be polling it constantly
        Response.Headers.CacheControl = "no-store";

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return BadRequest(new { error = "deviceId is required." });
        }

        if (!await OwnsVehicleAsync(deviceId, cancellationToken))
        {
            return VehicleAccessDenied();
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"devices/{Uri.EscapeDataString(deviceId)}/state");
        
        // attach the functions key so we can make requests to the Function
        var functionKey = configuration["TelemetryProcessor:FunctionKeys:GetDeviceState"];
        if (!string.IsNullOrWhiteSpace(functionKey))
        {
            request.Headers.Add("x-functions-key", functionKey);
        }

        try
        {
            using var response = await httpClientFactory
                .CreateClient("TelemetryProcessor")
                .SendAsync(request, cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            return new ContentResult
            {
                StatusCode = (int)response.StatusCode,
                Content = body,
                ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json"
            };
        }
        catch (HttpRequestException)
        {
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = "The vehicle state function could not be reached." });
        }
    }

    // patch the charging schedule
    [HttpPatch("{deviceId}/charging-schedule")]
    public async Task<IActionResult> SetChargingSchedule(
        string deviceId, ChargingScheduleInput input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return BadRequest(new { error = "deviceId is required." });
        }

        if (input.ScheduleEnabled is null && input.StartTime is null)
        {
            return BadRequest(new { error = "At least one schedule property is required." });
        }

        if (!await OwnsVehicleAsync(deviceId, cancellationToken))
        {
            return VehicleAccessDenied();
        }

        var desiredProperties = new Dictionary<string, object>();
        if (input.ScheduleEnabled is not null)
        {
            desiredProperties["chargingScheduleEnabled"] = input.ScheduleEnabled.Value;
        }

        if (input.StartTime is not null)
        {
            desiredProperties["chargingStartUtc"] = input.StartTime.Value.ToUniversalTime();
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"devices/{Uri.EscapeDataString(deviceId)}/desired-properties")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(desiredProperties),
                Encoding.UTF8,
                "application/json")
        };

        var functionKey = configuration["TelemetryProcessor:FunctionKeys:UpdateDesiredProperties"];
        if (!string.IsNullOrWhiteSpace(functionKey))
        {
            request.Headers.Add("x-functions-key", functionKey);
        }

        // duplicated from before, should refactor 
        try
        {
            using var response = await httpClientFactory
                .CreateClient("TelemetryProcessor")
                .SendAsync(request, cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            return new ContentResult
            {
                StatusCode = (int)response.StatusCode,
                Content = body,
                ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json"
            };
        }
        catch (HttpRequestException)
        {
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = "The schedule function could not be reached." });
        }
    }

    // replace the charging status 
    [HttpPut("{deviceId}/charging")]
    public async Task<IActionResult> SetCharging(
        string deviceId,
        ChargingInputDto input,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return BadRequest(new { error = "deviceId is required." });
        }

        if (input?.Charging is null)
        {
            return BadRequest(new { error = "charging is required." });
        }

        if (!await OwnsVehicleAsync(deviceId, cancellationToken))
        {
            return VehicleAccessDenied();
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"devices/{Uri.EscapeDataString(deviceId)}/charging")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { isCharging = input.Charging.Value }),
                Encoding.UTF8,
                "application/json")
        };

        var functionKey = configuration["TelemetryProcessor:FunctionKeys:SetCharging"];
        if (!string.IsNullOrWhiteSpace(functionKey))
        {
            request.Headers.Add("x-functions-key", functionKey);
        }

        try
        {
            using var response = await httpClientFactory
                .CreateClient("TelemetryProcessor")
                .SendAsync(request, cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            return new ContentResult
            {
                StatusCode = (int)response.StatusCode,
                Content = body,
                ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json"
            };
        }
        catch (HttpRequestException)
        {
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = "The charging function could not be reached." });
        }
    }

    // Check that the authenticated user owns the vehicle.
    private async Task<bool> OwnsVehicleAsync(
        string deviceId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return false;
        }

        return await vehicleRepository.IsOwnedByAsync(deviceId, userId, cancellationToken);
    }

    // Retrieve user ID from the Claims assigned to user
    private bool TryGetUserId(out int userId)
    {
        return int.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out userId) && userId > 0;
    }

    private IActionResult VehicleAccessDenied() =>
        StatusCode(
            StatusCodes.Status403Forbidden,
            new { error = "Vehicle does not exist or is not owned by the current user." });
}
