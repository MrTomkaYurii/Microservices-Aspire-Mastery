# 09. Архітектура, структура проєктів та рух даних: Distributed Testing (`Aspire.Hosting.Testing`)

> **Повна технічна специфікація автоматизованого інтеграційного тестування мікросервісних кластерів на .NET 10 за допомогою `Aspire.Hosting.Testing` та `xUnit`: програмний життєвий цикл оркестратора, динамічний розподіл портів, клієнтський Service Discovery та верифікація проб живучості.**

---

## 🏛️ Загальна архітектурна схема тестового конвеєра

```mermaid
flowchart TD
    subgraph TestRunner ["Тестовий раннер (dotnet test / xUnit 2.9)"]
        Test1["Step1_ClusterLifecycleTests<br/>(Cluster_StartsAndStopsCleanly)"]
        Test2["Step2_HttpEndpointIntegrationTests<br/>(GetAllItems & CreateItem E2E)"]
        Test3["Step3_HealthCheckClusterTests<br/>(Liveness /alive & Readiness /health)"]
    end

    subgraph AspireTestingEngine ["Рушій Aspire.Hosting.Testing"]
        Builder["DistributedApplicationTestingBuilder<br/>.CreateAsync~Projects.Aspire_AppHost~()"]
        InMemCluster["Ізольований екземпляр DistributedApplication<br/>(Динамічні порти без конфліктів)"]
    end

    subgraph ClusterServices ["Цільові сервіси кластера (SUT)"]
        AppHost["Aspire.AppHost"]
        SampleApi["Sample.Api (.NET 10)<br/>Step1_ItemsController + HealthChecks + Swagger UI"]
    end

    Test1 --> Builder
    Test2 --> Builder
    Test3 --> Builder

    Builder --> InMemCluster
    InMemCluster --> AppHost
    AppHost --> SampleApi

    Test2 ==>|"app.CreateHttpClient('sample-api') -> GET / POST /api/step1-items"| SampleApi
    Test3 ==>|"app.CreateHttpClient('sample-api') -> GET /alive, /health"| SampleApi
```

---

## 🌲 Дерево файлової структури модуля

```text
09-Distributed-Testing/
├── 09-Distributed-Testing.slnx                   # Рішення .NET 10 для модуля розподіленого тестування
├── README.md                                     # Посібник користувача із запуску автотестів та Swagger
├── STRUCTURE-AND-DATA-FLOW.md                    # ЦЕЙ ДОКУМЕНТ: повна технічна специфікація
│
├── Aspire.AppHost/                               # Оркестратор кластера .NET Aspire
│   ├── Program.cs                                # Реєстрація цільового мікросервісу builder.AddProject<Projects.Sample_Api>("sample-api")
│   ├── Aspire.AppHost.csproj                     # Aspire.Hosting.AppHost
│   └── appsettings.json                          # Конфігурація хоста
│
├── Aspire.ServiceDefaults/                       # Спільні стандарти мікросервісів
│   ├── Extensions.cs                             # Налаштування OpenTelemetry, ServiceDiscovery та ендпоінтів /alive, /health
│   └── Aspire.ServiceDefaults.csproj             # Пакетні посилання
│
├── Sample.Api/                                   # Цільовий сервіс під тестуванням (Swagger UI у корені /)
│   ├── Program.cs                                # Конфігурація DI, Controllers, HealthChecks та Swagger UI
│   ├── Sample.Api.csproj                         # Swashbuckle, посилання на ServiceDefaults та Docs
│   └── Controllers/
│       └── Step1_ItemsController.cs              # CRUD-контролер каталогу (/api/step1-items) з потокобезпечним сховищем
│
└── Tests.ClusterIntegration/                     # Інтеграційний тестовий проєкт (xUnit + Aspire.Hosting.Testing)
    ├── Tests.ClusterIntegration.csproj           # Aspire.Hosting.Testing, xunit, посилання на Aspire.AppHost
    ├── Step1_ClusterLifecycleTests.cs            # КРОК 1: Тест ініціалізації, запуску та чистої зупинки кластера
    ├── Step2_HttpEndpointIntegrationTests.cs     # КРОК 2: E2E HTTP-тести читання та створення елементів через Service Discovery
    └── Step3_HealthCheckClusterTests.cs          # КРОК 3: Перевірка готовності інфраструктурних проб /alive та /health
```

---

## 🧩 Взаємодія класів тестового стенду та цільового API (Dependency Graph)

