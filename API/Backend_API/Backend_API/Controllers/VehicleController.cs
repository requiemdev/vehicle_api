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
