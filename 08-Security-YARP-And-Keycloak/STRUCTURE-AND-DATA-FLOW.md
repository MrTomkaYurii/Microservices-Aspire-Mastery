# 08. Архітектура, структура проєктів та рух даних: Security, YARP API Gateway & Keycloak

> **Повна технічна специфікація побудови захищеного периметра мікросервісів на .NET 10 та .NET Aspire: зворотний проксі `YARP` із динамічним `Service Discovery`, сервер автентифікації `Keycloak 26.0` (OAuth2 / OIDC), захист `FixedWindow Rate Limiting`, патерн `Token & Claim Propagation` та рольова авторизація `RBAC`.**

---

## 🏛️ Загальна архітектурна схема та потік безпеки

```mermaid
flowchart TD
    subgraph ExternalWorld ["Зовнішня мережа (Untrusted Zone)"]
        Browser["🌐 Веб-браузер / Мобільний клієнт / Swagger UI"]
    end

    subgraph IdentityProvider ["Сервер ідентичності (Docker)"]
        Keycloak["🔐 Keycloak 26.0 (Порт: 8080)<br/>OAuth2 / OIDC Realms, Users & JWT Tokens"]
    end

    subgraph GatewayZone ["Демілітаризована зона (DMZ): YarpGateway (.NET 10)"]
        Step1["Step1_GatewayStatusController<br/>(/api/step1-gateway/routes)"]
        RateLimit["RateLimiter Middleware<br/>(50 запитів / хвилину, QueueLimit = 10)"]
        PropMiddleware["Identity Propagation Middleware<br/>(Інжекція X-User-Id, X-User-Email, X-User-Role, X-Correlation-Id)"]
        YarpEngine["YARP Reverse Proxy + Service Discovery<br/>(Кластер: 'https+http://protected-api')"]
    end

    subgraph InternalZone ["Внутрішній захищений контур: Protected.Api (.NET 10)"]
        Step2["Step2_PublicEndpointsController<br/>(/api/step2-public/catalog)"]
        Step3["Step3_TokenPropagationController<br/>(/api/step3-propagation/user-profile)"]
        Step4["Step4_RoleBasedAccessController<br/>(/api/step4-rbac/orders та /system/purge-cache)"]
    end

    Browser -->|"1. Автентифікація OAuth2 / OIDC"| Keycloak
    Browser -->|"2. Огляд маршрутів шлюзу"| Step1
    Browser -->|"3. Запит до /proxy/public/* або /proxy/secure/*"| RateLimit
    RateLimit --> PropMiddleware --> YarpEngine

    YarpEngine -->|"PathRemovePrefix: /proxy/public"| Step2
    YarpEngine -->|"PathRemovePrefix: /proxy/secure + Headers"| Step3
    YarpEngine -->|"PathRemovePrefix: /proxy/secure + Headers"| Step4
```

---

## 🌲 Дерево файлової структури модуля

```text
08-Security-YARP-And-Keycloak/
├── 08-Security-YARP-And-Keycloak.slnx            # Рішення .NET 10 для модуля безпеки та YARP
├── README.md                                     # Посібник користувача з покроковими тестами
├── STRUCTURE-AND-DATA-FLOW.md                    # ЦЕЙ ДОКУМЕНТ: повна технічна специфікація
│
├── Aspire.AppHost/                               # Оркестратор .NET Aspire
│   ├── Program.cs                                # Запуск контейнера Keycloak (8080), Protected.Api та YarpGateway
│   ├── Aspire.AppHost.csproj                     # Aspire.Hosting.AppHost
│   └── appsettings.json                          # Конфігурація оркестратора
│
├── Aspire.ServiceDefaults/                       # Спільні стандарти телеметрії та живучості
│   ├── Extensions.cs                             # OpenTelemetry, HealthChecks, ServiceDiscovery
│   └── Aspire.ServiceDefaults.csproj             # Пакетні посилання
│
├── YarpGateway/                                  # Зовнішній API Gateway (Swagger UI у корені /)
│   ├── Program.cs                                # Налаштування RateLimiter, YARP + ServiceDiscovery, Token Propagation, Swagger
│   ├── YarpGateway.csproj                        # Yarp.ReverseProxy, Microsoft.Extensions.ServiceDiscovery.Yarp, Swashbuckle
│   ├── appsettings.json                          # Конфігурація ReverseProxy: Routes (public-route, secure-route) та Clusters
│   └── Controllers/
│       └── Step1_GatewayStatusController.cs      # КРОК 1: Інспекція маршрутів YARP та параметрів Keycloak
│
└── Protected.Api/                                # Внутрішній захищений мікросервіс (Swagger UI у корені /)
    ├── Program.cs                                # Налаштування контролерів та Swagger UI
    ├── Protected.Api.csproj                      # Swashbuckle, ServiceDefaults, лінк на Docs
    └── Controllers/
        ├── Step2_PublicEndpointsController.cs    # КРОК 2: Публічна вітрина товарів без вимоги авторизації
        ├── Step3_TokenPropagationController.cs   # КРОК 3: Читання прокинутих клеймів ідентичності користувача
        └── Step4_RoleBasedAccessController.cs    # КРОК 4: Перевірка ролей User vs Admin (RBAC)
```

