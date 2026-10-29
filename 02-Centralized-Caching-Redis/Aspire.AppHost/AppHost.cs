var builder = DistributedApplication.CreateBuilder(args);

// =========================================================================
// 1. Централізований Redis з інтерактивною веб-панеллю RedisInsight
// =========================================================================
var redis = builder.AddRedis("cache")
    .WithRedisInsight(insight => insight.WithUrlForEndpoint("http", url => url.DisplayText = "Redis Insight UI"))
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

// =========================================================================
// 2. Сервіс кошика Basket.Api
//    Використовує HybridCache (L1 в пам'яті + L2 в Redis) та підписується
//    на Redis Pub/Sub канал для отримання сигналів інвалідації.
// =========================================================================
var basket = builder.AddProject<Projects.Basket_Api>("basket-api")
    .WithReference(redis)
    .WaitFor(redis);

// =========================================================================
// 3. Сервіс розрахунку цін Pricing.Api
//    Керує цінами та знижками, зберігає актуальні прайси в Redis
//    та публікує подію в Redis Pub/Sub при зміні ціни або знижки.
// =========================================================================
var pricing = builder.AddProject<Projects.Pricing_Api>("pricing-api")
    .WithReference(redis)
    .WaitFor(redis);

builder.Build().Run();
