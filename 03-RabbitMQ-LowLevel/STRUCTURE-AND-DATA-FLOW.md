# 03. Архітектура, структура проєктів та рух даних: RabbitMQ Low-Level Mechanics

> **Детальний технічний опис низькорівневої AMQP-топології на базі RabbitMQ.Client v7: пряма, широкомовна та шаблонна маршрутизація (Direct, Fanout, Topic), керування навантаженням (QoS Prefetch), черги збоїв (Dead Letter Exchange) та захист від "отруйних" повідомлень.**

---

## 🌲 Дерево файлової структури модуля

```text
03-RabbitMQ-LowLevel/
├── 03-RabbitMQ-LowLevel.slnx                # Рішення .NET 10 для модуля низькорівневого RabbitMQ
├── README.md                                 # Посібник з тестування ендпоінтів та огляд сценаріїв
├── STRUCTURE-AND-DATA-FLOW.md                # ЦЕЙ ДОКУМЕНТ: повна технічна специфікація
│
├── Aspire.AppHost/                           # Оркестратор .NET Aspire
│   ├── AppHost.cs                            # Запуск RabbitMQ з менеджмент-плагіном (.WithManagementPlugin())
│   ├── Aspire.AppHost.csproj                 # Aspire.Hosting.RabbitMQ
│   └── appsettings.json                      # Конфігурація
│
├── Aspire.ServiceDefaults/                   # Спільні стандарти мікросервісів
│   ├── Extensions.cs                         # Метрики брокера, HealthChecks, Service Discovery
│   └── Aspire.ServiceDefaults.csproj         # Пакетні посилання
│
├── Sender.Api/                               # Producer (Відправник повідомлень)
│   ├── Program.cs                            # Налаштування DI, Controllers, Swagger, підключення RabbitMQ v7
│   ├── Sender.Api.csproj                     # RabbitMQ.Client v7.x, Swagger, ServiceDefaults, лінк на Docs
│   ├── Controllers/
│   │   ├── Step1_DirectExchangeController.cs # КРОК 1: Direct Exchange (точний збіг ключа)
│   │   ├── Step2_FanoutExchangeController.cs # КРОК 2: Fanout Exchange (широкомовна розсилка)
│   │   ├── Step3_TopicExchangeController.cs  # КРОК 3: Topic Exchange (маски та шаблони * та #)
│   │   └── Step4_DeadLetterAndResilienceController.cs # КРОК 4: Dead Letter Exchange, TTL та Poison Messages
│   ├── Models/
│   │   └── RabbitModels.cs                   # DTO-моделі для різних бірж
│   └── Properties/launchSettings.json        # Профілі запуску
│
└── Receiver.Worker/                          # Consumer (Фоновий обробник)
    ├── Program.cs                            # Оголошення топології (Exchanges, Queues, Bindings), QoS та обробники
    └── Receiver.Worker.csproj                # Background Worker, RabbitMQ.Client v7.x
```

---

## 🧩 Взаємодія класів та компонентів (Dependency Graph)

```mermaid
classDiagram
    class AppHost {
        +AddRabbitMQ("messaging")
        +WithManagementPlugin()
        +AddProject<Sender_Api>()
        +AddProject<Receiver_Worker>()
    }

    class IConnection {
        +CreateChannelAsync() Task~IChannel~
        +IsOpen bool
        +CloseAsync() Task
    }

    class IChannel {
        +ExchangeDeclareAsync(exchange, type, durable)
        +QueueDeclareAsync(queue, durable, exclusive, autoDelete, arguments)
        +QueueBindAsync(queue, exchange, routingKey)
        +BasicQosAsync(prefetchSize, prefetchCount, global)
        +BasicPublishAsync(exchange, routingKey, mandatory, basicProperties, body)
        +BasicAckAsync(deliveryTag, multiple)
        +BasicNackAsync(deliveryTag, multiple, requeue)
    }

    class Step1_DirectExchangeController {
        -IConnection _connection
        +SendDirect(request) Task~IActionResult~
    }

    class Step2_FanoutExchangeController {
        -IConnection _connection
        +Broadcast(request) Task~IActionResult~
    }

    class Step3_TopicExchangeController {
        -IConnection _connection
        +SendTopic(request) Task~IActionResult~
    }

    class Step4_DeadLetterAndResilienceController {
        -IConnection _connection
        +SendWithTtl(request) Task~IActionResult~
        +SendPoisonMessage() Task~IActionResult~
    }

    class RabbitMqConsumerWorker {
        -IConnection _connection
        -IChannel _channel
        -ILogger _logger
        #ExecuteAsync(cancellationToken) Task
        -RegisterConsumer(queueName, handler, cancellationToken) Task
    }

    class AsyncEventingBasicConsumer {
        +ReceivedAsync Event
    }

    AppHost ..> IConnection : Надає рядок підключення "messaging"
    Step1_DirectExchangeController --> IConnection : Singleton підключення
    Step2_FanoutExchangeController --> IConnection : Singleton підключення
    Step3_TopicExchangeController --> IConnection : Singleton підключення
    Step4_DeadLetterAndResilienceController --> IConnection : Singleton підключення
    RabbitMqConsumerWorker --> IConnection : Singleton підключення
    RabbitMqConsumerWorker ..> IChannel : Створює довгоживучий IChannel для прослуховування черг
    RabbitMqConsumerWorker --> AsyncEventingBasicConsumer : Реєструє асинхронні колбеки обробки
    AsyncEventingBasicConsumer --> IChannel : Викликає BasicAckAsync або BasicNackAsync
```

