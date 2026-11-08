# 07. Архітектура, структура проєктів та рух даних: Transactional Patterns, Outbox & Sagas

> **Повна технічна специфікація патернів надійності розподілених транзакцій на .NET 10 та .NET Aspire: аналіз вразливості `Dual-Write`, реалізація `Transactional Outbox` із конвеєром `Polly v8` (`Retry` + `Circuit Breaker`), промисловий `MassTransit EF Core Outbox` та оркестрація `Saga State Machine` із компенсуючими транзакціями.**

---

## 🏛️ Загальна архітектурна схема модуля

```mermaid
flowchart TD
    subgraph ClientLayer ["Зовнішній шар"]
        User["👤 Клієнт / Інженер / Swagger UI (/)"]
    end

    subgraph Orchestration ["Інфраструктура та Оркестрація (.NET Aspire)"]
        AppHost["Aspire.AppHost"]
        Postgres[("🐘 PostgreSQL (checkoutdb)<br/>Таблиці: Orders, OutboxMessages, SagaStates,<br/>InboxState, OutboxState")]
        PgAdmin["🖥️ PgAdmin Web UI"]
        RabbitMQ["🐇 RabbitMQ Broker + Management UI"]
    end

    subgraph Service ["Мікросервіс Checkout.Api (.NET 10)"]
        Step1["Step1_DualWriteProblemController<br/>(/api/step1-dual-write)"]
        Step2["Step2_TransactionalOutboxController<br/>(/api/step2-outbox)"]
        Step3["Step3_MassTransitOutboxController<br/>(/api/step3-masstransit-outbox)"]
        Step4["Step4_SagaStateMachineController<br/>(/api/step4-saga)"]

        PollyWorker["OutboxPublisherWorker<br/>(Polly v8: Retry + Circuit Breaker)"]
        MTEFOutbox["MassTransit Bus Outbox<br/>(AddEntityFrameworkOutbox)"]
    end

    User -->|"Крок 1: Симуляція аварії"| Step1
    User -->|"Крок 2: Атомарний Outbox"| Step2
    User -->|"Крок 3: MassTransit Outbox"| Step3
    User -->|"Крок 4: Сага та Компенсація"| Step4

    Step1 -->|"Небезпечний прямий запис"| Postgres
    Step2 ==>|"Єдина ACID транзакція"| Postgres
    Step3 ==>|"EF Core Outbox транзакція"| Postgres
    Step4 ==>|"Збереження переходів Саги"| Postgres

    Postgres -.->|"Вичитка невідправлених подій"| PollyWorker
    Postgres -.->|"Автоматичний диспатч"| MTEFOutbox

    PollyWorker ==>|"Захищена публікація (Polly v8)"| RabbitMQ
    MTEFOutbox ==>|"Гарантована доставка At-Least-Once"| RabbitMQ
    Postgres -.-> PgAdmin
```

---

## 🌲 Дерево файлової структури модуля

