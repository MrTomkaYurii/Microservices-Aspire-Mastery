# 02. Centralized Caching with Redis & .NET 10 HybridCache

> **Централізоване кешування у розподілених системах: використання нового .NET 10 `HybridCache` (L1 In-Memory + L2 Redis), захист від Cache Stampede та наскрізна інвалідація кешу через Redis Pub/Sub між двома мікросервісами.**

---

## 🏛️ Архітектурна схема модуля

```mermaid
flowchart TD
    Client["👤 Клієнт / Swagger"]

    subgraph AspireHost [".NET Aspire AppHost & Dashboard"]
        AppHost["Aspire.AppHost"]
        Insight["📊 RedisInsight Web UI
(Аналіз ключів, пам'яті та TTL)"]
    end

    subgraph Microservices ["Мікросервіси"]
        BasketApi["🛒 Basket.Api
(Керує кошиками та счетами)"]
        PricingApi["🏷️ Pricing.Api
(Прайс-листи та знижки)"]
    end

    subgraph RedisInfrastructure ["Централізований шар Redis"]
        RedisL2[("🔴 Redis
(L2 Distributed Cache)")]
        PubSub{{"📡 Redis Pub/Sub
(Канал: 'pricing-channel')"}}
    end

    AppHost -.->|Оркеструє та моніторить| BasketApi
    AppHost -.->|Оркеструє та моніторить| PricingApi
    AppHost -.->|Піднімає контейнер| RedisL2
    RedisL2 -.-> Insight

    Client -->|1. GET /api/basket/john| BasketApi
    BasketApi <-->|2. Читання/Запис L1 RAM + L2 Redis| RedisL2

    Client -->|3. POST /api/pricing/update-price або apply-discount| PricingApi
    PricingApi -->|4. Publish PriceUpdatedEvent / DiscountAppliedEvent| PubSub
    PubSub -.->|5. Доставка сигналу інвалідації| BasketApi
    BasketApi -->|6. RemoveByTagAsync('baskets')| RedisL2
    
    Client -->|7. Наступний GET /api/basket/john| BasketApi
    BasketApi -->|8. Автоматичний перерахунок суми з новою ціною!| Client
```

---

## ⚡ Чому `HybridCache` у .NET 10 — це революція?

Раніше в .NET мікросервісах використовували або `IMemoryCache` (швидкий, але локальний для кожного інстансу і неузгоджений), або `IDistributedCache` (спільний у Redis, але кожен запит робить повільний мережевий round-trip та десеріалізацію JSON).

**.NET 10 `HybridCache` поєднує найкраще з обох світів:**
1. **L1 (Внутрішня пам'ять процесу):** Миттєва віддача з RAM без мережевих затримок (за замовчуванням 2 хвилини).
2. **L2 (Розподілений Redis):** Спільний для всіх реплік мікросервісу (за замовчуванням 10 хвилин).
3. **Захист від Cache Stampede:** Якщо 1000 клієнтів одночасно звернуться за кошиком або товаром, чий кеш протух, `HybridCache` виконає **лише 1 звернення до сховища**, а решту 999 запитів заблокує до отримання результату і віддасть їм свіжі дані з кешу.
4. **Інвалідація за тегами (`RemoveByTagAsync`):** Можливість в один виклик очистити всю групу ключів, наприклад, усі кошики за тегом `baskets`.

---

## 📂 Склад проєктів та їх призначення

| Проєкт | Стек | Опис |
| :--- | :--- | :--- |
| **`Aspire.AppHost`** | .NET Aspire 9.5+ / 10 | Піднімає контейнер Redis з веб-панеллю RedisInsight та реєструє залежності для `Basket.Api` і `Pricing.Api`. |
| **`Basket.Api`** | Minimal API + `HybridCache` | Сервіс кошика користувача. Зберігає кошик у `HybridCache` і містить фоновий підписник `PricingUpdateSubscriber`, що слухає Redis Pub/Sub канал `pricing-channel`. |
| **`Pricing.Api`** | Minimal API + `StackExchange.Redis` | Сервіс ціноутворення. Надає методи оновлення цін товарів і промокодів. При кожній зміні транслює подію через Redis Pub/Sub. |
| **`Aspire.ServiceDefaults`** | Shared Library | Встановлює базові стандарти телеметрії OpenTelemetry, Health Checks та Service Discovery. |

---

## 🧪 Практичний сценарій тестування (Крок за кроком)

1. **Запустіть AppHost:**
   ```bash
   cd 02-Centralized-Caching-Redis/Aspire.AppHost
   dotnet run
   ```
2. Відкрийте **Aspire Dashboard**. Знайдіть посилання на:
   - `basket-api` (Swagger)
   - `pricing-api` (Swagger)
   - `Redis Insight UI`
3. **Крок 1 (Перший запит — Cache Miss):**
   * У `basket-api` виконайте `GET /api/basket/john`.
   * У логах ви побачите: `🔍 [Cache MISS] Кошик не знайдено в HybridCache. Завантажуємо зі сховища...`
   * Повернуто кошик на суму **$359.97**.
4. **Крок 2 (Повторний запит — Cache Hit):**
   * Виконайте `GET /api/basket/john` ще раз.
   * У логах: `⚡ [Cache HIT] Кошик повернуто з HybridCache. Швидкість відповіді < 1ms!`
   * Зайдіть у **RedisInsight** і подивіться на створені ключі Redis!
5. **Крок 3 (Зміна ціни у сервісі Pricing.Api):**
   * Перейдіть у Swagger `pricing-api` $\to$ `POST /api/pricing/update-price`.
   * Передайте тіло:
     ```json
     {
       "productId": "11111111-1111-1111-1111-111111111111",
       "newPrice": 99.99,
       "reason": "Розпродаж вихідного дня Black Friday"
     }
     ```
   * У логах `pricing-api`: `📢 Сигнал інвалідації відправлено в Redis Pub/Sub!`
   * У логах `basket-api`: `⚡ [PricingUpdateSubscriber] Зміна ціни товару! HybridCache очищено за тегом 'baskets'.`
6. **Крок 4 (Перевірка оновленого підсумку):**
   * Знову виконайте `GET /api/basket/john` у `basket-api`.
   * Ви побачите, що ціна товару змінилася з $199.99 на $99.99, а загальний рахунок кошика миттєво перераховано до **$259.97**!