---

## 🧩 Взаємодія класів та конвеєрів безпеки (Dependency Graph)

```mermaid
classDiagram
    class AppHost {
        +AddContainer("keycloak", "quay.io/keycloak/keycloak:26.0")
        +AddProject~Protected_Api~("protected-api")
        +AddProject~YarpGateway~("yarp-gateway")
        +WithReference(protectedApi)
    }

    class YarpGatewayProgram {
        +AddRateLimiter("fixed", 50 per min)
        +AddReverseProxy()
        +AddServiceDiscoveryDestinationResolver()
        +IdentityPropagationMiddleware()
        +MapReverseProxy()
    }

    class Step1_GatewayStatusController {
        -IConfiguration _configuration
        +GetGatewayRoutes() IActionResult
    }

    class Step2_PublicEndpointsController {
        +GetPublicCatalog() IActionResult
    }

    class Step3_TokenPropagationController {
        +GetUserProfile() IActionResult
    }

    class Step4_RoleBasedAccessController {
        +GetUserOrders() IActionResult
        +PurgeSystemCache() IActionResult
    }

    AppHost ..> YarpGatewayProgram : Запускає вхідний шлюз та інжектує адресу protected-api
    AppHost ..> Step2_PublicEndpointsController : Запускає внутрішній сервіс Protected.Api
    YarpGatewayProgram --> Step1_GatewayStatusController : Локальний контролер діагностики шлюзу
    YarpGatewayProgram ==> Step2_PublicEndpointsController : Проксіює /proxy/public/*
    YarpGatewayProgram ==> Step3_TokenPropagationController : Проксіює /proxy/secure/* + заголовки X-User-*
    YarpGatewayProgram ==> Step4_RoleBasedAccessController : Проксіює /proxy/secure/* + заголовки X-User-Role
```

---

## 🔄 Покроковий рух даних: 4 навчальні етапи

### 📘 КРОК 1 та КРОК 2: Маршрутизація YARP, Rate Limiting та Публічні ендпоінти

```mermaid
sequenceDiagram
    autonumber
    actor Client as 👤 Гість / Браузер
    participant YARP as YarpGateway (RateLimiter + Proxy)
    participant SD as Aspire Service Discovery
    participant Protected as Protected.Api (Step2_PublicEndpointsController)

    Client->>YARP: GET /proxy/public/api/step2-public/catalog
    YARP->>YARP: 1. Перевірка ліміту FixedWindowLimiter (<= 50 запитів/хв)
    alt Ліміт перевищено (> 50 запитів/хв)
        YARP-->>Client: 429 Too Many Requests (Захист від DDoS спрацював!)
    else Ліміт у нормі
        YARP->>YARP: 2. Зіставлення маршруту "public-route" (/proxy/public/{**catch-all})
        YARP->>YARP: 3. Трансформація PathRemovePrefix("/proxy/public") -> "/api/step2-public/catalog"
        YARP->>SD: 4. Резолв кластера "https+http://protected-api"
        SD-->>YARP: Фізичний порт внутрішнього сервісу
        YARP->>Protected: 5. Проксіювання HTTP GET /api/step2-public/catalog
        Protected-->>YARP: 200 OK (JSON список ноутбуків і навушників)
        YARP-->>Client: 200 OK
    end
```

