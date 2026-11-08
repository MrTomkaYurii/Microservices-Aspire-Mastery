# 04. Архітектура, структура проєктів та рух даних: Event-Driven Architecture з MassTransit

> **Детальна технічна специфікація подієво-орієнтованої взаємодії мікросервісів на базі MassTransit та RabbitMQ: патерни `Publish/Subscribe`, асинхронний діалог `Request/Response`, політики повторів (`Retry Policy`), консьюмери компенсації `Fault<T>` та автоматична топологія бірж і черг.**

---

## 🌲 Дерево файлової структури модуля

```text
04-Event-Driven-MassTransit/
├── 04-Event-Driven-MassTransit.slnx          # Рішення .NET 10 для модуля MassTransit
├── README.md                                 # Посібник користувача та перевірка сценаріїв у Swagger
├── STRUCTURE-AND-DATA-FLOW.md                # ЦЕЙ ДОКУМЕНТ: повна технічна специфікація
│
├── Aspire.AppHost/                           # Оркестратор .NET Aspire
│   ├── AppHost.cs                            # Конфігурація RabbitMQ та реєстрація залежностей мікросервісів
│   ├── Aspire.AppHost.csproj                 # Aspire.Hosting.RabbitMQ
│   └── appsettings.json                      # Конфігурація хоста
│
├── Aspire.ServiceDefaults/                   # Спільні стандарти мікросервісів
│   ├── Extensions.cs                         # Метрики шини, OpenTelemetry трейсинг MassTransit, HealthChecks
│   └── Aspire.ServiceDefaults.csproj         # Пакетні посилання
│
├── Contracts/                                # Спільна збірка типізованих контрактів повідомлень (Pure DTOs)
│   ├── Messages.cs                           # OrderCreatedEvent, CheckStockRequest, StockAvailable/UnavailableResponse
│   └── Contracts.csproj                      # Чиста бібліотека класів без зовнішніх залежностей
│
├── Order.Api/                                # Producer подій та клієнт запитів (Swagger UI у корені /)
│   ├── Program.cs                            # Налаштування DI, Controllers, Swagger UI, конфігурація MassTransit
│   ├── Order.Api.csproj                      # MassTransit.RabbitMQ, Swashbuckle, ServiceDefaults, лінк на Docs
│   ├── Controllers/
│   │   └── OrdersController.cs               # ASP.NET Core контролер (POST /api/orders, GET /api/orders/check-stock/{id})
│   ├── Models/
│   │   └── OrderModels.cs                    # DTO-моделі замовлень (CreateOrderRequest, OrderCreationResult)
│   └── Properties/launchSettings.json        # Профілі запуску
│
└── Warehouse.Worker/                         # Подієвий обробник складу (Warehouse Microservice)
    ├── Program.cs                            # Налаштування консьюмерів, Retry-політики (3 спроби по 2с) та топології
    ├── Warehouse.Worker.csproj               # MassTransit.RabbitMQ, посилання на Contracts
    └── Consumers/
        └── Consumers.cs                      # OrderCreatedConsumer, CheckStockConsumer, OrderCreatedFaultConsumer
```

---

## 🧩 Взаємодія класів та контрактів (Dependency Graph)

```mermaid
classDiagram
    class OrderCreatedEvent {
        +Guid OrderId
        +string CustomerName
        +Guid ProductId
        +string ProductName
        +decimal TotalAmount
        +int Quantity
        +DateTime CreatedAtUtc
    }

    class CheckStockRequest {
        +Guid ProductId
        +int RequestedQuantity
    }

    class StockAvailableResponse {
        +Guid ProductId
        +int AvailableQuantity
        +decimal UnitPrice
        +string ConfirmationCode
    }

    class StockUnavailableResponse {
        +Guid ProductId
        +int RequestedQuantity
        +int CurrentStock
        +string Reason
    }

    class IPublishEndpoint {
        +Publish~T~(message, cancellationToken) Task
    }

    class IRequestClient~TRequest~ {
        +GetResponse~T1, T2~(request, cancellationToken) Task~Response~
    }

    class OrderCreatedConsumer {
        +Consume(ConsumeContext~OrderCreatedEvent~) Task
    }

    class CheckStockConsumer {
        -Dictionary Inventory
        +Consume(ConsumeContext~CheckStockRequest~) Task
    }

    class OrderCreatedFaultConsumer {
        +Consume(ConsumeContext~Fault_OrderCreatedEvent~) Task
    }

    class OrdersController {
        -IPublishEndpoint _publishEndpoint
        -IRequestClient~CheckStockRequest~ _requestClient
        -ILogger _logger
        +CreateOrder(request, cancellationToken) Task~IActionResult~
        +CheckStock(productId, quantity, cancellationToken) Task~IActionResult~
    }

    OrdersController --> IPublishEndpoint : Публікує OrderCreatedEvent
    OrdersController --> IRequestClient~CheckStockRequest~ : Відправляє асинхронний запит до складу
    
    OrderCreatedConsumer ..|> OrderCreatedEvent : Обробляє подію
    CheckStockConsumer ..|> CheckStockRequest : Отримує запит
    CheckStockConsumer ..> StockAvailableResponse : Відповідає при наявності
    CheckStockConsumer ..> StockUnavailableResponse : Відповідає при дефіциті
    OrderCreatedFaultConsumer ..|> OrderCreatedEvent : Ловить остаточний збій Fault
```

