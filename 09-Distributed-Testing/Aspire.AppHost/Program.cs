var builder = DistributedApplication.CreateBuilder(args);

// =========================================================================================
// 🎯 МОДУЛЬ 09: ОРКЕСТРАЦІЯ РОЗПОДІЛЕНОГО ТЕСТУВАННЯ (ASPIRE.HOSTING.TESTING)
// =========================================================================================

// Тестовий цільовий сервіс Sample.Api
builder.AddProject<Projects.Sample_Api>("sample-api");

builder.Build().Run();
