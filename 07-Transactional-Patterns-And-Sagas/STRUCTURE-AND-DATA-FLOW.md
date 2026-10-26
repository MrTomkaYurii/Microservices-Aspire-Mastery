# 📐 Модуль 07: Архітектурна структура та рух даних (Transactional Patterns & Sagas)

> **Мета модуля:** Опанувати надійність розподілених систем, вирішити проблему Dual-Write через патерн Transactional Outbox (як кастомний з Polly v8, так і промисловий MassTransit), та побудувати стійкий життєвий цикл Саги з компенсуючими транзакціями.

---

## 🏛️ Загальна схема архітектури та потоку даних

```mermaid
flowchart TD
    subgraph ClientLayer ["Зовнішній шар"]
        User["Клієнт / Інженер / Swagger"]
    end

    subgraph Orchestration ["Оркестрація .NET Aspire"]
        AppHost["Aspire.AppHost (Порт: 17007)"]
        Postgres["PostgreSQL Database (checkoutdb)"]
        RabbitMQ["RabbitMQ Message Broker"]
    end

    subgraph Service ["Checkout.Api (.NET 10)"]
        Step1["Step1_DualWriteProblemController
(/api/step1-dual-write)"]
        Step2["Step2_TransactionalOutboxController
(/api/step2-outbox)"]
        Step3["Step3_MassTransitOutboxController
(/api/step3-masstransit-outbox)"]
        Step4["Step4_SagaStateMachineController
(/api/step4-saga)"]

        PollyWorker["OutboxPublisherWorker
(Polly v8: Retry + Circuit Breaker)"]
        MTEFOutbox["MassTransit Outbox
(Inbox/Outbox States)"]
    end

    User -->|HTTP Запит| Step1
    User -->|HTTP Запит| Step2
    User -->|HTTP Запит| Step3
    User -->|HTTP Запит| Step4

    Step1 -->|Прямий Save & Publish (Вразливо!)| Postgres
    Step2 -->|Атомарний SQL Transaction| Postgres
    Step3 -->|EF Core Outbox Transaction| Postgres
    Step4 -->|Saga State & Compensations| Postgres

    Postgres -.->|Опитування нових подій| PollyWorker
    Postgres -.->|Автоматичний диспатч| MTEFOutbox

    PollyWorker -->|Resilient Publish| RabbitMQ
    MTEFOutbox -->|Guaranteed Publish| RabbitMQ
```

---

## 🪜 Покрокова методологія: Від простого до складного

### 🔹 Крок 1: Проблема подвійного запису (`Step1_DualWriteProblemController`)
- **Маршрут:** `/api/step1-dual-write`
- **Проблема:** Неможливо гарантувати атомарність між двома незалежними мережевими системами (SQL та RabbitMQ) без розподіленого координатора.
- **Сценарій:** Збереження замовлення в БД пройшло успішно, але зв'язок з брокером перервався. Результат — замовлення є, але повідомлення на склад не пішло (розсинхронізація бізнес-стану).

### 🔹 Крок 2: Кастомний Transactional Outbox з Polly v8 (`Step2_TransactionalOutboxController`)
- **Маршрут:** `/api/step2-outbox`
- **Рішення:** Замість відправки в брокер під час HTTP-запиту, замовлення та подія `OutboxMessage` записуються в одній ACID-транзакції PostgreSQL.
- **Стійкість:** Фоновий воркер `OutboxPublisherWorker` використовує конвеєр стійкості **Polly v8**:
  - `Exponential Backoff + Jitter`: захист брокера від перевантаження при повторних спробах.
  - `Circuit Breaker`: автоматичне відсікання запитів на 15 секунд при 50% збоїв.

### 🔹 Крок 3: Промисловий MassTransit Native Outbox (`Step3_MassTransitOutboxController`)
- **Маршрут:** `/api/step3-masstransit-outbox`
- **Проблема кастомного воркера:** Складність розподілених блокувань при масштабуванні на 10 реплік (конфлікти паралельного читання).
- **Рішення:** Метод `AddEntityFrameworkOutbox<AppDbContext>()`:
  - Розробник викликає стандартний `IPublishEndpoint.Publish()`.
  - MassTransit перехоплює подію і коммітить її разом із сутностями бізнесу через системні таблиці `OutboxMessage` та `InboxState`.

### 🔹 Крок 4: Сага та компенсуючі транзакції (`Step4_SagaStateMachineController`)
- **Маршрут:** `/api/step4-saga`
- **Концепція:** У мікросервісах розподілені транзакції (2PC) заборонені через блокування ресурсів. Замість цього використовується **Saga**:
  - Послідовність локальних кроків: Створити $\to$ Оплатити $\to$ Зарезервувати $\to$ Доставити.
  - При помилці на етапі Складу (товару немає) Сага запускає **зворотну дію (Компенсацію)**: повертає кошти клієнту та анулює замовлення.

---

## 🚀 Як протестувати самостійно

1. **Запуск через .NET CLI:**
   ```powershell
   dotnet run --project 07-Transactional-Patterns-And-Sagas/Aspire.AppHost/Aspire.AppHost.csproj
   ```
2. **Панель Aspire Dashboard:** `https://localhost:17007`
3. **Swagger UI:** `http://localhost:порт/`
