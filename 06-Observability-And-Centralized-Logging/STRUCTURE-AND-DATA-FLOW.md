# 06. Архітектура, структура проєктів та рух даних: Спостережуваність та Централізоване Логування

> **Повна технічна специфікація чотирьох стовпів діагностики мікросервісів на .NET 10 та .NET Aspire: дворежимні перевірки здоров'я (`Liveness` vs `Readiness`), розподілене трасування за стандартом `W3C TraceContext`, високошвидкісні метрики `System.Diagnostics.Metrics` та семантичне логування `Serilog + Seq`.**

---

## 🏛️ Загальна архітектурна схема та потоки телеметрії

```mermaid
flowchart TD
    subgraph ClientLayer ["Зовнішній шар"]
        User["👤 Інженер / Клієнт / Swagger UI (/)"]
    end

    subgraph Orchestration ["Оркестрація та Телеметрія (.NET Aspire)"]
        AppHost["Aspire.AppHost"]
        SeqContainer[("🔍 Seq Log Server<br/>(Індексація JSON-подій)")]
        Dashboard["📊 Aspire Dashboard<br/>(OTLP Collector: Traces, Metrics, Logs)"]
    end

    subgraph Service ["Мікросервіс Orders.Api (.NET 10)"]
        Step1["Step1_HealthChecksController<br/>(/api/step1-health)"]
        Step2["Step2_DistributedTracingController<br/>(/api/step2-tracing)"]
        Step3["Step3_MetricsController<br/>(/api/step3-metrics)"]
        Step4["Step4_CentralizedLoggingController<br/>(/api/step4-logging)"]
        
        Diagnostics["DiagnosticsConfig<br/>(ActivitySource + Meter)"]
        HealthProbes["HealthCheckService + PaymentGatewayHealthCheck<br/>(/alive vs /health)"]
        SerilogPipeline["Serilog Logger Pipeline<br/>(Enrich.FromLogContext + WriteTo.Seq)"]
    end

    User -->|"Крок 1: Діагностика проб"| Step1
    User -->|"Крок 2: Генерація Spans"| Step2
    User -->|"Крок 3: Запис метрик"| Step3
    User -->|"Крок 4: Семантичні логи"| Step4

    Step1 --> HealthProbes
    Step2 --> Diagnostics
    Step3 --> Diagnostics
    Step4 --> SerilogPipeline

    Diagnostics -.->|"W3C Spans & Metrics (gRPC OTLP)"| Dashboard
    SerilogPipeline ==>|"HTTP Batch JSON Events"| SeqContainer
```

---

## 🌲 Дерево файлової структури модуля

```text
06-Observability-And-Centralized-Logging/
├── 06-Observability-And-Centralized-Logging.slnx # Рішення .NET 10 для модуля спостережуваності
├── README.md                                     # Посібник користувача з покроковими тестами
├── STRUCTURE-AND-DATA-FLOW.md                    # ЦЕЙ ДОКУМЕНТ: повна технічна специфікація
│
├── Aspire.AppHost/                               # Оркестратор .NET Aspire
│   ├── Program.cs                                # Підняття контейнера Seq (builder.AddSeq("seq")) та Orders.Api
│   ├── Aspire.AppHost.csproj                     # Aspire.Hosting.AppHost + Aspire.Hosting.Seq
│   └── appsettings.json                          # Налаштування оркестратора
│
├── Aspire.ServiceDefaults/                       # Єдиний стандарт телеметрії мікросервісів
│   ├── Extensions.cs                             # Налаштування OpenTelemetry (Tracing, Metrics, OTLP) та /alive, /health
│   └── Aspire.ServiceDefaults.csproj             # Пакетні посилання OpenTelemetry
│
└── Orders.Api/                                   # Доменний сервіс замовлень (Swagger UI у корені /)
    ├── Program.cs                                # Конфігурація Serilog + Seq, HealthChecks, Controllers та Swagger UI
    ├── Orders.Api.csproj                         # Serilog.AspNetCore, Serilog.Sinks.Seq, Swashbuckle
    ├── Controllers/
    │   ├── Step1_HealthChecksController.cs       # КРОК 1: Управління та моніторинг проб Liveness (/alive) та Readiness (/health)
    │   ├── Step2_DistributedTracingController.cs # КРОК 2: Побудова дерева Spans (ActivitySource), Тегів та Baggage
    │   ├── Step3_MetricsController.cs            # КРОК 3: Лічильники (Counter), Гістограми (Histogram) та Датчики (Gauge)
    │   └── Step4_CentralizedLoggingController.cs # КРОК 4: Структуроване логування зі Scopes у централізований Seq
    ├── Diagnostics/
    │   └── DiagnosticsConfig.cs                  # Реєстрація доменних ActivitySource та Meter інструментів
    └── Health/
        └── PaymentGatewayHealthCheck.cs          # Кастомна проба готовності платіжного шлюзу з тегом "ready"
```

---

## 🧩 Взаємодія класів та діагностичних компонентів (Dependency Graph)

