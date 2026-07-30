# 📊 Distributed Tracing & Health Checks in Microservices

## 1. W3C TraceContext & OpenTelemetry
Кожен HTTP-запит або повідомлення RabbitMQ передає заголовки:
- `traceparent`: `00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01`
- `tracestate`

Завдяки цьому запит від користувача через Yarp Gateway -> Basket -> RabbitMQ -> Ordering простежується як єдиний розподілений трейс у Aspire Dashboard або Jaeger.

## 2. Health Checks у мікросервісах
- **/health/live (Liveness):** чи живий процес сервісу? (Якщо ні -> оркестратор перезапускає под).
- **/health/ready (Readiness):** чи доступні залежності: база даних, Redis, черга RabbitMQ?