```text
07-Transactional-Patterns-And-Sagas/
├── 07-Transactional-Patterns-And-Sagas.slnx      # Рішення .NET 10 для модуля транзакційних патернів
├── README.md                                     # Посібник користувача зі сценаріями тестування у Swagger
├── STRUCTURE-AND-DATA-FLOW.md                    # ЦЕЙ ДОКУМЕНТ: повна технічна специфікація
│
├── Aspire.AppHost/                               # Оркестратор .NET Aspire
│   ├── Program.cs                                # Підняття PostgreSQL (+PgAdmin), RabbitMQ (+Management) та Checkout.Api
│   ├── Aspire.AppHost.csproj                     # Aspire.Hosting.PostgreSQL + Aspire.Hosting.RabbitMQ
│   └── appsettings.json                          # Конфігурація оркестратора
│
├── Aspire.ServiceDefaults/                       # Спільні стандарти телеметрії
│   ├── Extensions.cs                             # OpenTelemetry, HealthChecks, ServiceDiscovery
│   └── Aspire.ServiceDefaults.csproj             # Пакетні посилання
│
└── Checkout.Api/                                 # Мікросервіс оформлення замовлень (Swagger UI у корені /)
    ├── Program.cs                                # Налаштування EF Core, MassTransit EF Outbox, Polly Worker, Swagger UI
    ├── Checkout.Api.csproj                       # EF Core Npgsql, MassTransit.EntityFrameworkCore, Polly v8, Swashbuckle
    ├── BackgroundServices/
    │   └── OutboxPublisherWorker.cs              # Фоновий сервіс: читає OutboxMessages та публікує через Polly v8
    ├── Controllers/
    │   ├── Step1_DualWriteProblemController.cs   # КРОК 1: Симуляція розсинхронізації між SQL та брокером
    │   ├── Step2_TransactionalOutboxController.cs # КРОК 2: Ручний Transactional Outbox + керування аварією для Polly v8
    │   ├── Step3_MassTransitOutboxController.cs  # КРОК 3: Нативний AddEntityFrameworkOutbox у MassTransit
    │   └── Step4_SagaStateMachineController.cs   # КРОК 4: Життєвий цикл Саги та компенсуючий Rollback
    ├── Data/
    │   └── AppDbContext.cs                       # DbContext: Orders, OutboxMessages, SagaStates + MassTransit Outbox Entities
    └── Entities/
        ├── Order.cs                              # Доменна сутність замовлення
        ├── OutboxMessage.cs                      # Таблиця вихідних подій для кастомного Outbox (Крок 2)
        └── OrderSagaState.cs                     # Сутність стану екземпляра Саги (Крок 4)
```

---

## 🧩 Взаємодія класів та сутностей БД (Dependency Graph)

```mermaid
classDiagram
    class AppHost {
        +AddPostgres("postgres")
        +AddDatabase("checkoutdb")
        +AddRabbitMQ("eventbus")
        +AddProject~Checkout_Api~("checkout-api")
    }

    class AppDbContext {
        +DbSet~Order~ Orders
        +DbSet~OutboxMessage~ OutboxMessages
        +DbSet~OrderSagaState~ SagaStates
        #OnModelCreating(modelBuilder)
    }

    class Order {
        +Guid Id
        +string CustomerId
        +decimal TotalAmount
        +string Status
        +DateTime CreatedAt
    }

    class OutboxMessage {
        +Guid Id
        +string EventType
        +string Payload
        +DateTime CreatedAt
        +DateTime ProcessedAt
        +int RetryCount
        +string ErrorMessage
    }

    class OrderSagaState {
        +Guid CorrelationId
        +string CustomerId
        +decimal Amount
        +string CurrentState
        +bool IsPaymentCaptured
        +bool IsStockReserved
        +string FailureReason
        +DateTime UpdatedAt
    }

    class OutboxPublisherWorker {
        +bool SimulateBrokerOutage
        -ResiliencePipeline _resiliencePipeline
        #ExecuteAsync(stoppingToken) Task
        -ProcessPendingOutboxMessagesAsync(ct) Task
    }

    class Step1_DualWriteProblemController {
        -AppDbContext _dbContext
        +SimulateDualWriteCrash(request) Task~IActionResult~
    }

    class Step2_TransactionalOutboxController {
        -AppDbContext _dbContext
        +AtomicCheckout(request) Task~IActionResult~
        +GetOutboxMessages() Task~IActionResult~
        +ToggleBrokerOutage(isOutageActive) IActionResult
    }

    class Step3_MassTransitOutboxController {
        -AppDbContext _dbContext
        -IPublishEndpoint _publishEndpoint
        +MassTransitCheckout(request) Task~IActionResult~
    }

    class Step4_SagaStateMachineController {
        -AppDbContext _dbContext
        +StartSaga(request) Task~IActionResult~
        +SimulateSagaCompensation(request) Task~IActionResult~
        +GetSagaState(orderId) Task~IActionResult~
    }

    AppDbContext o-- Order
    AppDbContext o-- OutboxMessage
    AppDbContext o-- OrderSagaState

    Step1_DualWriteProblemController --> AppDbContext
    Step2_TransactionalOutboxController --> AppDbContext
    Step2_TransactionalOutboxController ..> OutboxPublisherWorker : Керує прапорцем SimulateBrokerOutage
    OutboxPublisherWorker --> AppDbContext : Опитує таблицю OutboxMessages кожні 2 с
    Step3_MassTransitOutboxController --> AppDbContext : Зберігає Order + MassTransit OutboxMessage
    Step4_SagaStateMachineController --> AppDbContext : Оновлює стани OrderSagaState
```

