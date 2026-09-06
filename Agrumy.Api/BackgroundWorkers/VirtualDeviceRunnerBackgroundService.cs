using api.Dal.Interface;
using api.Devices;
using api.Models;
using api.Simulation;
using Microsoft.Extensions.Options;

namespace api.BackgroundWorkers
{
    /// Roadmap #251 modality B: drives every registered virtual device through the real device-facing HTTP wire protocol once per tick - Authenticate, Config, SensorData, ControllerData - exactly as a real AgrumyFirmware device would, just generated instead of read off real hardware.
    public sealed class VirtualDeviceRunnerBackgroundService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        SimulatedSensorGenerator generator,
        ILogger<VirtualDeviceRunnerBackgroundService> logger)
        : PeriodicBackgroundService(scopeFactory, logger)
    {
        public const string HttpClientName = "virtual-device";

        // Known scaling limit: every virtual device calls Authenticate from the same loopback IP, and that endpoint is rate-limited to 20/min per IP (roadmap #117's device-auth policy) - past roughly a dozen simultaneously-running virtual devices, some ticks will start seeing 429 and simply retry next interval. Fine for the onboarding/demo use case roadmap #251 targets; would need a per-loopback-IP carve-out if virtual devices are ever run at real scale.
        protected override TimeSpan Interval => TimeSpan.FromSeconds(30);

        protected override async Task DoWorkAsync(IServiceProvider scopedProvider, CancellationToken ct)
        {
            IRepository repo = scopedProvider.GetRequiredService<IRepository>();
            HttpClient http = httpClientFactory.CreateClient(HttpClientName);
            var client = new VirtualDeviceClient(http);

            foreach (int deviceId in await repo.VirtualDeviceIdsGetAsync())
            {
                try
                {
                    await RunOneTickAsync(repo, client, deviceId);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Virtual device {DeviceId} tick failed - will retry next interval.", deviceId);
                }
            }
        }

        private async Task RunOneTickAsync(IRepository repo, VirtualDeviceClient client, int deviceId)
        {
            Device? device = await repo.DeviceGetByIdAsync(deviceId);
            if (device is null)
            {
                return; // deleted between the id list read and now - VirtualDeviceDeleteAsync already dropped the registry row too
            }

            string apiAuth = await client.AuthenticateAsync(device.ApiId!, device.ApiKey!);
            DeviceConfig? config = await client.PollConfigAsync(device.ApiId!, apiAuth, "VirtualDevice");

            SimulatedReading reading = generator.Next(deviceId);
            await client.PushSensorDataAsync(device.ApiId!, apiAuth, reading);

            if (config?.DeviceConfigController is { Rules.Count: > 0 } controller)
            {
                IList<ControllerDataStatus> current = await repo.ControllerDataGetAsync(deviceId);
                DateTime utcNow = DateTime.UtcNow;
                var entries = new List<ControllerDataPush>();
                foreach (RelayFunction function in Enum.GetValues<RelayFunction>())
                {
                    bool wasOn = current.FirstOrDefault(c => c.RelayFunction == function)?.IsOn ?? false;
                    bool isOn = SimulatedRelayEvaluator.Evaluate(function, controller.Rules, wasOn, reading, utcNow, config.UtcOffsetSeconds ?? 0);
                    entries.Add(new ControllerDataPush { RelayFunction = function, IsOn = isOn, DateCreated = utcNow });
                }
                await client.PushControllerDataAsync(device.ApiId!, apiAuth, entries);
            }
        }
    }
}
