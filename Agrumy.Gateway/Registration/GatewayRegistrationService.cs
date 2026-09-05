using api.Gateway;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace api.Gateway.Registration
{
    /// Loads a persisted registration if one exists; otherwise registers once via the SAME PIN flow AgrumyFirmware uses, then persists the result - same boot sequence, minus the captive-portal UI since config comes from appsettings.json instead.
    public sealed partial class GatewayRegistrationService(
        AgrumyServiceClient client, GatewayRegistrationStore store, ILogger<GatewayRegistrationService> logger)
        : IHostedService
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Gateway already registered as device {IdDevice}.")]
        private static partial void LogAlreadyRegistered(ILogger logger, int? idDevice);

        [LoggerMessage(Level = LogLevel.Warning, Message = "No saved registration found - registering with AgrumyService now.")]
        private static partial void LogRegistering(ILogger logger);

        [LoggerMessage(Level = LogLevel.Information, Message = "Registered as device {IdDevice}.")]
        private static partial void LogRegistered(ILogger logger, int? idDevice);

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            store.Load();
            if (store.Current.IsComplete)
            {
                LogAlreadyRegistered(logger, store.Current.IdDevice);
                return;
            }

            LogRegistering(logger);
            // No retry loop - a transient failure here leaves the gateway unregistered until the next restart, matching how AgrumyFirmware's captive portal needs a human to notice and re-trigger it.
            var state = await client.RegisterAsync(cancellationToken);
            store.Save(state);
            LogRegistered(logger, state.IdDevice);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
