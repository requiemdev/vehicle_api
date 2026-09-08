using Azure.Identity;
using Microsoft.Azure.Devices;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using TelemetryProcessor;

// Local check if it parses
if (args is ["--self-check"])
{
    Telemetry.SelfCheck();
    return;
}

// Registry manager to update twins
var builder = FunctionsApplication.CreateBuilder(args);

// validate hostName
var hostName = builder.Configuration["IotHubHostName"];
if (string.IsNullOrWhiteSpace(hostName))
{
    throw new InvalidOperationException("IotHubHostName is required.");
}

// Registry manager for the twin update
builder.Services.AddSingleton(_ =>
    RegistryManager.Create(
        hostName,
        new DefaultAzureCredential()));

// ServiceClient for direct method invocation
builder.Services.AddSingleton(_ =>
    ServiceClient.Create(
        hostName,
        new DefaultAzureCredential(),
        TransportType.Amqp));

builder.Build().Run();