---

## 🔄 Покроковий рух даних за типами бірж (Data Flow)

### 1. Direct Exchange (Точна маршрутизація за ключем)

```mermaid
sequenceDiagram
    autonumber
    actor Client as 👤 Клієнт
    participant Sender as Sender.Api
    participant DirectEx as direct-exchange (Direct)
    participant Q_Crit as orders-critical-queue
    participant Q_Reg as orders-regular-queue
    participant Worker as Receiver.Worker

    Client->>Sender: POST /api/messages/direct { routingKey: "orders.critical", payload: "..." }
    Sender->>DirectEx: BasicPublishAsync(exchange: "direct-exchange", routingKey: "orders.critical")
    Note over DirectEx: Брокер порівнює точний збіг RoutingKey!
    DirectEx->>Q_Crit: Доставляє повідомлення (Binding = "orders.critical")
    DirectEx--xQ_Reg: Ігнорує (Binding = "orders.regular" не співпадає)
    Q_Crit->>Worker: Доставляє пачку (враховуючи prefetchCount: 5)
    Worker->>Worker: Обробляє критичне замовлення
    Worker->>Q_Crit: BasicAckAsync(deliveryTag) -> Повідомлення видалено з черги
```

---

### 2. Fanout Exchange (Широкомовна розсилка / Broadcast)

```mermaid
sequenceDiagram
    autonumber
    actor Client as 👤 Клієнт
    participant Sender as Sender.Api
    participant FanoutEx as fanout-exchange (Fanout)
    participant QA as notifications-service-a-queue
    participant QB as notifications-service-b-queue
    participant Worker as Receiver.Worker

    Client->>Sender: POST /api/messages/fanout { broadcastText: "Оновлення платформи 2.0" }
    Sender->>FanoutEx: BasicPublishAsync(exchange: "fanout-exchange", routingKey: "")
    Note over FanoutEx: RoutingKey ігнорується! Копія відправляється в усі прив'язані черги.
    FanoutEx->>QA: Клон 1 -> notifications-service-a-queue
    FanoutEx->>QB: Клон 2 -> notifications-service-b-queue
    QA->>Worker: Consumer A обробляє та викликає BasicAckAsync()
    QB->>Worker: Consumer B обробляє та викликає BasicAckAsync()
```

---

### 3. Topic Exchange (Маршрутизація за масками `*` та `#`)

```mermaid
sequenceDiagram
    autonumber
    actor Client as 👤 Клієнт
    participant Sender as Sender.Api
    participant TopicEx as topic-exchange (Topic)
    participant Q_EU as europe-all-queue (eu.#)
    participant Q_Urg as urgent-all-queue (*.orders.urgent)
    participant Worker as Receiver.Worker

    Note over Client,Worker: Тест 1: Відправка з ключем "eu.orders.urgent"
    Client->>Sender: POST /api/messages/topic { topicRoutingKey: "eu.orders.urgent" }
    Sender->>TopicEx: BasicPublishAsync("topic-exchange", "eu.orders.urgent")
    TopicEx->>Q_EU: Збіг з маскою "eu.#" (# = будь-яка к-сть слів)
    TopicEx->>Q_Urg: Збіг з маскою "*.orders.urgent" (* = рівно одне слово "eu")
    Note over Q_EU,Q_Urg: Обидві черги отримали повідомлення одночасно!

    Note over Client,Worker: Тест 2: Відправка з ключем "us.orders.urgent"
    Client->>Sender: POST /api/messages/topic { topicRoutingKey: "us.orders.urgent" }
    Sender->>TopicEx: BasicPublishAsync("topic-exchange", "us.orders.urgent")
    TopicEx--xQ_EU: Не співпадає з "eu.#"
    TopicEx->>Q_Urg: Співпадає з "*.orders.urgent" (* замінює "us")
```

---

### 4. Dead Letter Exchange (DLX), TTL та Poison Message Handling

