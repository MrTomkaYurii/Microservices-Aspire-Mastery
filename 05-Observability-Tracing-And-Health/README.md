# 05. Observability, Tracing & Health Checks

> Моніторинг, розподілене трасування та перевірка працездатності: OpenTelemetry, W3C Trace Context, кастомні `ActivitySource` та `Meter`, інтеграція з Aspire Dashboard.

## 🛠️ Стек технологій
- **Платформа:** .NET 10 (`net10.0`)
- **Оркестрація:** .NET Aspire (`AspireApp.ServiceDefaults`)
- **Телеметрія:** OpenTelemetry .NET SDK
- **Діагностика:** System.Diagnostics (`ActivitySource`, `Meter`, `Counter`, `Histogram`)
- **Health Checks:** Microsoft.Extensions.Diagnostics.HealthChecks

---

## 📂 Проекти модуля

### 1. `Observability.Aspire.Demo`
Повноцінний робочий демонстраційний мікросервіс:
- **Custom Distributed Tracing:** Власний `ActivitySource ("Mastery.DistributedShop")` зі створенням спанів (`Activity`), тегів (`shop.order.id`, `shop.customer.id`) та подій (`ActivityEvent`).
- **Custom Metrics:** Власний `Meter ("Mastery.Shop.Metrics")` з лічильником замовлень (`shop.orders.placed`) та гістограмою затримки (`shop.order.duration.ms`).
- **Health Checks:** Реалізація `IHealthCheck` для моніторингу затримок сторонніх провайдерів (Payment Gateway) зі статусами `Healthy` та `Degraded`.
- **Aspire Service Defaults:** Автоматичний мапінг OTLP експортерів до Aspire Dashboard.

### 🚀 Запуск
```bash
cd 05-Observability-Tracing-And-Health/Observability.Aspire.Demo
dotnet run
```

#### API Endpoints:
- `POST /api/order` — Симуляція розподіленої обробки замовлення (повертає `TraceId`, `SpanId`, тривалість).
- `/alive` — Liveness probe (чи живий процес).
- `/health` — Readiness probe (перевірка стану бази, черги та Payment Gateway).
