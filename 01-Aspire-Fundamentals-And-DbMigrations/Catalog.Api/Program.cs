using Catalog.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// =========================================================================
// 1. Aspire Service Defaults (OpenTelemetry, HealthChecks, ServiceDiscovery)
// =========================================================================
builder.AddServiceDefaults();

// =========================================================================
// 2. PostgreSQL DbContext через Aspire Component
//    Підключення "catalogdb" автоматично інжектується з AppHost!
// =========================================================================
builder.AddNpgsqlDbContext<CatalogDbContext>("catalogdb");

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// =========================================================================
// 3. Каталог REST API ендпоінти
// =========================================================================
var productsGroup = app.MapGroup("/api/products")
    .WithTags("Products");

// Отримати всі товари
productsGroup.MapGet("/", async (CatalogDbContext db) =>
{
    var products = await db.Products.AsNoTracking().ToListAsync();
    return Results.Ok(products);
})
.WithName("GetAllProducts")
.WithSummary("Отримати перелік усіх товарів каталогу");

// Отримати товар за ID
productsGroup.MapGet("/{id:guid}", async (Guid id, CatalogDbContext db) =>
{
    var product = await db.Products.FindAsync(id);
    return product is not null ? Results.Ok(product) : Results.NotFound($"Товар з ID '{id}' не знайдено.");
})
.WithName("GetProductById")
.WithSummary("Отримати деталі товару за унікальним ідентифікатором");

// Створити новий товар
productsGroup.MapPost("/", async (Product product, CatalogDbContext db) =>
{
    product.Id = Guid.NewGuid();
    product.CreatedAtUtc = DateTime.UtcNow;
    db.Products.Add(product);
    await db.SaveChangesAsync();

    return Results.Created($"/api/products/{product.Id}", product);
})
.WithName("CreateProduct")
.WithSummary("Створити новий товар у каталозі");

app.Run();
