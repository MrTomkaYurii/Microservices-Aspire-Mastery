# 06. Observability & Centralized Logging (.NET 10 & Aspire)

> **Повноцінний практичний модуль зі спостережуваності розподілених систем:**  
> Дворежимні перевірки здоров'я (`/alive` vs `/health`), наскрізний стандарт W3C TraceContext крізь сервіси, метрики OpenTelemetry (Counter, Histogram, Gauge) та централізоване логування у **Seq** через Serilog.

---

## 🛠️ Стек технологій
- **Платформа:** .NET 10 (net10.0)
- **Оркестрація:** .NET Aspire 9.5
- **Спостережуваність:** OpenTelemetry (.NET Extensions), W3C TraceContext
- **Логування:** Serilog + Serilog.Sinks.Seq
- **Сервер збору логів:** Seq (контейнер у Aspire AppHost)
- **Документація:** Swagger UI / OpenAPI

---

## 📂 Структура проєкту

- **`Aspire.AppHost`** — оркестратор Aspire, що піднімає контейнер Seq та зв'язує його з API (Порт дашборду: `17006`).
- **`Aspire.ServiceDefaults`** — спільні налаштування OpenTelemetry, OTLP експортерів та Health Checks.
- **`Orders.Api`** — бізнес-сервіс із чотирма покроковими контролерами:
  - `Step1_HealthChecksController` — дворівнева діагностика Liveness vs Readiness.
  - `Step2_DistributedTracingController` — наскрізні Spans, TraceId та Baggage.
  - `Step3_MetricsController` — вимірювання RPS, Latency через Meter/Counter/Histogram.
  - `Step4_CentralizedLoggingController` — структуровані події з контекстними Scopes у Seq.

Детальний опис класів та рух даних дивіться у [STRUCTURE-AND-DATA-FLOW.md](STRUCTURE-AND-DATA-FLOW.md).

---

## 🚀 Швидкий запуск
```bash
dotnet run --project Aspire.AppHost/Aspire.AppHost.csproj
```
Відкрийте веб-панель Aspire за адресою: **https://localhost:17006**
