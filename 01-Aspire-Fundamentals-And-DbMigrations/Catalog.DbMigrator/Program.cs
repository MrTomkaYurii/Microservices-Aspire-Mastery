using Catalog.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// =========================================================================
// 1. Aspire Service Defaults (OpenTelemetry, логирование)
// =========================================================================
builder.AddServiceDefaults();

// =========================================================================
// 2. Підключення до БД через Aspire компонент
// =========================================================================
builder.AddNpgsqlDbContext<CatalogDbContext>("catalogdb");

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger("Catalog.DbMigrator");
    var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

    logger.LogInformation("==================================================================");
    logger.LogInformation("🚀 [DbMigrator] Початок застосування міграцій для 'catalogdb'...");
    logger.LogInformation("==================================================================");

    try
    {
        // Створюємо схему БД, якщо вона ще не створена
        await dbContext.Database.EnsureCreatedAsync();
        logger.LogInformation("✅ [DbMigrator] Схема бази даних успішно перевірена / створена.");

        // Заповнюємо тестовими даними (Seeding), якщо таблиця порожня
        if (!await dbContext.Products.AnyAsync())
        {
            logger.LogInformation("🌱 [DbMigrator] Таблиця Products порожня. Виконується наповнення початковими даними...");

            var sampleProducts = new List<Product>
            {
                new()
                {
                    Name = "Смарт-годинник Pro Pulse",
                    Category = "Wearables",
                    Description = "AMOLED дисплей, GPS, моніторинг серцевого ритму та кисню, автономність 14 днів.",
                    Price = 199.99m,
                    StockQuantity = 50
                },
                new()
                {
                    Name = "Механічна клавіатура CyberClick RGB",
                    Category = "Peripherals",
                    Description = "Бездротова, перемикачі Hot-Swap Red, алюмінієвий корпус, RGB підсвічування.",
                    Price = 129.50m,
                    StockQuantity = 80
                },
                new()
                {
                    Name = "Бездротова миша UltraGlide 4K",
                    Category = "Peripherals",
                    Description = "Сенсор PixArt 26k DPI, частота опитування 4000Hz, вага лише 54 грами.",
                    Price = 79.99m,
                    StockQuantity = 120
                },
                new()
                {
                    Name = "4K Монітор StudioView 32\"",
                    Category = "Monitors",
                    Description = "IPS матриця, 100% sRGB, Type-C Power Delivery 90W, тонкі рамки.",
                    Price = 549.00m,
                    StockQuantity = 25
                },
                new()
                {
                    Name = "Навушники з ANC SoundShield 900",
                    Category = "Audio",
                    Description = "Активне шумозаглушення, кодеки LDAC/AAC, 40 годин відтворення від одного заряду.",
                    Price = 249.90m,
                    StockQuantity = 60
                }
            };

            await dbContext.Products.AddRangeAsync(sampleProducts);
            await dbContext.SaveChangesAsync();

            logger.LogInformation("✅ [DbMigrator] Успішно додано {Count} початкових товарів до каталогу.", sampleProducts.Count);
        }
        else
        {
            logger.LogInformation("ℹ️ [DbMigrator] База даних вже містить товари. Наповнення пропущено.");
        }

        logger.LogInformation("==================================================================");
        logger.LogInformation("🎉 [DbMigrator] Міграція та наповнення БД завершено успішно!");
        logger.LogInformation("==================================================================");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "❌ [DbMigrator] Помилка під час міграції бази даних.");
        throw;
    }
}

// Завершуємо роботу успішно з кодом 0 (щоб WaitForCompletion в AppHost розблокував запуск API)
return 0;
