using System.Text.Json;
using api.Dal;
using api.Dal.Entities;

namespace Agrumy.Api.Tests;

/// <summary>
/// Unit tests for the logic that moved out of stored procedures into C# during the EF Core
/// rewrite (roadmap #42): the sensor-report shaping port and database-error classification.
/// No database - these exercise pure functions only.
/// </summary>
public class SensorReportShaperTests
{
    private static SensorDataRow Row(string dateCreated, int? co2 = 400, double? temp = 20.0) => new()
    {
        DeviceID = 1,
        TenantID = 0,
        DateCreated = DateTime.Parse(dateCreated),
        Co2 = co2,
        Temperature = temp,
    };

    [Fact]
    public void Build_NoRows_ReturnsEmptyString()
    {
        Assert.Equal("", SensorReportShaper.Build(Array.Empty<SensorDataRow>(), 0));
    }

    [Fact]
    public void Build_MinuteMode_GroupsByMinute_AndKeepsLatestRowPerBucket()
    {
        var rows = new[]
        {
            Row("2026-08-29 09:50:10", temp: 1),
            Row("2026-08-29 09:50:40", temp: 2),   // same minute, later -> this one wins
            Row("2026-08-29 09:51:05", temp: 3),
        };

        var json = SensorReportShaper.Build(rows, 0);
        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement.GetProperty("sensorData");

        Assert.Equal(2, arr.GetArrayLength());
        Assert.Equal(2, arr[0].GetProperty("temperature").GetDouble());   // 09:50 bucket -> latest
        Assert.Equal(3, arr[1].GetProperty("temperature").GetDouble());   // 09:51 bucket
    }

    [Fact]
    public void Build_DayMode_GroupsByHour()
    {
        var rows = new[]
        {
            Row("2026-08-29 09:05:00"),
            Row("2026-08-29 09:55:00"),
            Row("2026-08-29 10:01:00"),
        };

        var json = SensorReportShaper.Build(rows, 1);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(2, doc.RootElement.GetProperty("sensorData").GetArrayLength());
    }

    [Fact]
    public void Build_MonthAndYearModes_GroupByDay()
    {
        var rows = new[]
        {
            Row("2026-08-29 01:00:00"),
            Row("2026-08-29 23:00:00"),
            Row("2026-08-30 12:00:00"),
        };

        Assert.Equal(2, JsonDocument.Parse(SensorReportShaper.Build(rows, 2)).RootElement.GetProperty("sensorData").GetArrayLength());
        Assert.Equal(2, JsonDocument.Parse(SensorReportShaper.Build(rows, 3)).RootElement.GetProperty("sensorData").GetArrayLength());
    }

    [Fact]
    public void Build_Record_HasProcKeysAndDateFormat()
    {
        var json = SensorReportShaper.Build(new[] { Row("2026-08-29 09:50:00") }, 0);
        using var doc = JsonDocument.Parse(json);
        var rec = doc.RootElement.GetProperty("sensorData")[0];

        foreach (var key in new[] { "battery", "temperature", "soilTemperature", "humidity", "moisture",
                                    "light", "co2", "tvoc", "barometer", "liquidPH", "rainLevel",
                                    "waterLevel", "wind", "dateCreated" })
        {
            Assert.True(rec.TryGetProperty(key, out _), $"missing key: {key}");
        }

        Assert.Equal("2026-08-29 09:50:00", rec.GetProperty("dateCreated").GetString());
    }
}

public class ClassifyExceptionTests
{
    private readonly EfRepository _repo = new();

    [Fact]
    public void PlainException_MentioningMissingTable_IsSchemaMissing()
    {
        Assert.Equal(DbFailureKind.SchemaMissing,
            _repo.ClassifyException(new Exception("Table 'agrumy.device' doesn't exist")));
    }

    [Fact]
    public void UnknownTableWording_IsSchemaMissing()
    {
        Assert.Equal(DbFailureKind.SchemaMissing,
            _repo.ClassifyException(new Exception("Unknown table 'agrumy.user'")));
    }

    [Fact]
    public void DbUpdateException_WrappingMissingTable_IsSchemaMissing()
    {
        var ex = new Microsoft.EntityFrameworkCore.DbUpdateException(
            "An error occurred while saving the entity changes.",
            new Exception("Table 'agrumy.sensorData' doesn't exist"));

        Assert.Equal(DbFailureKind.SchemaMissing, _repo.ClassifyException(ex));
    }

    [Fact]
    public void UnrelatedError_IsConnectionFailure()
    {
        Assert.Equal(DbFailureKind.ConnectionFailure,
            _repo.ClassifyException(new TimeoutException("connect timeout")));
    }
}

public class DbErrorResponseMentionsTests
{
    [Fact]
    public void Mentions_WalksInnerExceptionChain()
    {
        var ex = new Exception("outer",
            new Exception("An error occurred while saving the entity changes.",
                new Exception("Duplicate entry 'a@b.com' for key 'user.email_UNIQUE'")));

        Assert.True(DbErrorResponse.Mentions(ex, "email_UNIQUE"));
        Assert.False(DbErrorResponse.Mentions(ex, "Username_UNIQUE"));
        Assert.False(DbErrorResponse.Mentions(null, "email_UNIQUE"));
    }
}
