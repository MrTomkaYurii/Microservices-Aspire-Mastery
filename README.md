# 🌐 Microservices-Aspire-Mastery (.NET 10)

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![.NET Aspire](https://img.shields.io/badge/.NET%20Aspire-Orchestration-512BD4?logo=dotnet)](https://learn.microsoft.com/dotnet/aspire/)
[![RabbitMQ](https://img.shields.io/badge/RabbitMQ-Message%20Broker-FF6600?logo=rabbitmq)](https://www.rabbitmq.com/)
[![MassTransit](https://img.shields.io/badge/MassTransit-Event%20Driven-blue)](https://masstransit.io/)
[![Redis](https://img.shields.io/badge/Redis-Distributed%20Cache-DC382D?logo=redis)](https://redis.io/)
[![YARP](https://img.shields.io/badge/YARP-Reverse%20Proxy-blue)](https://microsoft.github.io/reverse-proxy/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

> **Повний практичний курс та виробничий стенд з мікросервісної архітектури на .NET 10 та .NET Aspire.**  
> Поєднує як глибоку ручну конфігурацію брокера повідомлень (MassTransit + RabbitMQ) на низькому рівні, так і сучасну cloud-ready оркестрацію сервісів через .NET Aspire, патерн Transactional Outbox, зворотний проксі YARP, стійкість Polly та розподілене трасування.

---

## 🏛️ Архітектурна карта платформи eShop & Aspire

```mermaid
flowchart TB
    Client["🌐 Клієнт / WebApp (Вітрина eShop)"]
    
    subgraph Gateway ["🚪 Рівень шлюзу"]
        Yarp["YARP API Gateway
Маршрутизація та балансування"]
    end
    
    subgraph Orchestration [".NET Aspire AppHost & Dashboard"]
        Aspire["Aspire Orchestrator
Service Discovery & Monitoring"]
        Dashboard["📊 Aspire Dashboard
Traces / Metrics / Logs"]
    end

    subgraph Microservices ["⚡ Сервіси платформи"]
        Catalog["Catalog Service
SQL Server / PostgreSQL"]
        Basket["Basket Service
Redis Distributed Cache"]
        Ordering["Ordering Service
Clean Architecture + CQRS"]
    end
    
    subgraph EventBus ["📨 Подієва шина та Брокер"]
        RabbitMQ[("🐰 RabbitMQ
Exchanges & Queues")]
        MassTransit["MassTransit Event Bus
Custom Pipes + Outbox"]
        Outbox[("Transactional Outbox
At-Least-Once Delivery")]
    end

    Client --> Yarp
    Yarp --> Catalog
    Yarp --> Basket
    Yarp --> Ordering
    
    Basket -->|Публікація Checkout| MassTransit
    MassTransit --> RabbitMQ
    RabbitMQ --> Ordering
    Ordering --> Outbox
    
    Aspire -.->|Запускає та моніторить| Catalog
    Aspire -.->|Запускає та моніторить| Basket
    Aspire -.->|Запускає та моніторить| Ordering
    Aspire -.->|Запускає та моніторить| Yarp
    Aspire -.-> Dashboard
```

---

## 📚 Модулі та структура курсу

| Модуль | Назва | Зміст та технології |
| :---: | :--- | :--- |
| **01** | **[Custom MassTransit & RabbitMQ](01-Custom-MassTransit-And-RabbitMQ)** | Ручна детальна збірка MassTransit без автоматичної магії: створення топологій брокера (Fanout/Direct/Topic), черги, Dead-Letter Exchanges, обробка помилок та повторні спроби (Retry Policies). |
| **02** | **[Aspire Fundamentals & Dashboard](02-Aspire-Fundamentals-And-Dashboard)** | Оркестрація мікросервісів у коді C#: проєкт AppHost, ServiceDefaults, Redis кеш, підключення контейнерів через Aspire, Service Discovery та інтерактивний Dashboard. |
| **03** | **[Distributed eShop Architecture](03-Distributed-eShop-Architecture)** | Повнофункціональна масштабована система: Catalog API, кошик Basket API на Redis, Ordering API на базі Clean Architecture, спільні контракти подій та зворотний проксі YARP Gateway. |
| **04** | **[Transactional Outbox & Resilience](04-Transactional-Outbox-And-Resilience)** | Надійність на рівні Enterprise: усунення проблеми подвійного запису через Transactional Outbox, ідемпотентність споживачів та налаштування політик Polly (Exponential backoff, Jitter, Circuit Breaker). |
| **05** | **[Observability, Tracing & Health](05-Observability-Tracing-And-Health)** | Наскрізне розподілене трасування (OpenTelemetry + W3C TraceContext) крізь HTTP та RabbitMQ, візуалізація спанів у Jaeger/Dashboard та дворівневі Health Checks (Liveness/Readiness). |

---

## ⚡ Швидкий старт

### Вимоги:
- **.NET SDK 10.0** або новіший (`dotnet --version`)
- **Docker Desktop** (для підняття контейнерів RabbitMQ, Redis, PostgreSQL)

### Клонування та збірка:
```bash
git clone https://github.com/MrTomkaYurii/Microservices-Aspire-Mastery.git
cd Microservices-Aspire-Mastery

# Зібрати всі рішення
dotnet build
```

---

## 👨‍💻 Авторство та ліцензія
- **Автор:** [MrTomkaYurii](https://github.com/MrTomkaYurii)
- **Ліцензія:** MIT
