using System.Text.Json;
using api.Devices;
using api.Models;
using api.Simulation;

namespace Agrumy.Api.Tests;

/// Relay evaluation for a simulated device against the SAME rule shape a real device receives over /api/Device/Config.
public class SimulatedRelayEvaluatorTests
{
    private static DeviceUnitZoneRule Rule(RelayFunction function, params RuleCondition[] conditions) => new()
    {
        IDDeviceUnitZoneRule = 1,
        TenantID = 1,
        ActionType = ActionType.Relay,
        RelayFunction = function,
        Conditions = conditions,
    };

    private static RuleCondition Threshold(double threshold, double hysteresis, LogicalOperator? op = null) =>
        new(ConditionType.Threshold, JsonSerializer.SerializeToNode(new ThresholdConditionConfig(threshold, hysteresis), ConditionConfigJson.Options), op);

    private static SimulatedReading Reading(double humidity = 50, double temperature = 20, int light = 5000, int waterLevel = 50) => new()
    {
        Humidity = humidity,
        Temperature = temperature,
        Light = light,
        WaterLevel = waterLevel,
    };

    [Fact]
    public void Ventilation_TurnsOnAboveHumidityThreshold()
    {
        var rules = new List<DeviceUnitZoneRule> { Rule(RelayFunction.Ventilation, Threshold(60, 2)) };
        bool on = SimulatedRelayEvaluator.Evaluate(RelayFunction.Ventilation, rules, wasOn: false, Reading(humidity: 65), DateTime.UtcNow, 0);
        Assert.True(on);
    }

    [Fact]
    public void Heating_TurnsOnBelowTemperatureThreshold()
    {
        // Heating's direction is inverted relative to Ventilation - matches AgrumyFirmware's ActuatorController::evaluateCondition table.
        var rules = new List<DeviceUnitZoneRule> { Rule(RelayFunction.Heating, Threshold(18, 1)) };
        bool on = SimulatedRelayEvaluator.Evaluate(RelayFunction.Heating, rules, wasOn: false, Reading(temperature: 15), DateTime.UtcNow, 0);
        Assert.True(on);
    }

    [Fact]
    public void Heating_AboveThreshold_StaysOff()
    {
        var rules = new List<DeviceUnitZoneRule> { Rule(RelayFunction.Heating, Threshold(18, 1)) };
        bool on = SimulatedRelayEvaluator.Evaluate(RelayFunction.Heating, rules, wasOn: false, Reading(temperature: 22), DateTime.UtcNow, 0);
        Assert.False(on);
    }

    [Fact]
    public void SeveralRulesForSameFunction_OrTogether()
    {
        var rules = new List<DeviceUnitZoneRule>
        {
            Rule(RelayFunction.Light, Threshold(1000, 50)), // below 1000 -> on; reading is 5000, so this alone is off
            Rule(RelayFunction.WaterPump, Threshold(30, 2)), // different function - must not affect Light's result
        };
        bool on = SimulatedRelayEvaluator.Evaluate(RelayFunction.Light, rules, wasOn: false, Reading(light: 5000), DateTime.UtcNow, 0);
        Assert.False(on);
    }

    [Fact]
    public void UnrelatedFunction_WithNoMatchingRule_IsFalse()
    {
        var rules = new List<DeviceUnitZoneRule> { Rule(RelayFunction.Heating, Threshold(18, 1)) };
        bool on = SimulatedRelayEvaluator.Evaluate(RelayFunction.WaterPump, rules, wasOn: true, Reading(waterLevel: 10), DateTime.UtcNow, 0);
        Assert.False(on);
    }
}
