# 08. Security, YARP API Gateway & Keycloak (.NET 10 & Aspire)

> **Виробничий модуль з побудови захищеного периметра мікросервісів:**  
> Шлюз на базі **YARP (Yet Another Reverse Proxy)**, контейнер **Keycloak (OAuth2 / OIDC)** у .NET Aspire, обмеження трафіку **Rate Limiting** та патерн **Token & Claim Propagation**.

---

## 🛠️ Стек технологій
- **Платформа:** .NET 10 (net10.0)
- **Оркестрація:** .NET Aspire 9.5
- **Зворотний проксі:** YARP 2.3
- **Сервер ідентичності:** Keycloak 26.0 (Docker Container)
- **Безпека:** JWT Bearer, Token Propagation, RBAC
- **Документація:** Swagger UI / OpenAPI

---

## 📂 Структура проєкту

- **`Aspire.AppHost`** — оркестратор Aspire, що запускає Keycloak, Protected.Api та YarpGateway (Порт дашборду: `17008`).
- **`Aspire.ServiceDefaults`** — спільні налаштування OpenTelemetry та Health Checks.
- **`YarpGateway`** — публічний фасад:
  - Маршрутизація трафіку за правилами YARP.
  - Rate Limiting (Fixed Window).
  - Підстановка заголовків ідентичності для внутрішнього контуру.
- **`Protected.Api`** — захищений домен:
  - `Step2_PublicEndpointsController` — публічні дані каталогу.
  - `Step3_TokenPropagationController` — читання прокинутих клеймів.
  - `Step4_RoleBasedAccessController` — розмежування доступу User vs Admin.

Повний опис потоків даних дивіться у [STRUCTURE-AND-DATA-FLOW.md](STRUCTURE-AND-DATA-FLOW.md).

---

## 🚀 Швидкий запуск
```bash
dotnet run --project Aspire.AppHost/Aspire.AppHost.csproj
```
Відкрийте веб-панель Aspire за адресою: **https://localhost:17008**