---

## 🔄 Покроковий рух даних та патерни повідомлень (Data Flow)

### 1. Патерн Publish / Subscribe (Асинхронне сповіщення про замовлення)

```mermaid
sequenceDiagram
    autonumber
    actor Client as 👤 Покупець / Swagger
    participant API as Order.Api (OrdersController)
    participant Bus as MassTransit Bus (RabbitMQ)
    participant Ex as Contracts:OrderCreatedEvent (Exchange)
    participant Q as order-created (Queue)
    participant Consumer as Warehouse.Worker (OrderCreatedConsumer)

    Client->>API: POST /api/orders { customerName: "Олена", productName: "Pro Pulse", quantity: 1 }
    API->>Bus: publishEndpoint.Publish(OrderCreatedEvent)
    Note over API: HTTP-відповідь 202 Accepted повертається клієнту миттєво!
    API-->>Client: 202 Accepted { OrderId, Status: "AcceptedForProcessing" }

    Bus->>Ex: Повідомлення потрапляє на біржу за іменем контракту
    Ex->>Q: Маршрутизується в чергу консьюмера order-created
    Q->>Consumer: MassTransit десеріалізує конверт у ConsumeContext
    Consumer->>Consumer: Виконує резервування товару на складі
    Consumer-->>Q: Автоматичний BasicAck після успішного завершення методу Consume
```

---

### 2. Патерн Request / Response через шину (Діалог з поліморфною відповіддю)

```mermaid
sequenceDiagram
    autonumber
    actor Client as 👤 Користувач / Swagger
    participant API as Order.Api (OrdersController)
    participant ClientBus as IRequestClient (MassTransit)
    participant Rabbit as RabbitMQ Broker
    participant Q_Req as check-stock (Queue)
    participant Consumer as Warehouse.Worker (CheckStockConsumer)
    participant Q_Temp as Тимчасова ReplyTo черга (Order.Api)

    Client->>API: GET /api/orders/check-stock/11111111-...?quantity=2
    API->>ClientBus: requestClient.GetResponse(CheckStockRequest)
    ClientBus->>Rabbit: Публікація із заголовками CorrelationId та ReplyTo (тимчасова черга)
    Rabbit->>Q_Req: Доставка у чергу складу check-stock
    Q_Req->>Consumer: CheckStockConsumer.Consume()
    
    alt Товар є в наявності (StockAvailableResponse)
        Consumer->>Rabbit: context.RespondAsync(new StockAvailableResponse(...))
        Rabbit->>Q_Temp: Маршрутизація за адресою ReplyTo у тимчасову чергу Order.Api
        Q_Temp-->>ClientBus: Зіставлення за CorrelationId -> StockAvailableResponse
        ClientBus-->>API: Кортеж (available: Completed, unavailable: NotCompleted)
        API-->>Client: 200 OK { AvailableQuantity: 50, ConfirmationCode: "STOCK-OK-..." }
    else Залишок менший за запит (StockUnavailableResponse)
        Consumer->>Rabbit: context.RespondAsync(new StockUnavailableResponse(...))
        Rabbit->>Q_Temp: Маршрутизація у тимчасову чергу Order.Api
        Q_Temp-->>ClientBus: Зіставлення за CorrelationId -> StockUnavailableResponse
        ClientBus-->>API: Кортеж (available: NotCompleted, unavailable: Completed)
        API-->>Client: 400 Bad Request { Reason: "Недостатньо товару на складі" }
    end
```

