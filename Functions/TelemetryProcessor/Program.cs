using Azure.Identity;
using Microsoft.Azure.Devices;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TelemetryProcessor;

// Local check if it parses
if (args is ["--self-check"])
{
    Telemetry.SelfCheck();
    return;
}

var builder = FunctionsApplication.CreateBuilder(args);
builder.Services.AddSingleton(_ =>
{
    var hostName = builder.Configuration["IotHubHostName"];
    if (string.IsNullOrWhiteSpace(hostName))
    {
        throw new InvalidOperationException("IotHubHostName is required.");
    }

    return RegistryManager.Create(hostName, new DefaultAzureCredential());
});
builder.Build().Run();
