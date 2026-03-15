namespace EasyRabbitMQ.Runtime;

public enum MessageProcessingResult
{
    NoMessage,
    Processed,
    DuplicateIgnored,
    MovedToRetry,
    MovedToDeadLetter,
    CircuitOpen
}
