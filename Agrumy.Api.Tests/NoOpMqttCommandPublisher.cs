using api.Commands;
using api.Models;

namespace Agrumy.Api.Tests;

/// Shared no-op IMqttCommandPublisher for tests that construct CommandQueueService directly and don't care about the MQTT side-channel - avoids every call site needing its own mock/setup.
public sealed class NoOpMqttCommandPublisher : IMqttCommandPublisher
{
    public Task PublishAsync(Device device, PendingCommand command, CancellationToken ct = default) => Task.CompletedTask;
}
