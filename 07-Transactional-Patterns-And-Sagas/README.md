# 07. Transactional Patterns & Sagas (.NET 10 & Aspire)

> **Виробничий модуль з патернів розподіленої узгодженості та надійності:**  
> Вирішення проблеми Dual-Write, патерн Transactional Outbox (як кастомний з конвеєром стійкості **Polly v8**, так і нативний **MassTransit EF Core Outbox**), а також Саги з компенсуючими транзакціями.

---

## 🛠️ Стек технологій
- **Платформа:** .NET 10 (net10.0)
- **Оркестрація:** .NET Aspire 9.5
- **База даних:** PostgreSQL (Npgsql + EF Core 9)
- **Брокер повідомлень:** RabbitMQ + MassTransit 8.3
- **Стійкість та відмовостійкість:** Polly v8 (ResiliencePipeline, Exponential Backoff, Jitter, Circuit Breaker)
- **Документація:** Swagger UI / OpenAPI

---

## 📂 Структура проєкту

- **`Aspire.AppHost`** — оркестратор Aspire, що піднімає PostgreSQL та RabbitMQ і підключає сервіс замовлень (Порт дашборду: `17007`).
- **`Aspire.ServiceDefaults`** — спільні налаштування OpenTelemetry, OTLP та Health Checks.
- **`Checkout.Api`** — бізнес-сервіс із чотирма покроковими контролерами:
  - `Step1_DualWriteProblemController` — наочна симуляція розсинхронізації між БД та брокером.
  - `Step2_TransactionalOutboxController` — атомарний запис події в таблицю Outbox та фоновий воркер з Polly v8.
  - `Step3_MassTransitOutboxController` — використання `AddEntityFrameworkOutbox` для безпечного масштабування.
  - `Step4_SagaStateMachineController` — покрокове виконання саги та компенсуючі транзакції при збої складу.

Повний опис потоків даних та діаграм дивіться у [STRUCTURE-AND-DATA-FLOW.md](STRUCTURE-AND-DATA-FLOW.md).

---

## 🚀 Швидкий запуск
```bash
dotnet run --project Aspire.AppHost/Aspire.AppHost.csproj
```
Відкрийте веб-панель Aspire за адресою: **https://localhost:17007**
