# 03. Архітектура, структура проєктів та рух даних: RabbitMQ Low-Level Mechanics

> **Детальна технічна специфікація низькорівневої AMQP-топології на базі `RabbitMQ.Client v7`: пряма, широкомовна та шаблонна маршрутизація (`Direct`, `Fanout`, `Topic`), керування навантаженням (`QoS Prefetch`), черги збоїв (`Dead Letter Exchange`) та захист від «отруйних» повідомлень.**

---

## 🎯 Преамбула: Еволюція маршрутизації в брокері повідомлень

У класичній монолітній системі компоненти викликають методи один одного напряму в пам'яті. У розподіленій системі прямі синхронні виклики створюють жорстку часову зв'язаність (Temporal Coupling): якщо сервіс-одержувач перезавантажується, відправник падає з помилкою.

Брокер **RabbitMQ** виступає буфером і інтелектуальним маршрутизатором між сервісами. У цьому модулі ми досліджуємо 4 фундаментальні патерни маршрутизації та надійності:

```mermaid
flowchart LR
    Step1["Крок 1: Direct Exchange<br/>(Точний адресний ключ)"] --> Step2["Крок 2: Fanout Exchange<br/>(Широкомовне розмноження)"]
    Step2 --> Step3["Крок 3: Topic Exchange<br/>(Гнучкі маски * та #)"]
    Step3 --> Step4["Крок 4: Dead Letter (DLX)<br/>(TTL, NACK та ізоляція збоїв)"]
```

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
│   └── appsettings.json                      # Конфігурація оркестратора
│
├── Aspire.ServiceDefaults/                   # Спільні стандарти мікросервісів
│   ├── Extensions.cs                         # Метрики брокера, HealthChecks, Service Discovery
│   └── Aspire.ServiceDefaults.csproj         # Пакетні посилання
│
├── Sender.Api/                               # Producer (Відправник повідомлень + Swagger UI у /)
│   ├── Program.cs                            # Налаштування DI, Controllers, Swagger UI, підключення RabbitMQ v7
│   ├── Sender.Api.csproj                     # RabbitMQ.Client v7.x, Swashbuckle, ServiceDefaults, лінк на Docs
│   ├── Controllers/
│   │   ├── Step1_DirectExchangeController.cs # КРОК 1: Direct Exchange (точний збіг ключа)
│   │   ├── Step2_FanoutExchangeController.cs # КРОК 2: Fanout Exchange (широкомовна розсилка)
│   │   ├── Step3_TopicExchangeController.cs  # КРОК 3: Topic Exchange (маски та шаблони * та #)
│   │   └── Step4_DeadLetterAndResilienceController.cs # КРОК 4: Dead Letter Exchange, TTL та Poison Messages
│   ├── Models/
│   │   └── RabbitModels.cs                   # DTO-моделі запитів для різних бірж
│   └── Properties/launchSettings.json        # Профілі запуску (відкривають корінь зі Swagger)
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
        +AddProject~Sender_Api~()
        +AddProject~Receiver_Worker~()
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
        +SendCritical(request) Task~IActionResult~
        +SendRegular(request) Task~IActionResult~
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

### 📘 КРОК 1: Direct Exchange (Точна маршрутизація за ключем)

> **Життєва аналогія:** Кур'єр доставляє лист у конкретну абонентську скриньку за точним номером квартири. Якщо на конверті написано `orders.critical`, лист потрапить виключно у скриньку критичних замовлень.

```mermaid
sequenceDiagram
    autonumber
    actor Client as 👤 Клієнт / Swagger
    participant Sender as Sender.Api (Step1_DirectExchangeController)
    participant DirectEx as direct-exchange (Direct)
    participant Q_Crit as orders-critical-queue
    participant Q_Reg as orders-regular-queue
    participant Worker as Receiver.Worker

    Client->>Sender: POST /api/step1-direct/send { routingKey: "orders.critical", payload: "..." }
    Sender->>DirectEx: BasicPublishAsync(exchange: "direct-exchange", routingKey: "orders.critical")
    Note over DirectEx: Брокер перевіряє точний збіг RoutingKey == BindingKey
    DirectEx->>Q_Crit: Доставляє повідомлення (Binding = "orders.critical")
    DirectEx--xQ_Reg: Ігнорує (Binding = "orders.regular" не збігається)
    Q_Crit->>Worker: Доставляє пачку (враховуючи prefetchCount: 5)
    Worker->>Worker: Обробляє критичне замовлення
    Worker->>Q_Crit: BasicAckAsync(deliveryTag, multiple: false)
```

---

### 📘 КРОК 2: Fanout Exchange (Широкомовна розсилка / Broadcast)

