using MassTransit;

var builder = WebApplication.CreateBuilder(args);


// Add services to the container.
builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq();
});

builder.Services.AddMassTransitHostedService();

// Add services to the container.
builder.Services.AddControllers();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

////////////
/// PIPELINE
////////////

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
