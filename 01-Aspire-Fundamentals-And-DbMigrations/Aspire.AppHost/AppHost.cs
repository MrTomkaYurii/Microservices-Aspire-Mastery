var builder = DistributedApplication.CreateBuilder(args);

// =========================================================================
// 1. Інфраструктурні ресурси: PostgreSQL
//    Запускається в ізольованому Docker-контейнері з персистентним томом.
// =========================================================================
var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin(pgAdmin => pgAdmin.WithUrlForEndpoint("http", url => url.DisplayText = "PostgreSQL Browser (PgAdmin)"))
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var catalogDb = postgres.AddDatabase("catalogdb");

// =========================================================================
// 2. Воркер міграцій бази даних (DbMigrator)
//    КРИТИЧНИЙ ПАТЕРН: окремий легковесний консольний застосунок,
//    який накладає схему EF Core та сідить тестові дані.
// =========================================================================
var migrator = builder.AddProject<Projects.Catalog_DbMigrator>("catalog-migrator")
    .WithReference(catalogDb)
    .WaitFor(catalogDb);

// =========================================================================
// 3. Мікросервіс Catalog.Api
//    Запускається ТІЛЬКИ ПІСЛЯ ТОГО, як migrator успішно завершився (WaitForCompletion).
//    Це запобігає race conditions та падінням API через нестворені таблиці.
// =========================================================================
var catalogApi = builder.AddProject<Projects.Catalog_Api>("catalog-api")
    .WithReference(catalogDb)
    .WaitForCompletion(migrator);

// =========================================================================
// 4. Веб-інтерфейс Client.Web (Blazor)
//    Звертається до catalog-api через автоматичний Aspire Service Discovery.
// =========================================================================
builder.AddProject<Projects.Client_Web>("client-web")
    .WithExternalHttpEndpoints()
    .WithReference(catalogApi)
    .WaitFor(catalogApi);

builder.Build().Run();