> **Життєва аналогія:** Оголошення через гучномовець на вокзалі. Диктор говорить один раз, а повідомлення одночасно чують усі пасажири в усіх залах очікування.

```mermaid
sequenceDiagram
    autonumber
    actor Client as 👤 Клієнт / Swagger
    participant Sender as Sender.Api (Step2_FanoutExchangeController)
    participant FanoutEx as fanout-exchange (Fanout)
    participant QA as notifications-service-a-queue
    participant QB as notifications-service-b-queue
    participant Worker as Receiver.Worker

    Client->>Sender: POST /api/step2-fanout/broadcast { broadcastText: "Оновлення платформи 2.0" }
    Sender->>FanoutEx: BasicPublishAsync(exchange: "fanout-exchange", routingKey: "")
    Note over FanoutEx: RoutingKey повністю ігнорується! Копія летить у всі прив'язані черги
    FanoutEx->>QA: Клон 1 -> notifications-service-a-queue
    FanoutEx->>QB: Клон 2 -> notifications-service-b-queue
    QA->>Worker: Consumer A обробляє та викликає BasicAckAsync()
    QB->>Worker: Consumer B обробляє та викликає BasicAckAsync()
```

---

### 📘 КРОК 3: Topic Exchange (Маршрутизація за масками `*` та `#`)

> **Життєва аналогія:** Міжнародний сортувальний термінал, де посилки маркуються словами через крапку: `регіон.тип.пріоритет`. Сортувальник з правилом `eu.#` забирає всі посилки з Європи, а сортувальник з правилом `*.orders.urgent` забирає термінові замовлення з будь-якої точки світу.

```mermaid
sequenceDiagram
    autonumber
    actor Client as 👤 Клієнт / Swagger
    participant Sender as Sender.Api (Step3_TopicExchangeController)
    participant TopicEx as topic-exchange (Topic)
    participant Q_EU as europe-all-queue
    participant Q_Urg as urgent-all-queue
    participant Worker as Receiver.Worker

    Note over Client,Worker: Тест 1: Відправка з ключем "eu.orders.urgent"
    Client->>Sender: POST /api/step3-topic/send { topicRoutingKey: "eu.orders.urgent" }
    Sender->>TopicEx: BasicPublishAsync("topic-exchange", "eu.orders.urgent")
    TopicEx->>Q_EU: Збіг із маскою eu-hash (будь-яка кількість слів після eu)
    TopicEx->>Q_Urg: Збіг із маскою star.orders.urgent (рівно одне слово перед orders)
    Note over Q_EU,Q_Urg: Обидві черги отримали повідомлення одночасно!

    Note over Client,Worker: Тест 2: Відправка з ключем "us.orders.urgent"
    Client->>Sender: POST /api/step3-topic/send { topicRoutingKey: "us.orders.urgent" }
    Sender->>TopicEx: BasicPublishAsync("topic-exchange", "us.orders.urgent")
    TopicEx--xQ_EU: Не починається з префікса eu
    TopicEx->>Q_Urg: Збігається з маскою star.orders.urgent
```

---

### 📘 КРОК 4: Dead Letter Exchange (DLX), TTL та Poison Message Handling

> **Життєва аналогія:** Якщо посилку ніхто не забрав протягом терміну зберігання (TTL) або адреса виявилася недійсною (Poison Message), пошта не возить її по колу вічно, а передає в архів нерозібраних відправлень (Dead Letter Queue).

```mermaid
sequenceDiagram
    autonumber
    actor Client as 👤 Клієнт / Swagger
    participant Sender as Sender.Api (Step4_DeadLetterAndResilienceController)
    participant WorkQ as work-queue-with-dlx
    participant CritQ as orders-critical-queue
    participant DLX as dlx-exchange (Direct)
    participant DeadQ as dead-letter-queue
    participant Worker as Receiver.Worker

    rect rgb(255, 240, 240)
        Note over Client,DeadQ: Сценарій А: Вичерпання часу життя повідомлення (TTL Expiration)
        Client->>Sender: POST /api/step4-dead-letter/send-with-ttl { ttlMilliseconds: 5000 }
        Sender->>WorkQ: Публікація із заголовком Expiration = "5000"
        Note over WorkQ: Минає 5 секунд без вичитки консьюмером...
        WorkQ->>DLX: Брокер автоматично вилучає прострочене повідомлення
        DLX->>DeadQ: Маршрутизація за dead-letter-routing-key у dead-letter-queue
        DeadQ->>Worker: DLX Consumer фіксує прострочений пакет у логах
    end

    rect rgb(240, 255, 240)
        Note over Client,DeadQ: Сценарій Б: Відхилення отруйного повідомлення (Poison Message)
        Client->>Sender: POST /api/step4-dead-letter/send-poison-message
        Sender->>CritQ: Публікація маркера "simulate-poison"
        CritQ->>Worker: Воркер зчитує повідомлення
        Worker->>Worker: Виявлено маркер помилки "simulate-poison"!
        Worker->>CritQ: BasicNackAsync(deliveryTag, multiple: false, requeue: false)
        Note over CritQ,DeadQ: Повідомлення видаляється з робочої черги без зависання процесора
    end
```

