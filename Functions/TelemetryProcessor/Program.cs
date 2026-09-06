using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Hosting;
using TelemetryProcessor;

// Local check if it parses
if (args is ["--self-check"])
{
    Telemetry.SelfCheck();
    return;
}

var builder = FunctionsApplication.CreateBuilder(args);
builder.Build().Run();
