using DrivingLicenceWorkflow.Shared.Messages;
using DrivingLicenceWorkflow.Worker.Logging;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DrivingLicenceWorkflow.Tests.Logging;

public class WorkflowMessageObserverTests
{
    [Fact]
    public async Task LogsConsumedAndPublishedMessagesWithApplicationId()
    {
        var logger = new CapturingLogger();
        await using var provider = new ServiceCollection()
            .AddSingleton<ILogger<WorkflowMessageObserver>>(logger)
            .AddConsumeObserver<WorkflowMessageObserver>()
            .AddPublishObserver<WorkflowMessageObserver>()
            .AddSendObserver<WorkflowMessageObserver>()
            .AddMassTransitTestHarness(x => x.AddConsumer<ValidateConsumer>())
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var applicationId = Guid.NewGuid();
        await harness.Bus.Publish(new ValidateApplication(applicationId));
        Assert.True(await harness.Consumed.Any<ValidateApplication>());
        Assert.True(await harness.Published.Any<ApplicationValidated>());

        var lines = logger.Messages.ToArray();
        Assert.Contains(lines, l => l.StartsWith("Consumed ValidateApplication") && l.Contains(applicationId.ToString()));
        Assert.Contains(lines, l => l.StartsWith("Published ApplicationValidated") && l.Contains(applicationId.ToString()));
    }

    private sealed class ValidateConsumer : IConsumer<ValidateApplication>
    {
        public Task Consume(ConsumeContext<ValidateApplication> context) =>
            context.Publish(new ApplicationValidated(context.Message.ApplicationId));
    }

    private sealed class CapturingLogger : ILogger<WorkflowMessageObserver>
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Enqueue(formatter(state, exception));
    }
}