---

## 💾 Де та що зберігається (Storage, Topology & Durability)

| Сутність RabbitMQ | Ім'я в системі | Тип / Специфікація | Прапорець Durability | Аргументи (`x-arguments`) | Призначення |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Exchange** | `direct-exchange` | Direct | `durable: true` | — | Маршрутизація за точним співпадінням `routingKey` |
| **Exchange** | `fanout-exchange` | Fanout | `durable: true` | — | Широкомовна розсилка повідомлень без аналізу ключів |
| **Exchange** | `topic-exchange` | Topic | `durable: true` | — | Маршрутизація за масками слів через крапку (`*`, `#`) |
| **Exchange** | `dlx-exchange` | Direct | `durable: true` | — | Dead Letter біржа для ізоляції помилкових та прострочених даних |
| **Queue** | `orders-critical-queue` | FIFO Queue | `durable: true` | — | Черга критичних замовлень (`orders.critical`) |
| **Queue** | `orders-regular-queue` | FIFO Queue | `durable: true` | — | Черга стандартних замовлень (`orders.regular`) |
| **Queue** | `notifications-service-a-queue` | FIFO Queue | `durable: true` | — | Сервіс сповіщень А (підписник Fanout) |
| **Queue** | `notifications-service-b-queue` | FIFO Queue | `durable: true` | — | Сервіс сповіщень Б (підписник Fanout) |
| **Queue** | `europe-all-queue` | FIFO Queue | `durable: true` | — | Підписник Topic за маскою `eu.#` |
| **Queue** | `urgent-all-queue` | FIFO Queue | `durable: true` | — | Підписник Topic за маскою `*.orders.urgent` |
| **Queue** | `work-queue-with-dlx` | FIFO Queue | `durable: true` | `x-dead-letter-exchange: dlx-exchange`<br>`x-dead-letter-routing-key: dead-letter-routing-key` | Демонстрація TTL тайм-аутів та перекидання в DLX |
| **Queue** | `dead-letter-queue` | FIFO Queue | `durable: true` | — | Кінцеве сховище недоставлених та отруйних повідомлень |

---

## ⚙️ Критичні низькорівневі механіки та рекомендації для Production

1. **Революція `RabbitMQ.Client v7` (Повна асинхронність):**
   - У SDK v7 старий синхронний інтерфейс `IModel` видалено.
   - Усі виклики топології та публікації здійснюються виключно через `IChannel` та методи `CreateChannelAsync()`, `ExchangeDeclareAsync()`, `BasicPublishAsync()`, `BasicAckAsync()`.
2. **Управління з'єднаннями (`IConnection` vs `IChannel`):**
   - `IConnection` — це важке довгоживуче TCP-з'єднання. Воно реєструється як **Singleton** на весь застосунок.
   - `IChannel` — це легковаговий віртуальний потік (мультиплексування всередині TCP). Для публікації використовується патерн `await using var channel = await connection.CreateChannelAsync()`. Канали **не можна** ділити між паралельними потоками!
3. **QoS (Quality of Service) та Backpressure:**
   - Якщо консьюмер не обмежує потік, брокер кине в нього всю чергу (наприклад, 100 000 повідомлень), що викличе аварійне завершення `OutOfMemoryException`.
   - Виклик `await channel.BasicQosAsync(0, prefetchCount: 5, false)` змушує RabbitMQ надсилати наступні повідомлення **тільки після того**, як воркер надішле `BasicAckAsync` за попередні.
4. **Гарантія збереження даних на диску (Message Durability):**
   - Щоб повідомлення не зникло під час рестарту брокера або Docker-контейнера, одночасно мають виконуватися **дві** умови:
     1. Черга та біржа створені як `durable: true`.
     2. Повідомлення опубліковано з атрибутом `DeliveryMode = DeliveryModes.Persistent` (записується на диск).
5. **Захист від Retry Storm при обробці Poison Messages:**
   - При виникненні непереборної помилки бізнес-логіки або десеріалізації не можна викликати `BasicNack(requeue: true)`, оскільки це призведе до нескінченного циклу зависання процесора.
   - Завжди використовуйте `BasicNackAsync(deliveryTag, multiple: false, requeue: false)` у поєднанні з налаштованим DLX.