---

## 🔄 Покроковий рух даних: 4 навчальні етапи

### 📘 КРОК 1: Чому виникає катастрофа Dual-Write?

```mermaid
sequenceDiagram
    autonumber
    actor Client as 👤 Покупець / Swagger
    participant API as Step1_DualWriteProblemController
    participant DB as 🐘 PostgreSQL (checkoutdb)
    participant Broker as 🐇 RabbitMQ Broker

    Client->>API: POST /api/step1-dual-write/simulate-crash { totalAmount: 500, simulateBrokerFailure: true }
    API->>DB: 1. INSERT INTO "Orders" (Status = "PendingPayment")
    DB-->>API: Транзакція БД закоммічена успішно!
    
    API--xBroker: 2. Спроба відправити подію OrderCreatedEvent -> 💥 ЗБІЙ МЕРЕЖІ!
    Note over DB,Broker: ⚠️ РОЗСИНХРОНІЗАЦІЯ: Замовлення в БД є, але подія в RabbitMQ втрачена назавжди!
    API-->>Client: 500 Internal Server Error (Система в неузгодженому стані)
```

---

### 📘 КРОК 2: Кастомний Transactional Outbox + Конвеєр стійкості Polly v8

```mermaid
sequenceDiagram
    autonumber
    actor Client as 👤 Покупець / Swagger
    participant API as Step2_TransactionalOutboxController
    participant DB as 🐘 PostgreSQL (checkoutdb)
    participant Worker as ⚙️ OutboxPublisherWorker
    participant Polly as 🛡️ Polly v8 (Retry + Circuit Breaker)
    participant Broker as 🐇 RabbitMQ Broker

    Client->>API: POST /api/step2-outbox/atomic-checkout { customerId: "USR-1", totalAmount: 299.99 }
    rect rgb(235, 255, 235)
        Note over API,DB: Єдина локальна ACID-транзакція PostgreSQL
        API->>DB: BEGIN TRANSACTION -> INSERT Order + INSERT OutboxMessage (ProcessedAt = NULL) -> COMMIT
    end
    API-->>Client: 202 Accepted (Клієнт не чекає на брокер!)

    loop Кожні 2 секунди у фоні
        Worker->>DB: SELECT * FROM "OutboxMessages" WHERE "ProcessedAt" IS NULL LIMIT 10
        DB-->>Worker: Необроблені повідомлення
        Worker->>Polly: ExecuteAsync(відправка в брокер)
        alt Брокер доступний (Closed State)
            Polly->>Broker: Доставка події OrderCreatedIntegrationEvent
            Polly-->>Worker: Успіх!
            Worker->>DB: UPDATE "OutboxMessages" SET "ProcessedAt" = NOW()
        else Аварія брокера (SimulateBrokerOutage = true)
            Polly--xBroker: Помилка з'єднання!
            Note over Polly: 3 спроби Exponential Backoff з Jitter -> Розмикання Circuit Breaker на 15 с!
            Worker->>DB: UPDATE "OutboxMessages" SET "RetryCount" = RetryCount + 1, "ErrorMessage" = "..."
        end
    end
```

---

### 📘 КРОК 3: Промисловий MassTransit EF Core Outbox (`AddEntityFrameworkOutbox`)

