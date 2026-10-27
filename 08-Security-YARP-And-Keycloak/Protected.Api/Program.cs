var builder = WebApplication.CreateBuilder(args);

// =========================================================================================
// 🎯 МОДУЛЬ 08: PROTECTED.API - ВНУТРІШНІЙ ЗАХИЩЕНИЙ КОНТУР
// =========================================================================================

builder.AddServiceDefaults();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Protected.Api - Internal Secure Domain (.NET 10)",
        Version = "v1",
        Description = "Внутрішній захищений мікросервіс із демонстрацією Public Endpoints, Token Propagation та RBAC."
    });
});

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Protected.Api v1");
        c.RoutePrefix = string.Empty;
    });
}

app.UseAuthorization();
app.MapControllers();

app.Run();
