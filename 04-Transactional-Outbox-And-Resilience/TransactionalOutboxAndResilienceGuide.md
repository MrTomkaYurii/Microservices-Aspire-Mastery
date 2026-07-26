# 🛡️ Transactional Outbox Pattern & Resilience Guide

## 1. Проблема Dual-Write у розподілених системах
Коли сервіс одночасно зберігає дані в БД та відправляє подію в RabbitMQ:
1. Якщо БД зберегла, а брокер впав -> **втрата події**.
2. Якщо брокер прийняв, а транзакція БД відкотилася -> **фантомна подія**.

## 2. Як працює Transactional Outbox
1. У тій самій локальній транзакції БД, що й зміна сутності, рядок записується в таблицю `OutboxMessage`.
2. Фоновий процес (MassTransit Outbox / Worker) періодично вичитує неповідомлені рядки, публікує їх у RabbitMQ і позначає `ProcessedOnUtc`.
3. Забезпечується гарантія доставки **At-least-once**.

## 3. Стійкість із Polly (.NET 10)
```csharp
builder.Services.AddHttpClient("ExternalService")
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.BackoffType = DelayBackoffType.Exponential;
        options.Retry.UseJitter = true;
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
        options.CircuitBreaker.FailureRatio = 0.5;
    });
```
