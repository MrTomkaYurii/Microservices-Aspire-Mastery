using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.AddServiceDefaults();

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

builder.Services.AddRateLimiter(rateLimiterOptions =>
{
    rateLimiterOptions.AddFixedWindowLimiter("fixed", options =>
    {
        options.Window = TimeSpan.FromSeconds(10);
        options.PermitLimit = 5;
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "eShop YARP API Gateway (.NET 10)",
        Version = "v1",
        Description = "Єдиний вхідний шлюз платформи eShop: маршрутизація до Catalog, Basket та Ordering через Aspire Service Discovery та захист Rate Limiting."
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "eShop YARP Gateway v1");
        c.RoutePrefix = string.Empty; // Відкриває Swagger UI одразу в корені
    });

    app.MapGet("/swagger", () => Results.Redirect("/"));
}

app.UseHttpsRedirection();

app.UseRateLimiter();

app.MapControllers();
app.MapReverseProxy();

app.Run();
