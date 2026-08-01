using MassTransit;
using MesBrConsumer;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

        Console.Write("Enter queue name (default: 'test-queue'): ");
        var queue = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(queue))
        {
            queue = "test-queue";
        }

services.AddMassTransit(x =>
{
    x.AddConsumer<ItemCreatedMessageConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("localhost", "/", RabMqHostConf =>
        {
            RabMqHostConf.Username("guest");
            RabMqHostConf.Password("guest");
        });

        //cfg.ConfigureEndpoints(context);

            cfg.ReceiveEndpoint(queue, e =>
            {
                e.Consumer<ItemCreatedMessageConsumer>();
            });
    });
});

var serviceProvider = services.BuildServiceProvider();
var bus = serviceProvider.GetRequiredService<IBusControl>();
await bus.StartAsync();

Console.WriteLine("Waiting for messages...");
Console.ReadLine();