---

### 3. Механізм повторів (Retry Policy) та обробка збоїв (`_error` + `Fault<T>`)

```mermaid
sequenceDiagram
    autonumber
    actor Client as 👤 Тестувальник / Swagger
    participant API as Order.Api
    participant Bus as RabbitMQ Broker
    participant Consumer as OrderCreatedConsumer
    participant ErrorQ as order-created_error (Error Queue)
    participant FaultConsumer as OrderCreatedFaultConsumer

    Client->>API: POST /api/orders { customerName: "Іван-SimulateError" }
    API->>Bus: Publish(OrderCreatedEvent)
    Bus->>Consumer: Спроба №1 обробки повідомлення
    Consumer--xConsumer: Виняток InvalidOperationException!
    Note over Consumer: MassTransit активує UseMessageRetry (3 спроби з інтервалом 2 с)
    
    Consumer->>Consumer: Пауза 2 с -> Спроба №2 (Виняток!)
    Consumer->>Consumer: Пауза 2 с -> Спроба №3 (Виняток!)
    
    Note over Consumer,ErrorQ: Усі спроби вичерпано! MassTransit виконує аварійний протокол:
    Consumer->>ErrorQ: 1. Переміщує оригінал повідомлення зі StackTrace у чергу 'order-created_error'
    Consumer->>Bus: 2. Публікує системну подію Fault(OrderCreatedEvent)
    Bus->>FaultConsumer: 3. OrderCreatedFaultConsumer.Consume()
    FaultConsumer->>FaultConsumer: Логує критичний збій та ініціює компенсацію (скасування замовлення)
```

---

## 💾 Де та що зберігається (Topology, Envelopes & Exchanges)

| Сутність брокера | Назва в RabbitMQ | Тип | Автоматично створено | Призначення |
| :--- | :--- | :--- | :--- | :--- |
| **Exchange** | `Contracts:OrderCreatedEvent` | Fanout | Так (`MassTransit`) | Топологічна точка публікації для всіх підписників `OrderCreatedEvent` |
| **Exchange** | `Contracts:CheckStockRequest` | Fanout | Так (`MassTransit`) | Маршрутизація запитів перевірки наявності до черги складу |
| **Exchange** | `Contracts:Fault--Contracts:OrderCreatedEvent--` | Fanout | Так (`MassTransit`) | Маршрутизація подій аварії `Fault<OrderCreatedEvent>` |
| **Queue** | `order-created` | FIFO Queue | Так (`KebabCase`) | Робоча черга підписника `OrderCreatedConsumer` |
| **Queue** | `order-created_error` | FIFO Queue | Так (`MassTransit Error`) | Ізолятор збійних повідомлень після вичерпання всіх спроб Retry |
| **Queue** | `order-created_skipped` | FIFO Queue | Так (`MassTransit DeadLetter`) | Черга повідомлень, які надійшли у чергу без активного обробника потрібного типу |
| **Queue** | `check-stock` | FIFO Queue | Так (`KebabCase`) | Черга обробника перевірки залишків `CheckStockConsumer` |
| **Queue** | `order-created-fault` | FIFO Queue | Так (`KebabCase`) | Черга компенсаційного консьюмера `OrderCreatedFaultConsumer` |

---

## ⚙️ Критичні архітектурні переваги MassTransit у Production

1. **MassTransit Message Envelope (Конверт метаданих):**
   - Замість відправки «голого» JSON-об'єкта, MassTransit загортає кожне повідомлення у стандартизований конверт із полями `messageId`, `correlationId`, `conversationId`, `initiatorId`, `sourceAddress`, `destinationAddress`, `messageType`, `sentTime` та `host` (ім'я машини, версія ОС та SDK). Це дає повну картину розслідування інцидентів.
2. **Поліморфний Request / Response через черги:**
   - Метод `_requestClient.GetResponse<StockAvailableResponse, StockUnavailableResponse>()` дозволяє консьюмеру повернути різні типи відповідей залежно від бізнес-результату без викидання винятків і без блокування потоків веб-сервера Kestrel.
3. **Розділення тимчасових і постійних помилок:**
   - Тимчасові мережеві моргання вирішуються в пам'яті через `UseMessageRetry`.
   - Постійні помилки бізнес-логіки не блокують чергу: вони переносяться в `_error` (звідки їх можна перевідправити після виправлення багу) та викликають `Fault<T>` для автоматичної бізнес-компенсації.
