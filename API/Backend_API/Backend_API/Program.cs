using Backend_API.Authentication;
using Backend_API.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("VehicleDatabase")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:VehicleDatabase is required.");

// Add services to the container.
builder.Services.AddDbContext<VehicleDbContext>(options =>
    options.UseSqlServer(connectionString));
// Register Controllers
builder.Services.AddControllers();

// Add a local HTTP client set to the baseurl of our function
builder.Services.AddHttpClient("TelemetryProcessor", client =>
{
    var baseUrl = builder.Configuration["TelemetryProcessor:BaseUrl"];
    if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) ||
        (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
    {
        throw new InvalidOperationException(
            "TelemetryProcessor:BaseUrl must be an absolute http or https URL.");
    }

    client.BaseAddress = new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/");
});

// Add Swagger for local API testing
builder.Services.AddSwaggerGen(c =>
{
    c.SupportNonNullableReferenceTypes(); // support nullable types like string?
});

// Register authentication handler
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = UserIdAuthenticationHandler.SchemeName;
        options.DefaultChallengeScheme = UserIdAuthenticationHandler.SchemeName;
    })
    .AddScheme<AuthenticationSchemeOptions, UserIdAuthenticationHandler>(
        UserIdAuthenticationHandler.SchemeName,
        _ => { });

builder.Services.AddAuthorization();

var app = builder.Build();


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
// Configure middleware
app.UseHttpsRedirection();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
