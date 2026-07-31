# 05. Distributed Observability, OpenTelemetry & Health Checks

> Наскрізне трасування запитів (Distributed Tracing з W3C TraceContext) через API Gateway, HTTP та RabbitMQ повідомлення, візуалізація трейсів у Jaeger та Aspire Dashboard, багаторівневі Health Checks (/health/live, /health/ready) для Kubernetes та хмарних середовищ.

## 🛠️ Стек технологій
- **Платформа:** .NET 10 (net10.0)
- **Оркестрація:** .NET Aspire 9.5+ / 10
- **Брокер повідомлень:** RabbitMQ + MassTransit (Custom & Outbox)
- **Кеш & Сховище:** Redis, SQL Server, PostgreSQL

---

## 📂 Вміст модуля
У цьому модулі представлені готові виробничі зразки мікросервісних компонентів та архітектурних рішень.

## 🚀 Запуск
```bash
cd 05-Observability-Tracing-And-Health
dotnet build
```
