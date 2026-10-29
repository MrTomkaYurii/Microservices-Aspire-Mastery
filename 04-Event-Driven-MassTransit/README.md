# 01. Custom MassTransit & RabbitMQ Low-Level Mechanics

> Повний практичний розбір ручної (custom) конфігурації шини повідомлень MassTransit поверх брокера RabbitMQ: конфігурація Host, віртуальних хостів, створення ексклюзивних та спільних черг, типізовані контракти, механізм Retry, Dead-Letter Exchanges та Request/Response pattern.

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
cd 01-Custom-MassTransit-And-RabbitMQ
dotnet build
```
