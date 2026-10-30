using Catalog.Api.Data;

var builder = WebApplication.CreateBuilder(args);

// =========================================================================
// 1. Aspire Service Defaults (OpenTelemetry, HealthChecks, ServiceDiscovery)
// =========================================================================
builder.AddServiceDefaults();

// =========================================================================
// 2. PostgreSQL DbContext через Aspire Component
//    Підключення "catalogdb" автоматично інжектується оркестратором AppHost!
// =========================================================================
builder.AddNpgsqlDbContext<CatalogDbContext>("catalogdb");

// =========================================================================
// 3. Реєстрація MVC контролерів та Swagger/OpenAPI
// =========================================================================
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Catalog API - Aspire Fundamentals",
        Version = "v1",
        Description = "REST API каталогу товарів з повноцінними контролерами ASP.NET Core та PostgreSQL під керуванням .NET Aspire."
    });
});

var app = builder.Build();

// Ендпоінти здоров'я (/health, /alive)
app.MapDefaultEndpoints();

// Підключення Swagger UI
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Catalog API v1");
        c.RoutePrefix = string.Empty; // Swagger доступний одразу в корені
    });

    app.MapGet("/swagger", () => Results.Redirect("/"));
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
