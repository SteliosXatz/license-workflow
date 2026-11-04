using System.Collections.Concurrent;
using System.Reflection;
using MassTransit;

namespace DrivingLicenceWorkflow.Worker.Logging;

/// <summary>Writes one structured log line per message consumed, published or sent. Message bodies are never logged.</summary>
public sealed class WorkflowMessageObserver : IConsumeObserver, IPublishObserver, ISendObserver
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> ApplicationIdProperties = new();

    private readonly ILogger<WorkflowMessageObserver> _logger;

    public WorkflowMessageObserver(ILogger<WorkflowMessageObserver> logger)
    {
        _logger = logger;
    }

    public Task PreConsume<T>(ConsumeContext<T> context) where T : class
    {
        _logger.LogInformation(
            "Consuming {MessageType} {MessageId} for application {ApplicationId}",
            typeof(T).Name,
            context.MessageId,
            GetApplicationId(context.Message));
        return Task.CompletedTask;
    }

    public Task PostConsume<T>(ConsumeContext<T> context) where T : class
    {
        _logger.LogInformation(
            "Consumed {MessageType} {MessageId} for application {ApplicationId} in {ElapsedMs} ms",
            typeof(T).Name,
            context.MessageId,
            GetApplicationId(context.Message),
            (long)context.ReceiveContext.ElapsedTime.TotalMilliseconds);
        return Task.CompletedTask;
    }

    public Task ConsumeFault<T>(ConsumeContext<T> context, Exception exception) where T : class
    {
        _logger.LogError(
            exception,
            "Failed consuming {MessageType} {MessageId} for application {ApplicationId}",
            typeof(T).Name,
            context.MessageId,
            GetApplicationId(context.Message));
        return Task.CompletedTask;
    }

    public Task PrePublish<T>(PublishContext<T> context) where T : class => Task.CompletedTask;

    public Task PostPublish<T>(PublishContext<T> context) where T : class
    {
        _logger.LogInformation(
            "Published {MessageType} {MessageId} for application {ApplicationId}",
            typeof(T).Name,
            context.MessageId,
            GetApplicationId(context.Message));
        return Task.CompletedTask;
    }

    public Task PublishFault<T>(PublishContext<T> context, Exception exception) where T : class
    {
        _logger.LogError(
            exception,
            "Failed publishing {MessageType} {MessageId} for application {ApplicationId}",
            typeof(T).Name,
            context.MessageId,
            GetApplicationId(context.Message));
        return Task.CompletedTask;
    }

    public Task PreSend<T>(SendContext<T> context) where T : class => Task.CompletedTask;

    public Task PostSend<T>(SendContext<T> context) where T : class
    {
        _logger.LogInformation(
            "Sent {MessageType} {MessageId} to {DestinationAddress} for application {ApplicationId}",
            typeof(T).Name,
            context.MessageId,
            context.DestinationAddress,
            GetApplicationId(context.Message));
        return Task.CompletedTask;
    }

    public Task SendFault<T>(SendContext<T> context, Exception exception) where T : class
    {
        _logger.LogError(
            exception,
            "Failed sending {MessageType} {MessageId} to {DestinationAddress} for application {ApplicationId}",
            typeof(T).Name,
            context.MessageId,
            context.DestinationAddress,
            GetApplicationId(context.Message));
        return Task.CompletedTask;
    }

    private static Guid? GetApplicationId(object message)
    {
        var property = ApplicationIdProperties.GetOrAdd(
            message.GetType(),
            type => type.GetProperty("ApplicationId", typeof(Guid)));
        return (Guid?)property?.GetValue(message);
    }
}
