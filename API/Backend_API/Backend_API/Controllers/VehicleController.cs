using Backend_API.Dto;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace Backend_API.Controllers
{
    [ApiController]
    [Route("vehicles")]
    public sealed class VehicleController(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration) : ControllerBase
    {

        /// <summary>
        /// Patch the fields of the device twin (for the scheduled time)
        /// </summary>
        /// <param name="deviceId"></param>
        /// <param name="input"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// 
        [HttpPatch("{deviceId}/charging-schedule")]
        public async Task<IActionResult> SetChargingSchedule(
            string deviceId, ChargingScheduleInput input, CancellationToken cancellationToken)
        {
            // should refactor to a reusable guard clause
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return BadRequest(new { error = "deviceId is required." });
            }

            if (input.ScheduleEnabled is null && input.StartTime is null)
            {
                return BadRequest(new { error = "At least one schedule property is required." });
            }

            // dict to store the fields
            var desiredProperties = new Dictionary<string, object>();
            if (input.ScheduleEnabled is not null)
            {
                desiredProperties["chargingScheduleEnabled"] = input.ScheduleEnabled.Value;
            }

            if (input.StartTime is not null)
            {
                desiredProperties["chargingStartUtc"] = input.StartTime.Value.ToUniversalTime();
            }

            // Make the request to the Azure function
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
    }
}
