# 04. Transactional Outbox Pattern & Polly Resilience

> Гарантії надійності в розподілених системах: патерн Transactional Outbox для усунення подвійного запису (Dual-Write Problem), ідемпотентні споживачі, політики стійкості Polly (Exponential Backoff with Jitter, Circuit Breaker, Rate Limiter) на рівні HTTP та брокера.

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
cd 04-Transactional-Outbox-And-Resilience
dotnet build
```