```mermaid
classDiagram
    class AppHost {
        +AddSeq("seq")
        +AddProject~Orders_Api~("orders-api")
        +WithReference(seq)
    }

    class DiagnosticsConfig {
        +string ServiceName
        +ActivitySource ActivitySource
        +Meter Meter
        +Counter~long~ OrdersPlacedCounter
        +Histogram~double~ OrderProcessingDuration
        +ObservableGauge~int~ ActiveCartsGauge
        +UpdateActiveCarts(delta)
    }

    class PaymentGatewayHealthCheck {
        +HealthStatus SimulatedStatus
        +string StatusReason
        +CheckHealthAsync(context, ct) Task~HealthCheckResult~
    }

    class Step1_HealthChecksController {
        -HealthCheckService _healthCheckService
        +GetHealthReport() Task~IActionResult~
        +SimulatePaymentGatewayStatus(request) IActionResult
    }

    class Step2_DistributedTracingController {
        -ILogger _logger
        +StartDistributedTrace(request) Task~IActionResult~
    }

    class Step3_MetricsController {
        +RecordOrderMetrics(request) Task~IActionResult~
        +AdjustCartGauge(delta) IActionResult
    }

    class Step4_CentralizedLoggingController {
        -ILogger _logger
        +LogStructuredEvent(request) IActionResult
        +SimulateExceptionLog(orderId) IActionResult
    }

    AppHost ..> Step1_HealthChecksController : Запускає Orders.Api та інжектує Seq URL
    Step1_HealthChecksController --> PaymentGatewayHealthCheck : Читає та змінює стан готовності
    Step2_DistributedTracingController --> DiagnosticsConfig : Стартує Root та Child Activities (Spans)
    Step3_MetricsController --> DiagnosticsConfig : Оновлює Counter, Histogram та Gauge
    Step4_CentralizedLoggingController ..> AppHost : Надсилає події у контейнер Seq через Serilog
```

---

## 🔄 Покроковий рух даних: 4 навчальні етапи

### 📘 КРОК 1: Дворежимні перевірки здоров'я (`Liveness` vs `Readiness`)

> **Чому не можна вішати перевірку БД на Liveness-пробу?** Якщо база даних перезавантажується протягом 10 секунд, а перевірка БД підключена до `/alive`, Kubernetes вирішить, що всі 20 реплік вашого мікросервісу «зависли», і одночасно вб'є їх усі, спричинивши тотальний даунтайм!

```mermaid
sequenceDiagram
    autonumber
    actor Admin as 👤 Інженер / Swagger
    participant API as Orders.Api (Step1_HealthChecksController)
    participant Probe as PaymentGatewayHealthCheck
    participant LoadBalancer as ⚖️ Оркестратор / Балансувальник

    Admin->>API: POST /api/step1-health/simulate-status { newStatus: "Unhealthy" }
    API->>Probe: Встановлює SimulatedStatus = HealthStatus.Unhealthy
    API-->>Admin: 200 OK (Стан платіжного шлюзу переведено у відмову)

    LoadBalancer->>API: GET /alive (Liveness Probe - тільки тег "live")
    Note over API: Перевіряє лише те, що процес Kestrel відповідає
    API-->>LoadBalancer: 200 OK (Healthy) -> Контейнер НЕ перезапускається!

    LoadBalancer->>API: GET /health (Readiness Probe - усі зареєстровані перевірки)
    API->>Probe: CheckHealthAsync()
    Probe-->>API: HealthCheckResult.Unhealthy("Шлюз недоступний")
    API-->>LoadBalancer: 503 Service Unavailable -> Балансувальник тимчасово відводить клієнтський трафік!
```

---

### 📘 КРОК 2: Наскрізне трасування за стандартом `W3C TraceContext`

> **Структура заголовка `traceparent`:** `00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01`, де `00` — версія, `32 hex-символи` — наскрізний `TraceId`, `16 hex-символів` — поточний `SpanId`, `01` — прапорець запису (`Sampled`).

```mermaid
sequenceDiagram
    autonumber
    actor Client as 👤 Клієнт / Swagger
    participant API as Step2_DistributedTracingController
    participant RootSpan as Activity: PlaceCustomerOrder (Root)
    participant DbSpan as Activity: QueryDatabase.SaveOrder (Child 1)
    participant PaySpan as Activity: ExternalCall.StripeCharge (Child 2)
    participant OTLP as 📊 Aspire Dashboard (OTLP)

    Client->>API: POST /api/step2-tracing/start-trace { orderId: "ORD-777", amount: 450 }
    API->>RootSpan: StartActivity("PlaceCustomerOrder", Server)
    Note over RootSpan: Присвоєно TraceId = X, SpanId = A, Baggage: user.tier=PremiumCustomer

    RootSpan->>DbSpan: StartActivity("QueryDatabase.SaveOrder", Internal)
    Note over DbSpan: Успадковує TraceId = X, ParentSpanId = A, власний SpanId = B
    DbSpan->>DbSpan: SetTag("db.system", "postgresql") + імітація SQL (35 ms)
    DbSpan-->>RootSpan: Stop Activity B

    RootSpan->>PaySpan: StartActivity("ExternalCall.StripeCharge", Client)
    Note over PaySpan: Успадковує TraceId = X, ParentSpanId = A, власний SpanId = C
    PaySpan->>PaySpan: AddEvent("StripeTokenReceived") + імітація HTTP (55 ms)
    PaySpan-->>RootSpan: Stop Activity C

    RootSpan->>RootSpan: AddEvent("OrderSuccessfullyPersisted") -> Stop Activity A
    RootSpan-.->OTLP: Асинхронний пакетний експорт усіх 3 Spans по gRPC OTLP
    API-->>Client: 200 OK (Повертає TraceId, ієрархію Spans та заголовок traceparent)
```

