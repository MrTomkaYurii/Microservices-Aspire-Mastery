# 04. Transactional Outbox Pattern & Polly Resilience

> Гарантії надійності в розподілених системах: патерн Transactional Outbox для усунення подвійного запису (Dual-Write Problem), фоновий `OutboxPublisherWorker`, та політики стійкості **Polly v8** (Exponential Backoff with Jitter, Circuit Breaker).

## 🛠️ Стек технологій
- **Платформа:** .NET 10 (`net10.0`)
- **Стійкість:** Polly v8 (`Polly.Core` 8.5+)
- **ORM:** Entity Framework Core 9/10
- **Патерни:** Transactional Outbox, Guaranteed At-Least-Once Delivery, Circuit Breaker, Exponential Retry with Jitter

---

## 📂 Проекти модуля

### 1. `Outbox.Resilience.Demo`
Повноцінний робочий демонстраційний мікросервіс:
- **Атомарний запис:** Замовлення (`Order`) та подія для публікації (`OutboxMessage`) записуються в одній транзакції EF Core.
- **Фоновий процесор:** `OutboxPublisherWorker` (`BackgroundService`) періодично вичитує непозначені повідомлення.
- **Polly v8 Pipeline:**
  - `AddRetry`: 3 спроби з експоненційною затримкою та jitter.
  - `AddCircuitBreaker`: Автоматичне розмикання ланцюга при збоях брокера повідомлень (>50% помилок за 10 сек).

### 🚀 Запуск
```bash
cd 04-Transactional-Outbox-And-Resilience/Outbox.Resilience.Demo
dotnet run
```

#### API Endpoints:
- `POST /api/orders` — Створення замовлення та збереження події в Outbox (повертає `202 Accepted`).
- `GET /api/orders` — Список замовлень.
- `GET /api/outbox` — Журнал Outbox повідомлень та їхніх статусів публікації.
