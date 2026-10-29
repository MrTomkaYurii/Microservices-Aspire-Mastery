# 03. RabbitMQ Low-Level Mechanics (.NET 10 & RabbitMQ.Client v7)

> **Глибокий практичний розбір низькорівневих механізмів брокера повідомлень RabbitMQ без додаткових абстракцій:** робота з новим асинхронним API `RabbitMQ.Client v7` (`IChannel`), усі типи обмінників (Direct, Fanout, Topic), керування підтвердженнями (ACK/NACK), механізм Backpressure через QoS (Prefetch) та автоматична маршрутизація відхилених чи протухлих повідомлень у **Dead Letter Exchange (DLX)**.

---

## 🏛️ Архітектурна карта топології брокера

```mermaid
flowchart TD
    Sender["📤 Sender.Api (Publisher)
HTTP POST /api/messages/..."]

    subgraph Exchanges ["Типи Exchanges у RabbitMQ"]
        DirectEx["🎯 Direct Exchange
('direct-exchange')"]
        FanoutEx["📢 Fanout Exchange
('fanout-exchange')"]
        TopicEx["🏷️ Topic Exchange
('topic-exchange')"]
        DlxEx["💀 Dead Letter Exchange
('dlx-exchange')"]
    end

    subgraph Queues ["Черги (Queues)"]
        CritQ["orders-critical-queue
(RoutingKey: 'orders.critical')"]
        RegQ["orders-regular-queue
(RoutingKey: 'orders.regular')"]
        
        NotifA["notifications-service-a-queue
(Отримує копію)"]
        NotifB["notifications-service-b-queue
(Отримує копію)"]

        EuQ["europe-all-queue
(Патерн: 'eu.#')"]
        UrgentQ["urgent-all-queue
(Патерн: '*.orders.urgent')"]

        WorkDlxQ["work-queue-with-dlx
(Повідомлення з TTL)"]
        DeadQ["dead-letter-queue
(Кладовище збійних повідомлень)"]
    end

    subgraph Consumers ["Обробники (Receiver.Worker)"]
        Worker["🎧 Receiver.Worker
(AsyncEventingBasicConsumer,
PrefetchCount = 5, Manual Ack/Nack)"]
    end

    Sender -->|Routing: 'orders.critical'| DirectEx
    DirectEx -->|Точний збіг| CritQ
    DirectEx -->|Точний збіг| RegQ

    Sender -->|Без ключа| FanoutEx
    FanoutEx -->|Широкомовна розсилка| NotifA
    FanoutEx -->|Широкомовна розсилка| NotifB

    Sender -->|'eu.orders.urgent'| TopicEx
    TopicEx -->|Збіг маски 'eu.#'| EuQ
    TopicEx -->|Збіг маски '*.orders.urgent'| UrgentQ

    Sender -->|TTL = 5000ms| WorkDlxQ
    WorkDlxQ -.->|Таймаут TTL або BasicNack(requeue=false)| DlxEx
    DlxEx --> DeadQ

    CritQ --> Worker
    RegQ --> Worker
    NotifA --> Worker
    NotifB --> Worker
    EuQ --> Worker
    UrgentQ --> Worker
    DeadQ --> Worker
```

---

## ⚡ Критичні точки та концепції RabbitMQ v7

### 1. `IConnection` проти `IChannel` (Ключове правило)
* **`IConnection`** представляє реальне TCP-з'єднання з брокером. Його створення — «дорога» операція. У мікросервісі має бути **лише один `IConnection` (Singleton)**.
* **`IChannel`** — це легковажний віртуальний канал всередині одного TCP-з'єднання. У `RabbitMQ.Client v7` старий інтерфейс `IModel` видалено, і тепер усі канали створюються асинхронно через `connection.CreateChannelAsync()`. Канали **не є потокобезпечними**: один канал має використовуватись одним потоком/воркером!

### 2. Manual ACK/NACK проти AutoAck
* **`autoAck: true`** — брокер видаляє повідомлення з пам'яті в ту ж секунду, як відправив його по мережі. Якщо сервіс впаде під час обробки — повідомлення буде **втрачено назавжди**.
* **`autoAck: false` (Рекомендовано для Production):**
  * `BasicAckAsync(deliveryTag, multiple: false)` — повідомлення успішно збережено в БД чи оброблено, брокер може його видалити.
  * `BasicNackAsync(deliveryTag, multiple: false, requeue: true)` — тимчасова помилка (наприклад, БД недоступна), повернути повідомлення на початок черги.
  * `BasicNackAsync(deliveryTag, multiple: false, requeue: false)` — критична помилка бізнес-валідації (Poison message). Повідомлення відкидається і, якщо налаштовано, летить у **Dead Letter Exchange (DLX)**!

### 3. Backpressure та QoS (`BasicQosAsync`)
Якщо в черзі накопичилося 100 000 замовлень, і запускається воркер, без налаштування QoS брокер спробує вивантажити йому в пам'ять усі 100 000 повідомлень одночасно, що гарантовано спричинить `OutOfMemoryException`.
* Виклик `await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 5, global: false)` встановлює ліміт: воркер бере **не більше 5 непідтверджених повідомлень**. Нове надійде лише тоді, коли хоча б одне буде підтверджено через `BasicAck`!

---

## 🧪 Як протестувати всі сценарії через Swagger

1. **Запустіть AppHost:**
   ```bash
   cd 03-RabbitMQ-LowLevel/Aspire.AppHost
   dotnet run
   ```
2. Відкрийте **Aspire Dashboard** та перейдіть за посиланням на **RabbitMQ Management UI** (`http://localhost:15672`, логін: `guest`, пароль: `guest`).
3. Відкрийте Swagger `sender-api`:
   * **Тест Direct:** `POST /api/messages/direct` з `routingKey: "orders.critical"`. Подивіться в логах `Receiver.Worker`, як спрацював `[Critical Consumer]`.
   * **Тест Fanout:** `POST /api/messages/fanout`. Повідомлення миттєво з'явиться одночасно в обох воркерах `Service A` та `Service B`.
   * **Тест Topic:** `POST /api/messages/topic` з `topicRoutingKey: "eu.orders.urgent"`. Повідомлення потрапить одразу в дві різні черги: `europe-all-queue` (бо починається на `eu.`) і в `urgent-all-queue` (бо закінчується на `.orders.urgent`)!
   * **Тест Dead Letter Exchange (DLX):**
     * `POST /api/messages/dead-letter-test` з `ttlMilliseconds: 3000`. Не чіпайте повідомлення 3 секунди.
     * Зайдіть у RabbitMQ Management UI: ви побачите, як через 3 секунди таймер TTL протухне, брокер сам викине повідомлення з черги та перенаправить його в `dead-letter-queue`, де його зловить `[DLX DEAD LETTER CONSUMER]`!