---

### 📘 КРОК 3: Числові метрики OpenTelemetry (`Counter`, `Histogram`, `ObservableGauge`)

```mermaid
sequenceDiagram
    autonumber
    actor Client as 👤 Клієнт / Swagger
    participant API as Step3_MetricsController
    participant Meter as DiagnosticsConfig.Meter ("Orders.Api.Meters")
    participant OTLP as 📊 Aspire Dashboard / Prometheus

    Client->>API: POST /api/step3-metrics/record-order { currency: "USD", tier: "VIP" }
    API->>API: Stopwatch вимірює тривалість обробки (наприклад, 64 ms)
    API->>Meter: OrdersPlacedCounter.Add(1, currency="USD", tier="VIP")
    API->>Meter: OrderProcessingDuration.Record(64, tier="VIP")
    API-->>Client: 200 OK (Метрики оновлено в пам'яті без блокування I/O)

    Client->>API: POST /api/step3-metrics/adjust-cart-gauge?delta=5
    API->>Meter: Interlocked.Add(ref _activeShoppingCarts, 5)
    
    Note over Meter,OTLP: Фоновий потік OpenTelemetry періодично знімає агреговані зрізи
    Meter-.->OTLP: Експорт часових рядів (Time Series) для побудови графіків p50/p95/p99
```

---

### 📘 КРОК 4: Семантичне логування у централізований сервер `Seq`

```mermaid
sequenceDiagram
    autonumber
    actor Client as 👤 Клієнт / Swagger
    participant API as Step4_CentralizedLoggingController
    participant Scope as ILogger.BeginScope (Context)
    participant Serilog as Serilog Sink Pipeline
    participant Seq as 🔍 Seq Container (Port 5341)

    Client->>API: POST /api/step4-logging/log-event { orderId: "ORD-900", tenantId: "eu-west", amount: 2500 }
    API->>Scope: BeginScope(CorrelationId, TenantId="eu-west", Environment="Development-Aspire")
    API->>Serilog: LogInformation("Отримано замовлення {OrderId} на суму {Amount}...", "ORD-900", 2500)
    API->>Serilog: LogWarning("Замовлення {OrderId} перевищує поріг {Threshold}", "ORD-900", 1000)
    Note over Serilog: Замість склеювання рядка формує JSON-словник властивостей!
    Serilog->>Seq: HTTP POST (CLEF JSON Batch з полями OrderId, Amount, TenantId, TraceId)
    API-->>Client: 200 OK (Повертає готовий пошуковий вираз для Seq UI)
```

---

## 💾 Де та що зберігається (Telemetry Sinks & Instruments)

| Сутність / Інструмент | Ім'я в системі | Тип даних | Де зберігається / Візуалізується | Призначення |
| :--- | :--- | :--- | :--- | :--- |
| **Liveness Probe** | `/alive` | HTTP 200 / 503 | Пам'ять процесу Kestrel | Сигнал для оркестратора, що процес не завис у Deadlock |
| **Readiness Probe** | `/health` (`payment_gateway`) | HTTP 200 / 503 + JSON | `PaymentGatewayHealthCheck` | Сигнал для балансувальника, чи готовий сервіс приймати замовлення |
| **ActivitySource** | `Orders.Api.Tracing` | W3C Spans (`Activity`) | Aspire Dashboard $\to$ **Traces** | Побудова дерева викликів `PlaceCustomerOrder` та його дочірніх операцій |
| **Counter** | `shop.orders.placed` | `Counter<long>` | Aspire Dashboard $\to$ **Metrics** | Монотонно зростаючий лічильник замовлень у розрізі валют і тарифів |
| **Histogram** | `shop.order.duration.ms` | `Histogram<double>` | Aspire Dashboard $\to$ **Metrics** | Розподіл часу обробки для обчислення медіани (`p50`) та хвостів (`p95`, `p99`) |
| **ObservableGauge** | `shop.active_carts` | `ObservableGauge<int>` | Aspire Dashboard $\to$ **Metrics** | Поточна кількість активних кошиків у реальному часі |
| **Structured Logs** | `Application = "Orders.Api"` | CLEF JSON Document | Контейнер **Seq** (`http://localhost:5341`) | Повнотекстовий та індексований пошук за полями `OrderId`, `TenantId`, `CorrelationId` |