```mermaid
sequenceDiagram
    autonumber
    actor Client as 👤 Покупець / Swagger
    participant API as Step3_MassTransitOutboxController
    participant BusOutbox as MassTransit Bus Outbox Interceptor
    participant DB as 🐘 PostgreSQL (checkoutdb)
    participant DeliverySvc as MassTransit Outbox Delivery Service
    participant Rabbit as 🐇 RabbitMQ Broker

    Client->>API: POST /api/step3-masstransit-outbox/checkout { customerId: "USR-2", totalAmount: 750 }
    API->>DB: _dbContext.Orders.Add(order)
    API->>BusOutbox: _publishEndpoint.Publish(new OrderSubmittedEvent(...))
    Note over BusOutbox,DB: Подія НЕ йде в мережу! Вона додається в ChangeTracker DbContext (таблиця OutboxMessage)
    API->>DB: await _dbContext.SaveChangesAsync() (Атомарний COMMIT Order + OutboxMessage)
    API-->>Client: 200 OK

    DeliverySvc->>DB: Захоплює блокування OutboxState та вичитує нові рядки OutboxMessage
    DeliverySvc->>Rabbit: Публікує конверт у біржу Checkout.Api.Controllers:OrderSubmittedEvent
    DeliverySvc->>DB: Позначає повідомлення доставленим і очищує таблицю OutboxMessage
```

---

### 📘 КРОК 4: Розподілена Сага та Компенсуючі Транзакції (Rollback без блокувань)

```mermaid
stateDiagram-v2
    [*] --> Submitted : 1. Створення замовлення (OrderSubmitted)
    Submitted --> PaymentCaptured : 2. Оплата пройшла успішно (CapturePayment)
    
    PaymentCaptured --> StockReserved : 3а. Товар є в наявності (ReserveStock)
    StockReserved --> Completed : 4а. Замовлення виконано!
    Completed --> [*]

    PaymentCaptured --> StockReservationFailed : 3б. ЗБІЙ СКЛАДУ (Товар закінчився!)
    StockReservationFailed --> Cancelled_PaymentRefunded : 4б. КОМПЕНСАЦІЯ (RefundPayment - повернення грошей клієнту)
    Cancelled_PaymentRefunded --> [*]
```

---

## 💾 Де та що зберігається (Таблиці PostgreSQL `checkoutdb`)

| Таблиця в PostgreSQL | Хто керує | Ключові колонки | Призначення |
| :--- | :--- | :--- | :--- |
| **`"Orders"`** | `AppDbContext` | `Id`, `CustomerId`, `TotalAmount`, `Status`, `CreatedAt` | Основна бізнес-таблиця замовлень клієнтів |
| **`"OutboxMessages"`** | Кастомний Outbox (Крок 2) | `Id`, `EventType`, `Payload`, `CreatedAt`, `ProcessedAt`, `RetryCount`, `ErrorMessage` | Черга вихідних подій для демонстрації роботи `OutboxPublisherWorker` та `Polly v8` |
| **`"OutboxState"` / `"OutboxMessage"`** | MassTransit EF Outbox (Крок 3) | `OutboxId`, `SequenceNumber`, `MessageId`, `ContentType`, `Body`, `Headers` | Системні таблиці MassTransit для безпечної доставки при горизонтальному масштабуванні |
| **`"InboxState"`** | MassTransit EF Outbox (Крок 3) | `Id`, `MessageId`, `ConsumerId`, `Received`, `Consumed` | Дедуплікація вхідних повідомлень (Idempotent Consumer / Inbox Pattern) |
| **`"SagaStates"`** | Saga Controller (Крок 4) | `CorrelationId` (PK), `CustomerId`, `Amount`, `CurrentState`, `IsPaymentCaptured`, `IsStockReserved`, `FailureReason` | Персистентне збереження поточного стану та історії переходів екземпляра Саги |
