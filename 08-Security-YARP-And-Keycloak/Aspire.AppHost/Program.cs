var builder = DistributedApplication.CreateBuilder(args);

// =========================================================================================
// 🎯 МОДУЛЬ 08: ОРКЕСТРАЦІЯ БЕЗПЕКИ, YARP API GATEWAY ТА KEYCLOAK
// =========================================================================================

// 1. Контейнер Identity Provider - Keycloak (OAuth2 / OpenID Connect)
//    Запускається в режимі розробки зі стандартними обліковими даними admin/admin.
var keycloak = builder.AddContainer("keycloak", "quay.io/keycloak/keycloak:26.0")
    .WithHttpEndpoint(port: 8080, targetPort: 8080, name: "keycloak-http")
    .WithEnvironment("KEYCLOAK_ADMIN", "admin")
    .WithEnvironment("KEYCLOAK_ADMIN_PASSWORD", "admin")
    .WithArgs("start-dev");

// 2. Внутрішній захищений мікросервіс Protected.Api (не доступний зовні напряму)
var protectedApi = builder.AddProject<Projects.Protected_Api>("protected-api");

// 3. YARP API Gateway - єдина публічна точка входу в систему
//    Виконує маршрутизацію, Rate Limiting, перевірку JWT та Token Propagation
builder.AddProject<Projects.YarpGateway>("yarp-gateway")
    .WithReference(protectedApi)
    .WaitFor(protectedApi);

builder.Build().Run();
