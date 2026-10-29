# 02. Архітектура, покрокова структура та рух даних: Централізоване кешування (Redis & HybridCache)

> **Покроковий навчальний майстер-клас: від найпростішого локального кешу в оперативній пам'яті (`IMemoryCache`) до розподіленого Redis (`IDistributedCache`), гібридного кешу (`HybridCache`) та подієвої синхронізації між мікросервісами через `Redis Pub/Sub`.**

---

## 🎯 Преамбула: Навіщо потрібне кешування та яка еволюція підходів?

У мікросервісній архітектурі база даних — це найвужче місце (bottleneck). Якщо кожен запит користувача йде напряму в базу даних, під час пікових навантажень (Чорна п'ятниця, розпродажі) сервер бази даних неминуче вичерпує пул з'єднань і падає.

Кешування — це збереження результатів важких запитів у швидкому сховищі (у пам'яті), щоб наступні звернення отримували результат за частки мілісекунди без навантаження на БД.

Але в мікросервісах кешування проходить 4 послідовні еволюційні етапи:

```mermaid
flowchart LR
    Step1["Крок 1: IMemoryCache
(Локальна RAM процесу)"] --> Step2["Крок 2: IDistributedCache
(Спільний Redis)"]
    Step2 --> Step3["Крок 3: HybridCache
(L1 RAM + L2 Redis + Stampede Guard)"]
    Step3 --> Step4["Крок 4: Redis Pub/Sub
(Синхронізація між мікросервісами)"]
```

---

## 🌲 Покрокова файлова структура модуля

```text
02-Centralized-Caching-Redis/
├── 02-Centralized-Caching.slnx              # Рішення .NET 10 для модуля кешування
├── README.md                                 # Посібник користувача з тестування в Swagger
├── STRUCTURE-AND-DATA-FLOW.md                # ЦЕЙ ДОКУМЕНТ: повна технічна специфікація
│
├── Aspire.AppHost/                           # Оркестратор .NET Aspire
│   ├── AppHost.cs                            # Конфігурація Redis-контейнера "cache" та RedisInsight GUI
│   ├── Aspire.AppHost.csproj                 # Aspire.Hosting.AppHost + Aspire.Hosting.Redis
│   └── appsettings.json                      # Конфігурація хоста
│
├── Aspire.ServiceDefaults/                   # Спільні стандарти
│   ├── Extensions.cs                         # OpenTelemetry трейсинг кешу, метрики, HealthChecks
│   └── Aspire.ServiceDefaults.csproj         # Пакетні посилання
│
├── Basket.Api/                               # Мікросервіс кошика (Лабораторія кешування)
│   ├── Program.cs                            # Реєстрація IMemoryCache, IDistributedCache, HybridCache, Swagger
│   ├── Basket.Api.csproj                     # Aspire.StackExchange.Redis, HybridCache, лінк на Docs
│   ├── Controllers/
│   │   ├── Step1_InMemoryCacheController.cs  # КРОК 1: Локальний IMemoryCache у пам'яті поточного процесу
│   │   ├── Step2_DistributedRedisCacheController.cs # КРОК 2: Чистий розподілений кеш Redis (IDistributedCache)
│   │   ├── Step3_HybridCacheController.cs    # КРОК 3: Дворівневий HybridCache (L1 + L2 + Cache Stampede Protection)
│   │   └── Step4_CrossServiceCacheSyncController.cs # КРОК 4: Читання кошика після інвалідації через Pub/Sub
│   ├── Models/
│   │   └── BasketModels.cs                   # Моделі ShoppingCart, ShoppingCartItem, події зміни цін
│   └── Services/
│       ├── BasketStore.cs                    # Імітація бази даних кошиків (Backing Database)
│       └── PricingUpdateSubscriber.cs        # Фоновий IHostedService: слухає Redis Pub/Sub канал 'pricing-channel'
│
└── Pricing.Api/                              # Мікросервіс управління цінами (Джерело подій)
    ├── Program.cs                            # Налаштування DI, Controllers, Swagger, Redis Pub/Sub
    ├── Pricing.Api.csproj                    # StackExchange.Redis, Swagger, ServiceDefaults, лінк на Docs
    ├── Controllers/
    │   └── PricingController.cs              # Контролер зміни цін: публікує сигнали у 'pricing-channel'
    └── Models/
        └── PricingModels.cs                  # DTO запитів зміни ціни та промокодів
```

---

## 🧩 Взаємодія класів та контролерів (Dependency Graph)

```mermaid
classDiagram
    class AppHost {
        +AddRedis("cache")
        +WithRedisInsight()
        +AddProject<Basket_Api>()
        +AddProject<Pricing_Api>()
    }

    class Step1_InMemoryCacheController {
        -IMemoryCache _memoryCache
        -BasketStore _store
        +GetBasket(userName) Task~IActionResult~
        +UpdateBasket(cart) Task~IActionResult~
        +InvalidateCache(userName) IActionResult
    }

    class Step2_DistributedRedisCacheController {
        -IDistributedCache _distributedCache
        -BasketStore _store
        +GetBasket(userName) Task~IActionResult~
        +UpdateBasket(cart) Task~IActionResult~
        +InvalidateCache(userName) Task~IActionResult~
    }

    class Step3_HybridCacheController {
        -HybridCache _hybridCache
        -BasketStore _store
        +GetBasket(userName) Task~IActionResult~
        +UpdateBasket(cart) Task~IActionResult~
        +ClearByTag() Task~IActionResult~
    }

    class Step4_CrossServiceCacheSyncController {
        -HybridCache _hybridCache
        -BasketStore _store
        +GetBasket(userName) Task~IActionResult~
        +ExplainSyncFlow() IActionResult
    }

    class PricingController {
        -IConnectionMultiplexer _redis
        +GetPricingList() IActionResult
        +UpdatePrice(request) Task~IActionResult~
        +ApplyDiscount(request) Task~IActionResult~
    }

    class PricingUpdateSubscriber {
        -IConnectionMultiplexer _redis
        -BasketStore _store
        -HybridCache _cache
        #ExecuteAsync(cancellationToken) Task
    }

    class BasketStore {
        -ConcurrentDictionary _baskets
        +GetAsync(userName) Task~ShoppingCart~
        +SaveAsync(cart) Task
        +UpdateProductPrice(productId, newPrice)
        +ApplyGlobalDiscount(percent)
    }

    AppHost ..> Step1_InMemoryCacheController : Запускає Basket.Api
    AppHost ..> PricingController : Запускає Pricing.Api

    Step1_InMemoryCacheController --> BasketStore : Читає БД при Cache Miss
    Step2_DistributedRedisCacheController --> BasketStore : Читає БД при Cache Miss
    Step3_HybridCacheController --> BasketStore : Читає БД при Cache Miss

    PricingController --> PricingUpdateSubscriber : Транслює подію через Redis Pub/Sub канал 'pricing-channel'
    PricingUpdateSubscriber --> BasketStore : Оновлює персистентний стан
    PricingUpdateSubscriber --> Step3_HybridCacheController : Викликає RemoveByTagAsync("baskets")
```

---

## 🔄 Покроковий рух даних: 4 навчальні етапи

---

### 📘 КРОК 1: Локальне кешування в пам'яті (`IMemoryCache`)

> **Життєва аналогія:** Офіціант записує ціну страви на клаптику паперу і кладе собі в кишеню. Коли клієнт запитує ціну — офіціант дістає її за частку секунди.

```mermaid
sequenceDiagram
    autonumber
    actor Client as 👤 Клієнт
    participant API as Basket.Api (Step1_InMemoryCacheController)
    participant RAM as IMemoryCache (RAM процесу)
    participant DB as BasketStore (База даних)

    Client->>API: GET /api/step1-in-memory/john
    API->>RAM: 1. TryGetValue("inmemory:basket:john")
    alt Дані є в пам'яті (Cache HIT)
        RAM-->>API: Об'єкт ShoppingCart (~0.005 ms)
        API-->>Client: 200 OK (DataSource: In-Memory RAM)
    else Даних немає (Cache MISS)
        API->>DB: 2. store.GetAsync("john")
        DB-->>API: Дані кошика з бази
        API->>RAM: 3. Set(key, cart, AbsoluteExpiration: 5 min)
        API-->>Client: 200 OK (DataSource: Database -> Saved to RAM)
    end
```

* **Чому це найшвидше:** Немає мережевих запитів, немає серіалізації/десеріалізації JSON.
* **Чому цього замало в мікросервісах:** Якщо ми піднімемо **2 або 3 репліки** `Basket.Api`, кожен інстанс матиме свою ізольовану пам'ять. Користувач додасть товар на репліці 1, а наступний запит балансувальник направить на репліку 2 — і там кошик буде порожнім! Крім того, при перезапуску або деплої контейнера пам'ять стирається.

---

### 📘 КРОК 2: Чистий розподілений кеш (`IDistributedCache` + Redis)

> **Життєва аналогія:** Щоб усі офіціанти бачили однакове замовлення, на кухні повісили спільне електронне табло (сервер Redis). Тепер будь-який офіціант бачить актуальний стан.

```mermaid
sequenceDiagram
    autonumber
    actor Client as 👤 Клієнт
    participant API as Basket.Api (Step2_DistributedRedisCacheController)
    participant Redis as Redis Container (В окремому Docker-контейнері)
    participant DB as BasketStore (База даних)

    Client->>API: GET /api/step2-distributed-redis/john
    API->>Redis: 1. GetStringAsync("distributed:basket:john") через TCP
    alt Запис знайдено в Redis (Cache HIT)
        Redis-->>API: JSON-рядок (~1.5 ms)
        API->>API: JsonSerializer.Deserialize<ShoppingCart>(json)
        API-->>Client: 200 OK (DataSource: Redis Distributed Cache)
    else Запису немає (Cache MISS)
        API->>DB: 2. store.GetAsync("john")
        DB-->>API: Об'єкт кошика з бази
        API->>API: JsonSerializer.Serialize(cart)
        API->>Redis: 3. SetStringAsync(key, json, TTL = 10 min)
        API-->>Client: 200 OK (DataSource: Database -> Saved to Redis)
    end
```

* **Плюси:** Усі 10 реплік мікросервісу бачать один і той самий стан. При падінні сервісу дані в Redis зберігаються.
* **Ціна компромісу:**
  1. З'являється затримка мережі (TCP Network Latency ~1–3 мс замість 0.005 мс у RAM).
  2. Процесор витрачає час на серіалізацію та десеріалізацію JSON при кожному запиті.

---

### 📘 КРОК 3: Сучасне гібридне кешування (.NET `HybridCache`)

> **Життєва аналогія:** Офіціант має і клаптик у кишені (L1), і спільне табло на кухні (L2). Він зазирає спочатку в кишеню (0.01 мс). Якщо там немає — дивиться на спільне табло (1 мс) і ПЕРЕПИСУЄ собі в кишеню на наступні 2 хвилини!

```mermaid
sequenceDiagram
    autonumber
    actor Client as 👤 Клієнт
    participant API as Basket.Api (Step3_HybridCacheController)
    participant L1 as HybridCache L1 (Локальна RAM)
    participant L2 as Redis L2 (Розподілений кеш)
    participant DB as BasketStore (База даних)

    Client->>API: GET /api/step3-hybrid/john
    API->>L1: 1. Перевірка L1 RAM
    alt Є в L1 (L1 Cache HIT)
        L1-->>API: Об'єкт без серіалізації (~0.01 ms)
    else Немає в L1 (L1 MISS)
        API->>L2: 2. Запит у Redis (L2)
        alt Є в L2 (L2 Cache HIT)
            L2-->>API: Бінарні дані з Redis
            API->>L1: Зберігає копію в L1 на 2 хвилини
        else Немає в L2 (Повний Cache MISS)
            Note over API,DB: 🛡️ Stampede Protection: пускає до БД лише 1 потік!
            API->>DB: 3. store.GetAsync("john")
            DB-->>API: Свіжі дані з бази
            API->>L2: Зберігає в Redis на 10 хвилин (з тегом "baskets")
            API->>L1: Зберігає в L1 на 2 хвилини
        end
    end
    API-->>Client: 200 OK (DataSource: HybridCache)
```

#### 🛡️ Що таке Cache Stampede (Ефект стада бізонів)?
Уявіть популярний товар під час акції. Його час життя в кеші закінчився о 12:00:00.
В ту саму мілісекунду надходить **2000 запитів від покупців**.
* **У звичайному кеші (Крок 2):** Усі 2000 запитів побачать "Cache Miss" і одночасно вдарять по базі даних. База перевантажується або падає!
* **У `HybridCache`:** Метод `GetOrCreateAsync` атомарно блокує паралельні виклики фабрики. До бази даних піде **рівно 1 запит**. Інші 1999 запитів зачекають частку мілісекунди і отримають готовий результат із пам'яті.

---

### 📘 КРОК 4: Синхронізація між мікросервісами через Redis Pub/Sub

> **Проблема:** Бухгалтерія (`Pricing.Api`) змінила ціну на товар зі 199 до 149 грн. Але кошик покупця (`Basket.Api`) вже закешував стару ціну 199 грн. Як змусити кошик дізнатися, що треба скинути свій кеш, якщо сервіси розділені?

#### 📻 Аналогія з радіостанцією:
* `Pricing.Api` — це **радіоведучий (Publisher)**. Він кричить у мікрофон на хвилі `'pricing-channel'`: *"Ціна товару X змінилася!"*.
* `PricingUpdateSubscriber` у `Basket.Api` — це **радіоприймач (Subscriber)**. Він постійно слухає хвилю `'pricing-channel'`.

```mermaid
sequenceDiagram
    autonumber
    actor Admin as 👨‍💼 Менеджер цін
    participant Pricing as Pricing.Api (PricingController)
    participant RedisPubSub as Redis Pub/Sub ('pricing-channel')
    participant Sub as Basket.Api (PricingUpdateSubscriber)
    participant Cache as HybridCache (L1 + L2)
    actor Buyer as 🛒 Покупець
    participant Step4 as Basket.Api (Step4_CrossServiceCacheSyncController)

    Admin->>Pricing: POST /api/pricing/update-price (ProductId, NewPrice: 149.99)
    Pricing->>Pricing: Оновлює свою базу цін
    Pricing->>RedisPubSub: sub.PublishAsync("pricing-channel", JSON)
    Pricing-->>Admin: 200 OK (Ціну змінено, сигнал відправлено в ефір)

    Note over RedisPubSub,Sub: Радіосигнал миттєво надходить до всіх запущених реплік Basket.Api!
    RedisPubSub-->>Sub: Отримано PriceUpdatedEvent
    Sub->>Cache: RemoveByTagAsync("baskets")
    Note over Cache: Миттєве очищення L1 у всіх репліках та ключів у Redis!

    Buyer->>Step4: GET /api/step4-cross-service-sync/basket/john
    Step4->>Cache: Запит кошика
    Note over Cache: Cache Miss! (Кеш щойно скинуто сигналом із радіо)
    Cache->>Step4: Завантажує дані зі сховища з новою ціною 149.99 грн
    Step4-->>Buyer: 200 OK (Актуальна сума зі знижкою!)
```

#### ❓ Чому саме Redis Pub/Sub, а не звичайний HTTP POST?
Якщо в продакшені `Basket.Api` запущений у **5 репліках** за балансувальником навантаження (Nginx, Kubernetes Ingress):
* Звичайний HTTP-запит від `Pricing.Api` потрапить лише на **одну випадкову репліку**. Інші 4 репліки залишаться зі старим кешем і продаватимуть товар за неправильною ціною!
* Через `Redis Pub/Sub` сигнал отримують **ВСІ 5 реплік одночасно**.

---

## 💾 Де та що зберігається (Storage, Keys & Lifetime)

| Етап | Рівень сховища | Носій / Технологія | Формат ключа / Канал | Час життя (TTL) | Коли очищується |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Крок 1** | In-Memory Cache | RAM процесу Kestrel | `inmemory:basket:{userName}` | 5 хв (Sliding: 1 хв) | При перезапуску процесу або `DELETE` |
| **Крок 2** | Розподілений кеш | Redis Docker Container | `distributed:basket:{userName}` | 10 хв (Sliding: 2 хв) | За таймером TTL або `DELETE` |
| **Крок 3** | Дворівневий кеш | L1 (RAM) + L2 (Redis) | `hybrid:basket:{userName}` | L1: 2 хв, L2: 10 хв | За тегом `"baskets"` або за TTL |
| **Крок 4** | Радіоканал зв'язку | Redis In-Memory Bus | Канал: `pricing-channel` | Миттєвий (без збереження) | Доставляється всім підписникам наживо |

---

## 🧪 Як протестувати всі 4 кроки власноруч у Swagger

1. **Запустіть оркестратор AppHost:**
   ```powershell
   dotnet run --project 02-Centralized-Caching-Redis/Aspire.AppHost/Aspire.AppHost.csproj
   ```
2. **Відкрийте Swagger для `Basket.Api`:**
   - **Крок 1 (In-Memory):**
     - Викличте `GET /api/step1-in-memory/john` двічі. Перший запит покаже `Database (~20ms)`, другий — `In-Memory RAM (~0.05ms)`.
   - **Крок 2 (Redis):**
     - Викличте `GET /api/step2-distributed-redis/john`. Дані підуть у Docker-контейнер Redis. Відкрийте панель **RedisInsight** і подивіться на реальний збережений JSON-рядок.
   - **Крок 3 (HybridCache):**
     - Викличте `GET /api/step3-hybrid/john`. Спробуйте виконати 10 паралельних запитів: жодного перевантаження бази не буде.
   - **Крок 4 (Міжсервісна синхронізація):**
     - Перевірте суму кошика: `GET /api/step4-cross-service-sync/basket/john` (Сума: 359.97 грн).
     - Перейдіть у Swagger `Pricing.Api` і викличте `POST /api/pricing/update-price` для товару `11111111-1111-1111-1111-111111111111` з новою ціною `99.99`.
     - Поверніться у `Basket.Api` і знову викличте `GET /api/step4-cross-service-sync/basket/john` — сума кошика миттєво змінилася на меншу!
