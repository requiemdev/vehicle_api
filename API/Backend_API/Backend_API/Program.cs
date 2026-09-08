var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
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
builder.Services.AddSwaggerGen(c =>
{
    c.SupportNonNullableReferenceTypes();
});

var app = builder.Build();


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
