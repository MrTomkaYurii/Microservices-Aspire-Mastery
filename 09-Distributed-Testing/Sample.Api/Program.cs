var builder = WebApplication.CreateBuilder(args);

// =========================================================================================
// 🎯 МОДУЛЬ 09: SAMPLE.API - ЦІЛЬОВИЙ СЕРВІС ДЛЯ РОЗПОДІЛЕНОГО ТЕСТУВАННЯ
// =========================================================================================

builder.AddServiceDefaults();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Sample.Api - Testing Target (.NET 10)",
        Version = "v1",
        Description = "Цільовий сервіс для автоматизованих інтеграційних тестів через Aspire.Hosting.Testing."
    });
});

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Sample.Api v1");
        c.RoutePrefix = string.Empty;
    });
}

app.UseAuthorization();
app.MapControllers();

app.Run();
