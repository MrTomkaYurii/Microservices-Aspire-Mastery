# 04. Event-Driven Architecture with MassTransit & RabbitMQ

> **Побудова Event-Driven мікросервісної архітектури на .NET 10 за допомогою MassTransit поверх RabbitMQ:** патерни Publish/Subscribe, синхронний Request/Response через шину, автоматичне керування топологією брокера, політики повторних спроб (Retry Policies) та автоматичні черги збоїв `_error`.

---

## 🏛️ Архітектурна схема модуля

```mermaid
flowchart TD
    Client["👤 Клієнт / Swagger"]

    subgraph AspireOrchestration [".NET Aspire AppHost & Dashboard"]
        AppHost["Aspire.AppHost"]
        Dashboard["📊 Aspire Dashboard & RabbitMQ Management"]
    end

    subgraph Microservices ["Мікросервіси"]
        OrderApi["🛒 Order.Api
(IPublishEndpoint + IRequestClient)"]
        Warehouse["🏭 Warehouse.Worker
(IConsumer&lt;OrderCreatedEvent&gt;,
IConsumer&lt;CheckStockRequest&gt;,
IConsumer&lt;Fault&gt;)"]
    end

    subgraph MassTransitBus ["Шина повідомлень MassTransit (RabbitMQ)"]
        EventEx{{"📢 Contracts:OrderCreatedEvent
(Fanout Exchange)"}}
        RequestQ[("📥 check-stock-request
(Очередь запитів)")]
        ReplyQ[("📤 response-queue
(Тимчасова черга відповідей)")]
        ErrorQ[("💀 order-created_error
(Черга збоїв після вичерпання Retry)")]
    end

    AppHost -.-> OrderApi
    AppHost -.-> Warehouse

    Client -->|1. POST /api/orders (Pub/Sub)| OrderApi
    OrderApi -->|2. publishEndpoint.Publish| EventEx
    EventEx --> Warehouse
    Warehouse -.->|3. Якщо помилка -> 3 спроби Retry -> якщо FAIL| ErrorQ

    Client -->|4. POST /api/orders/check-stock| OrderApi
    OrderApi -->|5. requestClient.GetResponse| RequestQ
    RequestQ --> Warehouse
    Warehouse -->|6. context.RespondAsync| ReplyQ
    ReplyQ -->|7. StockAvailableResponse / Unavailable| OrderApi
    OrderApi -->|8. Відповідь клієнту| Client
```

---

## ⚡ Чому MassTransit замість сирого RabbitMQ.Client?

У модулі `03-RabbitMQ-LowLevel` ми побачили, скільки ручного коду вимагає брокер:
* Ручне створення каналів (`CreateChannelAsync`), оголошення exchanges, черг і bindings.
* Ручна серіалізація/десеріалізація JSON і перетворення в байтові масиви `ReadOnlyMemory<byte>`.
* Ручний ACK/NACK і логіка перенаправлення в DLX.

**MassTransit бере всю цю рутину на себе:**
1. **Автоматична топологія:** Створює типізовані обмінники за назвою інтерфейсу/рекорду (`Contracts:OrderCreatedEvent`) і прив'язує черги консьюмерів у стилі kebab-case.
2. **Вбудовані політики повторів (Retry):** Якщо сервіс тимчасово збійнув, MassTransit автоматично перепробує обробку з заданим інтервалом (наприклад, 3 рази з інтервалом 2с).
3. **Автоматичні черги `_error` і `_skipped`:** Якщо після всіх повторів повідомлення так і не обробилося, воно автоматично переноситься у чергу `order-created_error` разом із повним StackTrace помилки у заголовках.
4. **Синхронний Request/Response через шину:** Дозволяє одному сервісу запитати дані у іншого без прямого HTTP-з'єднання, зберігаючи слабку зв'язаність (Loose Coupling).

---

## 📂 Склад проєктів

| Проєкт | Роль в системі |
| :--- | :--- |
| **`Contracts`** | Спільна бібліотека контрактів подій та запитів: `OrderCreatedEvent`, `CheckStockRequest`, `StockAvailableResponse`, `StockUnavailableResponse`. |
| **`Order.Api`** | REST API для створення замовлень та перевірки наявності. Впроваджує `IPublishEndpoint` та `IRequestClient<CheckStockRequest>`. |
| **`Warehouse.Worker`** | Фоновий воркер складу. Містить консьюмери `OrderCreatedConsumer`, `CheckStockConsumer` та `OrderCreatedFaultConsumer`. |
| **`Aspire.AppHost`** | Оркестратор Aspire, що піднімає контейнер RabbitMQ і запускає обидва сервіси. |
| **`Aspire.ServiceDefaults`** | Телеметрія OpenTelemetry, HealthChecks і Service Discovery. |

---

## 🧪 Практичний сценарій тестування через Swagger

1. **Запустіть AppHost:**
   ```bash
   cd 04-Event-Driven-MassTransit/Aspire.AppHost
   dotnet run
   ```
2. Відкрийте Swagger `order-api`.

### Сценарій 1: Publish / Subscribe (Успішна обробка)
* Виконайте `POST /api/orders`:
  ```json
  {
    "customerName": "Олександр",
    "productId": "11111111-1111-1111-1111-111111111111",
    "productName": "Смарт-годинник Pro Pulse",
    "unitPrice": 199.99,
    "quantity": 1
  }
  ```
* У логах `Warehouse.Worker`: `📦 [OrderCreatedConsumer] Отримано замовлення ... зарезервовано на складі!`

### Сценарій 2: Демонстрація Retry та відправки в чергу `_error`
* Виконайте `POST /api/orders`, передавши в полі `customerName` значення зі словом **`SimulateError`**:
  ```json
  {
    "customerName": "Іван-SimulateError",
    "productId": "11111111-1111-1111-1111-111111111111",
    "productName": "Смарт-годинник Pro Pulse",
    "unitPrice": 199.99,
    "quantity": 1
  }
  ```
* У логах `Warehouse.Worker` ви побачите:
  1. `💥 [OrderCreatedConsumer] Імітація збою! Спроба №1`
  2. Пауза 2 секунди $\to$ `💥 Спроба №2`
  3. Пауза 2 секунди $\to$ `💥 Спроба №3`
  4. Вичерпання повторів $\to$ спрацьовує `💀 [FAULT CONSUMER] Замовлення остаточно впало!`
* Зайдіть у RabbitMQ Management UI (`http://localhost:15672`): ви побачите, що повідомлення безпечно осіло в черзі `order-created_error` і не було втрачено!

### Сценарій 3: Request / Response через шину
* Виконайте `POST /api/orders/check-stock` з кількістю `2` шт. (товар є на складі):
  * Отримано статус `200 OK`, `InStock`, код підтвердження `STOCK-OK-...`.
* Виконайте `POST /api/orders/check-stock` з кількістю `99` шт. (перевищує залишок):
  * Отримано статус `409 Conflict`, `OutOfStock`, причина: `Недостатньо залишку на складі`.
