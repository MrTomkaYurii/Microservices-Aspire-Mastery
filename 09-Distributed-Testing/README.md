# 09. Distributed Testing (.NET 10 & Aspire)

> **Виробничий модуль з інтеграційного та E2E тестування мікросервісних систем:**  
> Використання офіційної бібліотеки **`Aspire.Hosting.Testing`** для підняття всього кластера у пам'яті всередині тестів xUnit без ручного Docker Compose.

---

## 🛠️ Стек технологій
- **Платформа:** .NET 10 (net10.0)
- **Оркестрація тестування:** Aspire.Hosting.Testing 9.5
- **Тестовий фреймворк:** xUnit 2.9 + Microsoft.NET.Test.Sdk
- **Документація:** Swagger UI / OpenAPI

---

## 📂 Структура проєкту

- **`Aspire.AppHost`** — оркестратор кластера для запуску Sample.Api (Порт дашборду: `17009`).
- **`Aspire.ServiceDefaults`** — спільні налаштування OpenTelemetry та Health Checks.
- **`Sample.Api`** — цільовий сервіс з класичними контролерами (`Step1_ItemsController`).
- **`Tests.ClusterIntegration`** — набір тестів:
  - `Step1_ClusterLifecycleTests` — запуск та коректна зупинка кластера.
  - `Step2_HttpEndpointIntegrationTests` — перевірка контрактів через типізований `HttpClient`.
  - `Step3_HealthCheckClusterTests` — верифікація liveness/readiness probes.

Повний опис потоків даних дивіться у [STRUCTURE-AND-DATA-FLOW.md](STRUCTURE-AND-DATA-FLOW.md).

---

## 🚀 Запуск тестів
```bash
dotnet test 09-Distributed-Testing.slnx
```
