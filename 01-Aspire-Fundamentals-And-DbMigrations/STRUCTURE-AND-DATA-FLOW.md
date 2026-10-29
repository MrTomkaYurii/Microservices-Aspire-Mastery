# 01. Архітектура, структура проєктів та рух даних: Aspire Fundamentals & DbMigrator

> **Детальний технічний опис структури модулю, дерева файлів, життєвого циклу процесів, взаємодії класів та збереження стану в PostgreSQL.**

---

## 🌲 Дерево файлової структури модуля

```text
01-Aspire-Fundamentals-And-DbMigrations/
├── 01-Aspire-Fundamentals.slnx           # Рішення .NET 10, що об'єднує всі проєкти модуля
├── README.md                              # Оглядовий посібник модуля
├── STRUCTURE-AND-DATA-FLOW.md             # ЦЕЙ ДОКУМЕНТ: повна технічна специфікація
│
├── Aspire.AppHost/                        # Точка входу оркестрації .NET Aspire
│   ├── AppHost.cs                         # Конфігурація топології: PostgreSQL, черговість запуску (WaitFor/WaitForCompletion)
│   ├── Aspire.AppHost.csproj              # Залежності від Aspire.Hosting.AppHost та Aspire.Hosting.PostgreSQL
│   └── appsettings.json                   # Налаштування хоста та логування
│
├── Aspire.ServiceDefaults/                # Загальні бібліотечні стандарти для всіх мікросервісів
│   ├── Extensions.cs                      # Налаштування OpenTelemetry, HealthChecks (/health, /alive), ServiceDiscovery, Resiliency
│   └── Aspire.ServiceDefaults.csproj      # Пакетні посилання на OpenTelemetry та розширення хостингу
│
├── Catalog.DbMigrator/                    # Виробничий воркер безпечних міграцій БД
│   ├── Program.cs                         # Застосування EnsureCreated/Migrate, сідинг 5 початкових товарів, код виходу 0
│   └── Catalog.DbMigrator.csproj          # Посилання на Catalog.Api (доступ до DbContext) та Npgsql
│
├── Catalog.Api/                           # Мікросервіс каталогу товарів (REST API)
│   ├── Program.cs                         # Налаштування DI, Controllers, Swagger, підключення PostgreSQL
│   ├── Catalog.Api.csproj                 # Web SDK, Aspire.Npgsql.EntityFrameworkCore.PostgreSQL, Swagger, лінк на Docs
│   ├── Controllers/
│   │   └── ProductsController.cs          # ASP.NET Core контролер (GET /api/products, POST /api/products)
│   └── Data/
│       └── CatalogDbContext.cs            # EF Core DbContext та сутність Product для PostgreSQL
│
└── Client.Web/                            # Клієнтський веб-застосунок (Blazor WebApp)
    ├── Program.cs                         # Налаштування Blazor Server, Razor Components, реєстрація CatalogApiClient
    ├── CatalogApiClient.cs                # HTTP-клієнт, що звертається до "https+http://catalog-api" через Service Discovery
    ├── Client.Web.csproj                  # Web SDK, посилання на ServiceDefaults
    └── Components/
        ├── App.razor                      # Кореневий HTML-шаблон Blazor
        ├── Routes.razor                   # Маршрутизація сторінок
        ├── Layout/
        │   ├── MainLayout.razor           # Основна розмітка вітрини
        │   └── NavMenu.razor              # Бокове меню з посиланням на /products
        └── Pages/
            ├── Home.razor                 # Головна сторінка
            ├── Products.razor             # Таблиця товарів, завантажених з Catalog.Api
            └── Counter.razor              # Інтерактивний лічильник
```

---

## 🧩 Взаємодія класів та сервісів (Dependency Graph)

```mermaid
classDiagram
    class AppHost {
        +DistributedApplicationBuilder builder
        +AddPostgres("postgres")
        +AddDatabase("catalogdb")
        +AddProject<Catalog_DbMigrator>()
        +AddProject<Catalog_Api>()
        +AddProject<Client_Web>()
    }

    class CatalogDbContext {
        +DbSet~Product~ Products
        +OnModelCreating()
    }

    class Product {
        +Guid Id
        +string Name
        +string Category
        +string Description
        +decimal Price
        +int StockQuantity
        +DateTime CreatedAtUtc
    }

    class DbMigratorProgram {
        +Main()
        +EnsureCreatedAsync()
        +SeedInitialProducts()
    }

    class ProductsController {
        -CatalogDbContext _db
        -ILogger _logger
        +GetAll() Task~ActionResult~
        +GetById(id) Task~ActionResult~
        +Create(product) Task~ActionResult~
    }

    class CatalogApiClient {
        -HttpClient _httpClient
        +GetProductsAsync() Task~ProductItem[]~
    }

    class ProductsRazor {
        -CatalogApiClient CatalogApi
        -ProductItem[] products
        +OnInitializedAsync()
    }

    AppHost ..> DbMigratorProgram : Запускає першим (WaitFor PostgreSQL)
    AppHost ..> ProductsController : Запускає другим (WaitForCompletion Migrator)
    AppHost ..> ProductsRazor : Запускає третім (WaitFor CatalogApi)

    CatalogDbContext o-- Product : містить сутності
    DbMigratorProgram --> CatalogDbContext : застосовує міграцію і наповнює даними
    ProductsController --> CatalogDbContext : читає/пише товари через EF Core
    ProductsRazor --> CatalogApiClient : інжектує для отримання даних
    CatalogApiClient --> ProductsController : викликає HTTP GET через Service Discovery
```

