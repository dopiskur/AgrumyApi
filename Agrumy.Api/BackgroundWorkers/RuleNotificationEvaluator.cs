using api.Dal.Interface;
using api.Devices;
using api.Models;
using api.Notifications;
using api.Utils;

namespace api.BackgroundWorkers
{
    /// Evaluates every Notification-action rule (roadmap #212) against each zone it reaches (Zone>Unit>Global
    /// precedence resolved per zone via RuleHierarchyResolver.ResolveNotificationRules, same "more specific
    /// wins" semantics as Relay rules), dispatching one notification per false->true transition - a Relay
    /// rule's OR-across-rules/AND-OR-within-a-rule fold happens on-device, this is the server-side
    /// equivalent for the action type firmware has no way to perform itself.
    public sealed class RuleNotificationEvaluator(
        ITenantRepository tenantRepo, IDeviceUnitRepository unitRepo, IUserRepository userRepo, INotificationDispatcher dispatcher)
    {
        private sealed record EvalItem(DeviceUnitZoneRule Rule, int ZoneId, int TenantId, bool WasTrue, double? MetricReading, int UtcOffsetSeconds);

        public async Task RunOnceAsync(CancellationToken ct = default)
        {
            foreach (Tenant tenant in await tenantRepo.TenantsGetAllAsync())
            {
                ct.ThrowIfCancellationRequested();
                if (tenant.IDTenant is not int tenantId)
                {
                    continue;
                }

                IList<DeviceUnitZoneRule> notificationRules = await unitRepo.RulesGetNotificationRulesForTenantAsync(tenantId);
                if (notificationRules.Count == 0)
                {
                    continue;
                }

                await EvaluateTenantAsync(tenant, tenantId, notificationRules, ct);
            }
        }

        private async Task EvaluateTenantAsync(Tenant tenant, int tenantId, IList<DeviceUnitZoneRule> notificationRules, CancellationToken ct)
        {
            int utcOffsetSeconds = TimeZoneHelper.GetUtcOffsetSeconds(DateTime.UtcNow, tenant.ScheduleTimeZone);
            DateTime utcNow = DateTime.UtcNow;

            var items = new List<EvalItem>();
            foreach (DeviceUnit unit in await unitRepo.DeviceUnitsGetAsync(tenantId))
            {
                if (unit.IDDeviceUnit is not int unitId)
                {
                    continue;
                }
                var unitScoped = notificationRules.Where(r => r.DeviceUnitID == unitId).ToList();
                var globalScoped = notificationRules.Where(r => r.DeviceUnitID == null && r.DeviceUnitZoneID == null).ToList();

                foreach (DeviceUnitZone zone in await unitRepo.DeviceUnitZonesGetAsync(unitId))
                {
                    if (zone.IDDeviceUnitZone is not int zoneId)
                    {
                        continue;
                    }
                    var zoneScoped = notificationRules.Where(r => r.DeviceUnitZoneID == zoneId).ToList();
                    IList<DeviceUnitZoneRule> effective = RuleHierarchyResolver.ResolveNotificationRules(zoneScoped, unitScoped, globalScoped);
                    if (effective.Count == 0)
                    {
                        continue;
                    }

                    DeviceUnitZoneDashboard? dashboard = await unitRepo.DeviceUnitZoneDashboardGetAsync(zoneId);
                    foreach (DeviceUnitZoneRule rule in effective)
                    {
                        if (rule.IDDeviceUnitZoneRule is not int ruleId)
                        {
                            continue;
                        }
                        bool wasTrue = await unitRepo.RuleNotificationWasTrueGetAsync(ruleId, zoneId);
                        double? reading = rule.SensorMetric is SensorMetric metric && dashboard != null ? ReadMetric(dashboard.Averages, metric) : null;
                        items.Add(new EvalItem(rule, zoneId, tenantId, wasTrue, reading, utcOffsetSeconds));
                    }
                }
            }

            if (items.Count == 0)
            {
                return;
            }

            // Fixed-point resolution for RuleTriggered chaining (roadmap #212): "referenced rule fired" means
            // "fired in ANY zone it applies to, this tick" - a deliberate simplification, not per-zone dependency
            // tracking. firedThisTick only grows, so repeating settles within a bounded number of rounds; a
            // dependency that never resolves (missing/circular reference) just evaluates false for that
            // condition, same as any other unmet condition.
            var firedThisTick = new HashSet<int>();
            var results = new Dictionary<EvalItem, bool>();
            for (int round = 0; round < 10; round++)
            {
                ct.ThrowIfCancellationRequested();
                int before = firedThisTick.Count;
                foreach (EvalItem item in items)
                {
                    bool result = RuleConditionEvaluator.EvaluateRule(item.Rule, item.WasTrue, item.MetricReading, utcNow, item.UtcOffsetSeconds, firedThisTick.Contains);
                    results[item] = result;
                    if (result)
                    {
                        firedThisTick.Add(item.Rule.IDDeviceUnitZoneRule!.Value);
                    }
                }
                if (firedThisTick.Count == before)
                {
                    break; // stable - no new rule fired this round, further rounds would repeat the same result
                }
            }

            foreach (EvalItem item in items)
            {
                await FinalizeAsync(item, results[item], ct);
            }
        }

        private async Task FinalizeAsync(EvalItem item, bool result, CancellationToken ct)
        {
            int ruleId = item.Rule.IDDeviceUnitZoneRule!.Value;
            if (result == item.WasTrue)
            {
                return; // no transition - dedup, nothing to persist or notify
            }
            await unitRepo.RuleNotificationWasTrueSetAsync(ruleId, item.ZoneId, result, result ? DateTime.UtcNow : null);
            if (!result)
            {
                return; // true->false recovery clears the latch silently, no notification
            }

            var admins = await userRepo.TenantAdminsGetAsync(item.TenantId);
            foreach (User admin in admins)
            {
                if (string.IsNullOrWhiteSpace(admin.Email))
                {
                    continue;
                }
                var notification = new Notification(
                    Subject: Placeholder(item.Rule.NotificationSubject) ?? "Agrumy alert",
                    Body: Placeholder(item.Rule.NotificationBody) ?? string.Empty,
                    Recipient: new NotificationRecipient(Email: admin.Email),
                    Severity: NotificationSeverity.Warning);
                await dispatcher.DispatchAsync(notification, ct);
            }

            string? Placeholder(string? template) => template?
                .Replace("{value}", item.MetricReading?.ToString("0.##") ?? "n/a")
                .Replace("{metric}", item.Rule.SensorMetric?.ToString() ?? "");
        }

        private static double? ReadMetric(SensorAverages averages, SensorMetric metric) => metric switch
        {
            SensorMetric.Temperature => averages.Temperature,
            SensorMetric.SoilTemperature => averages.SoilTemperature,
            SensorMetric.Humidity => averages.Humidity,
            SensorMetric.Vpd => averages.Vpd,
            SensorMetric.Moisture => averages.Moisture,
            SensorMetric.Light => averages.Light,
            SensorMetric.Co2 => averages.Co2,
            SensorMetric.Tvoc => averages.Tvoc,
            SensorMetric.Barometer => averages.Barometer,
            SensorMetric.LiquidPH => averages.LiquidPH,
            SensorMetric.RainLevel => averages.RainLevel,
            SensorMetric.WaterLevel => averages.WaterLevel,
            SensorMetric.Wind => averages.Wind,
            _ => null,
        };
    }
}