```mermaid
classDiagram
    class DistributedApplicationTestingBuilder {
        +CreateAsync~TEntryPoint~() Task~IDistributedApplicationTestingBuilder~
        +BuildAsync() Task~DistributedApplication~
    }

    class DistributedApplication {
        +StartAsync() Task
        +StopAsync() Task
        +CreateHttpClient(resourceName) HttpClient
        +DisposeAsync() ValueTask
    }

    class Step1_ClusterLifecycleTests {
        +Cluster_StartsAndStopsCleanly_WithoutExceptions() Task
    }

    class Step2_HttpEndpointIntegrationTests {
        +GetAllItems_ReturnsSuccessAndInitialData() Task
        +CreateItem_ReturnsCreatedAndCanBeRetrieved() Task
    }

    class Step3_HealthCheckClusterTests {
        +LivenessEndpoint_Returns200Healthy() Task
        +ReadinessEndpoint_Returns200Healthy() Task
    }

    class Step1_ItemsController {
        -ConcurrentDictionary Items
        +GetAll() IActionResult
        +GetById(id) IActionResult
        +Create(request) IActionResult
        +Delete(id) IActionResult
    }

    Step1_ClusterLifecycleTests --> DistributedApplicationTestingBuilder : Створює тестовий хост
    Step2_HttpEndpointIntegrationTests --> DistributedApplicationTestingBuilder : Створює тестовий хост
    Step3_HealthCheckClusterTests --> DistributedApplicationTestingBuilder : Створює тестовий хост

    DistributedApplicationTestingBuilder ..> DistributedApplication : Будує ізольований кластер
    DistributedApplication ==> Step1_ItemsController : Направляє запити HttpClient("sample-api")
```

---

## 🔄 Покроковий рух даних під час виконання E2E-тестів (Data Flow)

### 📘 КРОК 1 та КРОК 2: Підняття кластера в пам'яті та перевірка ланцюжка `POST` $\to$ `GET`

```mermaid
sequenceDiagram
    autonumber
    participant xUnit as 🧪 xUnit Test Runner
    participant Builder as DistributedApplicationTestingBuilder
    participant App as DistributedApplication (AppHost)
    participant Client as HttpClient ("sample-api")
    participant API as Sample.Api (Step1_ItemsController)

    xUnit->>Builder: 1. CreateAsync(Projects.Aspire_AppHost)
    Builder-->>xUnit: Сконфігурований білдер кластера
    xUnit->>App: 2. await appHost.BuildAsync() + await app.StartAsync()
    Note over App,API: Оркестратор піднімає Kestrel для Sample.Api на випадковому вільному порту!
    
    xUnit->>App: 3. app.CreateHttpClient("sample-api")
    App-->>xUnit: HttpClient з підставленим BaseAddress динамічного порту

    xUnit->>Client: 4. PostAsJsonAsync("/api/step1-items", { Name: "Clean Architecture", Price: 79.99 })
    Client->>API: HTTP POST /api/step1-items
    API->>API: Зберігає новий ItemDto у ConcurrentDictionary
    API-->>Client: 201 Created (Повертає створений об'єкт зі згенерованим Guid Id)
    Client-->>xUnit: Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode)

    xUnit->>Client: 5. GetAsync("/api/step1-items/{createdItem.Id}")
    Client->>API: HTTP GET /api/step1-items/{id}
    API-->>Client: 200 OK (JSON знайденого елемента)
    Client-->>xUnit: Assert.Equal(createdItem.Id, retrievedItem.Id)

    xUnit->>App: 6. await using DisposeAsync() -> Автоматичне завершення всіх процесів кластера
```

---

### 📘 КРОК 3: Автоматична верифікація проб живучості (`/alive` та `/health`)

```mermaid
sequenceDiagram
    autonumber
    participant xUnit as 🧪 Step3_HealthCheckClusterTests
    participant App as DistributedApplication
    participant API as Sample.Api (HealthCheck Middleware)

    xUnit->>App: StartAsync() + CreateHttpClient("sample-api")
    
    xUnit->>API: GET /alive (Liveness Probe)
    API-->>xUnit: 200 OK ("Healthy") -> Assert.Equal(HttpStatusCode.OK)

    xUnit->>API: GET /health (Readiness Probe)
    API-->>xUnit: 200 OK ("Healthy") -> Assert.Equal(HttpStatusCode.OK)
```

---

## 💾 Порівняння підходів до тестування мікросервісів

| Критерій | Класичні Unit-тести з `Moq` | Ручний `docker-compose` у CI/CD | **`Aspire.Hosting.Testing`** |
| :--- | :--- | :--- | :--- |
| **Реальність топології** | Низька (тестуються ізольовані класи з фейками). | Висока, але потребує дублювання YAML-файлів. | **100% реальна топологія** з того ж самого C# `AppHost.cs`! |
| **Конфлікти портів** | Відсутні (немає мережі). | Постійні помилки зайнятих портів при паралельному запуску. | **Повна ізоляція:** кожен тест отримує власні динамічні порти. |
| **Зручність дебагу** | Легко (`F5` у Visual Studio). | Складно підключитися дебагером до зовнішнього контейнера. | **Ідеально:** ставте Breakpoint прямо в контролері API і тисніть `Debug Test`! |
