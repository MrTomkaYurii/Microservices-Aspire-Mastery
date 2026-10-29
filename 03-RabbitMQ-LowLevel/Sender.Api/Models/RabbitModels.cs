namespace Sender.Api.Models;

public record DirectMessageRequest(Guid MessageId, string RoutingKey, string Payload);
public record FanoutMessageRequest(string BroadcastText);
public record TopicMessageRequest(string TopicRoutingKey, string Data);
public record DeadLetterRequest(string Payload, int TtlMilliseconds = 5000);
