# 03. Distributed eShop Enterprise Architecture

> Повнофункціональна еталонна платформа електронної комерції eShop на .NET 10: Catalog Service (SQL), Basket Service (Redis), Ordering Service (Events & Outbox), Shared.Messaging (контракти інтеграційних подій), YARP API Gateway для єдиної точки входу та WebApp вітрина.

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
cd 03-Distributed-eShop-Architecture
dotnet build
```
