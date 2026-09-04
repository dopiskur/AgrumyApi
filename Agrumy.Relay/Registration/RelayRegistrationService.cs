using api.Relay;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace api.Relay.Registration
{
    /// <summary>Loads a persisted registration if one exists; otherwise registers once via the
    /// SAME PIN flow AgrumyFirmware uses (POST /api/Device/Register), then persists the result -
    /// mirrors AgrumyFirmware's own "load deviceRegistration.json, else initializeDevice()" boot
    /// sequence (see AgrumyFirmware's DeviceController), just without the captive-portal UI since a
    /// relay's email/PIN/MacAddress come from its own appsettings.json instead of a phone screen.</summary>
    public sealed partial class RelayRegistrationService(
        AgrumyServiceClient client, RelayRegistrationStore store, ILogger<RelayRegistrationService> logger)
        : IHostedService
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Relay already registered as device {IdDevice}.")]
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
            // Register's rate limiting (device-auth policy) is generous enough for a one-shot
            // startup call; a transient failure here just means the relay stays unregistered and
            // every Batch call fails loudly (RelayRegistrationState.IsComplete false) until the
            // next restart - no retry loop, matching how AgrumyFirmware's own captive portal
            // requires a human to notice and re-trigger it rather than retrying blindly forever.
            var state = await client.RegisterAsync(cancellationToken);
            store.Save(state);
            LogRegistered(logger, state.IdDevice);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