```mermaid
sequenceDiagram
    autonumber
    actor Client as 👤 Клієнт
    participant Sender as Sender.Api
    participant WorkQ as work-queue-with-dlx (x-dead-letter-exchange: dlx-exchange)
    participant DLX as dlx-exchange (Direct)
    participant DeadQ as dead-letter-queue
    participant Worker as Receiver.Worker

    rect rgb(255, 240, 240)
        Note over Client,DeadQ: Сценарій А: Вичерпання TTL (Часу життя повідомлення)
        Client->>Sender: POST /dead-letter-test { ttlMilliseconds: 5000 }
        Sender->>WorkQ: Публікація з заголовком Expiration: "5000"
        Note over WorkQ: Минає 5 секунд без консьюмера...
        WorkQ->>DLX: Брокер автоматично перекидає прострочене повідомлення!
        DLX->>DeadQ: Маршрутизація з dead-letter-routing-key у dead-letter-queue
    end

    rect rgb(240, 255, 240)
        Note over Client,DeadQ: Сценарій Б: Отруйне повідомлення (Poison Message)
        Client->>Sender: POST /direct { routingKey: "orders.critical", payload: "simulate-poison" }
        Sender->>WorkQ: Доставка у orders-critical-queue
        WorkQ->>Worker: Воркер зчитує повідомлення
        Worker->>Worker: Виявлено маркер "simulate-poison"!
        Worker->>WorkQ: BasicNackAsync(requeue: false)
        WorkQ->>DLX: Відхилене повідомлення негайно перенаправляється в DLX
        DLX->>DeadQ: Збереження для ручного розбору та аудиту інженерами
    end
```

---

## 💾 Де та що зберігається (Storage, Topology & Durability)

| Сутність RabbitMQ | Ім'я в системі | Тип / Специфікація | Прапорець Durability | Аргументи (x-arguments) | Призначення |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Exchange** | `direct-exchange` | Direct | `durable: true` | — | Маршрутизація за точним співпадінням routingKey |
| **Exchange** | `fanout-exchange` | Fanout | `durable: true` | — | Широкомовна розсилка повідомлень без аналізу ключів |
| **Exchange** | `topic-exchange` | Topic | `durable: true` | — | Маршрутизація за масками слів через крапку (`*`, `#`) |
| **Exchange** | `dlx-exchange` | Direct | `durable: true` | — | Dead Letter біржа для ізоляції помилкових та прострочених даних |
| **Queue** | `orders-critical-queue` | FIFO Queue | `durable: true` | — | Черга критичних замовлень (`orders.critical`) |
| **Queue** | `orders-regular-queue` | FIFO Queue | `durable: true` | — | Черга стандартних замовлень (`orders.regular`) |
| **Queue** | `notifications-service-a-queue` | FIFO Queue | `durable: true` | — | Сервіс сповіщень А (підписник Fanout) |
| **Queue** | `notifications-service-b-queue` | FIFO Queue | `durable: true` | — | Сервіс сповіщень Б (підписник Fanout) |
| **Queue** | `europe-all-queue` | FIFO Queue | `durable: true` | — | Підписник Topic за маскою `eu.#` |
| **Queue** | `urgent-all-queue` | FIFO Queue | `durable: true` | — | Підписник Topic за маскою `*.orders.urgent` |
| **Queue** | `work-queue-with-dlx` | FIFO Queue | `durable: true` | `x-dead-letter-exchange: dlx-exchange`, `x-dead-letter-routing-key: dead-letter-routing-key` | Демонстрація TTL тайм-аутів та перекидання в DLX |
| **Queue** | `dead-letter-queue` | FIFO Queue | `durable: true` | — | Кінцеве сховище недоставлених та отруйних повідомлень |

---

## ⚙️ Критичні низькорівневі механіки та рекомендації

1. **Революція RabbitMQ.Client v7 (Повна асинхронність):**
   - У новітньому SDK v7 старий синхронний інтерфейс `IModel` видалено.
   - Усі виклики топології та публікації здійснюються через `IChannel` та методи `CreateChannelAsync()`, `ExchangeDeclareAsync()`, `BasicPublishAsync()`, `BasicAckAsync()`.
2. **Управління з'єднаннями (Connection vs Channel):**
   - `IConnection` — це важке довгоживуче TCP-з'єднання. Воно реєструється як **Singleton** на весь застосунок.
   - `IChannel` — це легковаговий віртуальний потік (мультиплексування всередині TCP). Для публікації використовується патерн `await using var channel = await connection.CreateChannelAsync()`. Канали **не можна** ділити між паралельними потоками!
3. **QoS (Quality of Service) та Backpressure:**
   - Якщо консьюмер не обмежує потік, брокер кине в нього всю чергу (наприклад, 100 000 повідомлень), що викличе аварійне завершення `OutOfMemoryException`.
   - Виклик `await channel.BasicQosAsync(0, prefetchCount: 5, false)` змушує RabbitMQ надсилати наступні повідомлення **тільки після того**, як воркер надішле `BasicAckAsync` за попередні.
4. **Гарантія збереження даних (Message Durability):**
   - Щоб повідомлення не зникло під час рестарту брокера або Docker-контейнера, одночасно мають виконуватися 2 умови:
     1. Черга та біржа створені як `durable: true`.
     2. Повідомлення опубліковано з атрибутом `DeliveryMode = DeliveryModes.Persistent` (записується на диск).
5. **Обробка Poison Messages:**
   - При виникненні непереборної помилки бізнес-логіки не можна викликати `BasicNack(requeue: true)`, оскільки це призведе до нескінченного циклу зависання процесора (Retry Storm).
   - Завжди використовуйте `BasicNackAsync(deliveryTag, multiple: false, requeue: false)` у поєднанні з DLX.
