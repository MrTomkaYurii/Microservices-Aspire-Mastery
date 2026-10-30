using System.Reflection;
using Catalog.Data;
using Catalog.Endpoints;
using Catalog.Services;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.AddServiceDefaults();

builder.AddNpgsqlDbContext<CatalogDbContext>(connectionName: "catalogdb");

builder.Services.AddScoped<ProductService>();

builder.Services.AddMassTransitWithAssemblies(Assembly.GetExecutingAssembly());

// Реєстрація контролерів та Swagger UI
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Catalog API - eShop Platform (.NET 10)",
        Version = "v1",
        Description = "Сервіс каталогу товарів eShop з повноцінними контролерами ASP.NET Core та PostgreSQL."
    });
});

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Catalog API v1");
        c.RoutePrefix = string.Empty;
    });

    app.MapGet("/swagger", () => Results.Redirect("/"));
}

app.UseHttpsRedirection();

app.UseMigration();

app.MapControllers();
app.MapProductEndpoints(); // Збережено для сумісності з іншими викликами

app.Run();
