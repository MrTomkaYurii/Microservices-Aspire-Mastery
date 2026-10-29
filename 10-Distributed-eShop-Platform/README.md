# 10. Distributed eShop Platform (.NET 10 & Aspire Capstone)

> **Фінальний синтез курсу мікросервісів:**  
> Повнофункціональна еталонна платформа електронної комерції **eShop**, де об'єднані всі попередні 9 технологій:
> - **Оркестрація:** .NET Aspire 9.5
> - **API Gateway:** YARP (Yet Another Reverse Proxy)
> - **Каталог товарів:** Catalog Service (PostgreSQL + EF Core Migrations)
> - **Кошик покупця:** Basket Service (Redis HybridCache L1/L2 + PostgreSQL)
> - **Замовлення:** Ordering Service (SQL Server + MassTransit Outbox + RabbitMQ.Client v7)
> - **Контракти:** Shared.Messaging
> - **Вітрина:** WebApp (UI для покупців)
> - **Інфраструктура:** RedisInsight, PgAdmin, RabbitMQ Management Web UI

---

## 📂 Структура рішень та сервісів

- **`AspireApp/AspireApp.AppHost`** — генеральний оркестратор усього стенду.
- **`AspireApp/AspireApp.ServiceDefaults`** — стандартизована діагностика OpenTelemetry та Health Checks.
- **`YarpApiGateway`** — єдина вхідна точка доступу.
- **`Catalog`** — управління товарами та категоріями.
- **`Basket`** — кешування та кошики на HybridCache.
- **`Ordering`** — збереження замовлень та обробка транзакційних подій.
- **`Shared.Messaging`** — спільні події та повідомлення.
- **`WebApp`** — клієнтський інтерфейс магазину.

Детальний рух даних, взаємодію між сервісами та матрицю технологій дивіться у [STRUCTURE-AND-DATA-FLOW.md](STRUCTURE-AND-DATA-FLOW.md).

---

## 🚀 Запуск усієї платформи
```bash
dotnet run --project AspireApp/AspireApp.AppHost/AspireApp.AppHost.csproj
```
