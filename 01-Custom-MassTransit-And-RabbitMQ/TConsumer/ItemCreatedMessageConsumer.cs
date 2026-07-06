using MassTransit;
using MesBrContracts;

namespace MesBrConsumer;

public record ItemCreatedMessageConsumer : IConsumer<ItemCreatedMessage>
{
    public Task Consume(ConsumeContext<ItemCreatedMessage> context)
    {
        Console.WriteLine(
            $"Item '{context.Message.Name}' " +
            $"with price '{context.Message.Price}' created.");

        return Task.CompletedTask;
    }
}