---

## 🔄 Покроковий рух даних (Data Flow)

### Фаза 1: Запуск та ініціалізація кластера (Життєвий цикл)
```mermaid
sequenceDiagram
    autonumber
    participant Host as Aspire AppHost
    participant PG as PostgreSQL (Docker)
    participant Migrator as Catalog.DbMigrator
    participant API as Catalog.Api
    participant Web as Client.Web

    Host->>PG: 1. Піднімає контейнер postgres:latest
    Note over Host,PG: Перевірка готовності порту 5432
    Host->>Migrator: 2. Запускає воркер міграцій
    Migrator->>PG: 3. Підключається через "catalogdb" рядок з'єднання
    Migrator->>PG: 4. EnsureCreatedAsync() (створює таблицю "Products")
    Migrator->>PG: 5. Якщо порожньо -> InsertRange(5 товарів)
    Migrator-->>Host: 6. Воркер завершує роботу з кодом Exit Code: 0
    Note over Host,API: Умова WaitForCompletion(migrator) виконана!
    Host->>API: 7. Запускає мікросервіс Catalog.Api
    API->>PG: 8. Перевіряє HealthCheck бази даних
    Host->>Web: 9. Запускає Blazor фронтенд Client.Web
```

### Фаза 2: Обробка користувацького запиту (Runtime Flow)
```mermaid
sequenceDiagram
    autonumber
    actor User as 👤 Користувач
    participant Browser as 🌐 Браузер
    participant Blazor as Client.Web (Products.razor)
    participant Client as CatalogApiClient
    participant SD as Aspire Service Discovery
    participant API as Catalog.Api
    participant DB as PostgreSQL ("catalogdb")

    User->>Browser: Переходить за адресою /products
    Browser->>Blazor: Відкриває сторінку
    Blazor->>Client: OnInitializedAsync -> GetProductsAsync()
    Client->>SD: Резолв логічного імені "catalog-api"
    SD-->>Client: Фізичний адрес: http://localhost:5214
    Client->>API: HTTP GET http://localhost:5214/api/products
    API->>DB: EF Core: SELECT "Id", "Name", "Price", ... FROM "Products"
    DB-->>API: 5 записів товарів
    API-->>Client: HTTP 200 OK (JSON масив товарів)
    Client-->>Blazor: ProductItem[]
    Blazor-->>Browser: Рендерить HTML-таблицю з товарами
    Browser-->>User: Відображає вітрину з цінами та залишками
```

---

## 💾 Де і що зберігається (Storage & State)

| Рівень збереження | Технологія / Носій | Назва та структура | Опис даних |
| :--- | :--- | :--- | :--- |
| **База даних** | PostgreSQL 16/17 (Docker) | База: `catalogdb`<br>Схема: `public` | Персистентні реляційні дані каталогу товарів e-commerce. |
| **Таблиця** | PostgreSQL Table | Таблиця: `"Products"` | Стовпці:<br>• `"Id"` (UUID, Primary Key)<br>• `"Name"` (VARCHAR 150, Not Null)<br>• `"Category"` (VARCHAR 50, Not Null)<br>• `"Description"` (TEXT)<br>• `"Price"` (NUMERIC 18, 2)<br>• `"StockQuantity"` (INTEGER)<br>• `"CreatedAtUtc"` (TIMESTAMP WITH TIME ZONE) |
| **Фізичний том** | Docker Data Volume | Persistent Volume у Docker | Гарантує збереження бази даних між перезапусками контейнерів Aspire (`WithDataVolume()`). |
| **Клієнтський стан** | Blazor Server Memory | Сигнали SignalR / DOM | Кешування результату рендеру на 5 секунд директивою `@attribute [OutputCache(Duration = 5)]`. |

---

## ⚠️ Критичні точки та архітектурні нюанси

1. **Чому не `dbContext.Database.Migrate()` у `Program.cs` API?**
   * При горизонтальному масштабуванні (наприклад, запуск 3-5 реплік `catalog-api` у Kubernetes/Docker) екземпляри почнуть конкурувати за блокування схеми БД, викликаючи дедлоки (`DeadlockDetectedException`) або падіння частини реплік.
   * Винесення в `Catalog.DbMigrator` гарантує виконання міграцій строго в **одному** процесі **до** того, як API почне приймати трафік.
2. **Як працює резолв `https+http://catalog-api`?**
   * Префікс `https+http://` вказує бібліотеці `Microsoft.Extensions.ServiceDiscovery` спочатку спробувати знайти захищений HTTPS ендпоінт сервісу з ім'ям `catalog-api`, а за його відсутності — перемкнутися на HTTP.
   * Номер порту призначається оркестратором динамічно, тому в коді сервісів немає жодного жорстко закодованого `localhost:5001`.