---

### 📘 КРОК 3: Патерн Token & Claim Propagation (Gateway Offloading)

```mermaid
sequenceDiagram
    autonumber
    actor Client as 👤 Авторизований Користувач
    participant KC as 🔐 Keycloak (OIDC Server)
    participant YARP as YarpGateway (Identity Middleware)
    participant Protected as Protected.Api (Step3_TokenPropagationController)

    Client->>KC: 1. POST /realms/master/protocol/openid-connect/token (Логін + Пароль)
    KC-->>Client: 2. Підписаний JWT Access Token
    
    Client->>YARP: 3. GET /proxy/secure/api/step3-propagation/user-profile (Header: X-Mock-Role: User)
    Note over YARP: Шлюз виконує перевірку токена ОДИН РАЗ на вході в периметр!
    YARP->>YARP: 4. Інжектує перевірені заголовки:<br/>X-User-Id: usr_simulated_777<br/>X-User-Email: dev@mastery.com<br/>X-User-Role: User<br/>X-Correlation-Id: 9a8b7c...
    YARP->>Protected: 5. Перенаправляє запит із заголовками у приватну мережу
    Protected->>Protected: 6. Миттєво зчитує Request.Headers["X-User-*"] без RSA-дешифрування
    Protected-->>YARP: 200 OK { IsAuthenticated: true, SecurityContext: { ... } }
    YARP-->>Client: 200 OK
```

---

### 📘 КРОК 4: Рольовий контроль доступу (RBAC: `Guest` vs `User` vs `Admin`)

```mermaid
sequenceDiagram
    autonumber
    actor User as 👤 Користувач
    participant YARP as YarpGateway
    participant RBAC as Protected.Api (Step4_RoleBasedAccessController)

    rect rgb(255, 240, 240)
        Note over User,RBAC: Сценарій А: Спроба звичайного покупця (Role = User) очистити системний кеш
        User->>YARP: DELETE /proxy/secure/api/step4-rbac/system/purge-cache (X-Mock-Role: User)
        YARP->>RBAC: Проксіює із заголовком X-User-Role: User
        RBAC->>RBAC: Перевіряє: role == "Admin"? -> Ні!
        RBAC-->>YARP: 403 Forbidden (Потрібні права системного адміністратора)
        YARP-->>User: 403 Forbidden
    end

    rect rgb(235, 255, 235)
        Note over User,RBAC: Сценарій Б: Виконання операції адміністратором (Role = Admin)
        User->>YARP: DELETE /proxy/secure/api/step4-rbac/system/purge-cache (X-Mock-Role: Admin)
        YARP->>RBAC: Проксіює із заголовком X-User-Role: Admin
        RBAC->>RBAC: Перевіряє: role == "Admin"? -> Так!
        RBAC-->>YARP: 200 OK (Адміністративну операцію purge-cache виконано)
        YARP-->>User: 200 OK
    end
```

---

## 💾 Таблиця маршрутів, політик та заголовків безпеки

| Елемент конфігурації | Значення в системі | Де налаштовано | Призначення |
| :--- | :--- | :--- | :--- |
| **Маршрут `public-route`** | `/proxy/public/{**catch-all}` | `YarpGateway/appsettings.json` | Відрізає префікс `/proxy/public` і пересилає на `https+http://protected-api` |
| **Маршрут `secure-route`** | `/proxy/secure/{**catch-all}` | `YarpGateway/appsettings.json` | Відрізає префікс `/proxy/secure`, збагачує заголовками ідентичності та пересилає на `protected-api` |
| **Політика Rate Limiting** | `FixedWindowLimiter ("fixed")` | `YarpGateway/Program.cs` | Дозволяє до `50` запитів за `1 хвилину` (черга очікування `10`), при перевищенні повертає `429 Too Many Requests` |
| **Заголовки ідентичності** | `X-User-Id`, `X-User-Email`, `X-User-Role`, `X-Correlation-Id` | `YarpGateway/Program.cs` | Передача перевіреного контексту користувача та трасування у внутрішні мікросервіси |
| **Keycloak IDP** | `quay.io/keycloak/keycloak:26.0` | `Aspire.AppHost/Program.cs` | OAuth2 / OIDC сервер на порту `8080` (`admin` / `admin`) |
