using System.Text.Json;
using api;
using api.Dal;
using api.Dal.Entities;
using api.Dal.Interface;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Agrumy.Api.Tests;

/// <summary>Unit tests for the sensor-report shaping port and database-error classification. No database - pure functions only.</summary>
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

        foreach (var key in new[] { "battery", "temperature", "soilTemperature", "humidity", "vpd", "moisture",
                                    "light", "co2", "tvoc", "barometer", "liquidPH", "rainLevel",
                                    "waterLevel", "wind", "dateCreated" })
        {
            Assert.True(rec.TryGetProperty(key, out _), $"missing key: {key}");
        }

        Assert.Equal("2026-08-29 09:50:00", rec.GetProperty("dateCreated").GetString());
    }


    [Fact]
    public void Build_Vpd_NullWhenHumidityMissing()
    {
        var json = SensorReportShaper.Build(new[] { Row("2026-08-29 09:50:00", temp: 20) }, 0);
        var rec = JsonDocument.Parse(json).RootElement.GetProperty("sensorData")[0];

        Assert.Equal(JsonValueKind.Null, rec.GetProperty("vpd").ValueKind);
    }

    [Fact]
    public void Build_Vpd_ComputedWhenTemperatureAndHumidityBothPresent()
    {
        var row = Row("2026-08-29 09:50:00", temp: 20);
        row.Humidity = 60;

        var json = SensorReportShaper.Build(new[] { row }, 0);
        var rec = JsonDocument.Parse(json).RootElement.GetProperty("sensorData")[0];

        Assert.True(Math.Abs(rec.GetProperty("vpd").GetDouble() - 0.9353) < 0.001);
    }

    [Fact]
    public void BuildAveraged_NoRows_ReturnsEmptyString()
    {
        Assert.Equal("", SensorReportShaper.BuildAveraged(Array.Empty<SensorDataRow>(), 1));
    }

    [Fact]
    public void BuildAveraged_TwoDevicesSameBucket_AveragesTheMetric()
    {
        var rows = new[]
        {
            Row("2026-08-29 09:05:00", temp: 10),
            Row("2026-08-29 09:40:00", temp: 20),
        };

        var json = SensorReportShaper.BuildAveraged(rows, 1);
        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement.GetProperty("sensorData");

        Assert.Equal(1, arr.GetArrayLength());
        Assert.Equal(15, arr[0].GetProperty("temperature").GetDouble());
    }

    [Fact]
    public void BuildAveraged_OnlyOneDeviceReportsAMetric_ShowsThatValueDirectly()
    {
        // Same bucket, one row has soilTemperature, the other doesn't - averaging over just the
        // one non-null value must equal that value, not be dragged toward zero/null.
        var withSoil = Row("2026-08-29 09:05:00", temp: 10);
        withSoil.SoilTemperature = 18;
        var withoutSoil = Row("2026-08-29 09:10:00", temp: 12);

        var json = SensorReportShaper.BuildAveraged(new[] { withSoil, withoutSoil }, 1);
        var rec = JsonDocument.Parse(json).RootElement.GetProperty("sensorData")[0];

        Assert.Equal(18, rec.GetProperty("soilTemperature").GetDouble());
        Assert.Equal(11, rec.GetProperty("temperature").GetDouble());
    }

    [Fact]
    public void BuildAveraged_NoDeviceReportsAMetric_StaysNull()
    {
        var json = SensorReportShaper.BuildAveraged(new[] { Row("2026-08-29 09:05:00") }, 1);
        var rec = JsonDocument.Parse(json).RootElement.GetProperty("sensorData")[0];

        Assert.Equal(JsonValueKind.Null, rec.GetProperty("soilTemperature").ValueKind);
    }

    [Fact]
    public void BuildAveraged_DifferentBuckets_KeepsThemSeparate()
    {
        var rows = new[]
        {
            Row("2026-08-29 09:05:00", temp: 10),
            Row("2026-08-29 10:05:00", temp: 30),
        };

        Assert.Equal(2, JsonDocument.Parse(SensorReportShaper.BuildAveraged(rows, 1))
            .RootElement.GetProperty("sensorData").GetArrayLength());
    }
}

public class VpdCalculatorTests
{
    [Fact]
    public void Compute_TemperatureMissing_ReturnsNull() =>
        Assert.Null(api.Utils.VpdCalculator.Compute(null, 60));

    [Fact]
    public void Compute_HumidityMissing_ReturnsNull() =>
        Assert.Null(api.Utils.VpdCalculator.Compute(20, null));

    [Fact]
    public void Compute_20C_60Percent_MatchesTetensFormula()
    {
        double? vpd = api.Utils.VpdCalculator.Compute(20, 60);
        Assert.True(vpd.HasValue);
        Assert.True(Math.Abs(vpd!.Value - 0.9353) < 0.001);
    }
}

public class ClassifyExceptionTests
{
    // ClassifyException is a pure function - never touches the DbContext, so a never-connected one and default settings are enough to construct it.
    private readonly EfRepository _repo = new(
        new AgrumyDbContext(DbOptionsFactory.Build(DbProviderKind.MySql, "server=unused;database=unused;")),
        Options.Create(new AgrumySettings()),
        NullLogger<EfRepository>.Instance,
        new Mock<ICache>().Object); // ClassifyException never touches the cache - unused here

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
    public void TransportError_IsConnectionFailure()
    {
        Assert.Equal(DbFailureKind.ConnectionFailure,
            _repo.ClassifyException(new TimeoutException("connect timeout")));
        Assert.Equal(DbFailureKind.ConnectionFailure,
            _repo.ClassifyException(new System.Net.Sockets.SocketException()));
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(NullReferenceException))]
    public void UnrecognisedException_IsUnknown(Type exType)
    {
        var ex = (Exception)Activator.CreateInstance(exType)!;
        Assert.Equal(DbFailureKind.Unknown, _repo.ClassifyException(ex));
    }

    [Fact]
    public void NotFoundArgumentException_IsUnknown_NotConnectionFailure()
    {
        // The DAL throws ArgumentException for "no such user/device"; that must not read as a DB outage.

        Assert.Equal(DbFailureKind.Unknown,
            _repo.ClassifyException(new ArgumentException("Wrong id, no such person")));
    }

    private static Npgsql.PostgresException Pg(string sqlState) =>
        new("boom", "ERROR", "ERROR", sqlState);

    [Theory]
    [InlineData("23503")] // foreign_key_violation
    [InlineData("23505")] // unique_violation
    [InlineData("23514")] // check_violation
    public void PostgresConstraintSqlState_IsConstraintViolation(string sqlState)
    {
        Assert.Equal(DbFailureKind.ConstraintViolation, _repo.ClassifyException(Pg(sqlState)));
    }

    [Theory]
    [InlineData("40P01")] // deadlock_detected
    [InlineData("40001")] // serialization_failure
    [InlineData("55P03")] // lock_not_available
    public void PostgresContentionSqlState_IsContention(string sqlState)
    {
        Assert.Equal(DbFailureKind.Contention, _repo.ClassifyException(Pg(sqlState)));
    }

    [Fact]
    public void PostgresUndefinedTable_StaysSchemaMissing()
    {
        Assert.Equal(DbFailureKind.SchemaMissing, _repo.ClassifyException(Pg("42P01")));
    }

    [Fact]
    public void DbUpdateException_WrappingConstraintViolation_IsConstraintViolation()
    {
        var ex = new Microsoft.EntityFrameworkCore.DbUpdateException(
            "An error occurred while saving the entity changes.", Pg("23505"));

        Assert.Equal(DbFailureKind.ConstraintViolation, _repo.ClassifyException(ex));
    }
}

public class DbErrorResponseForTests
{
    private static string Json(DbFailureKind kind) =>
        JsonSerializer.Serialize(DbErrorResponse.For(kind));

    [Fact]
    public void For_ConstraintViolation_HasConstraintReason()
    {
        Assert.Contains("constraint_violation", Json(DbFailureKind.ConstraintViolation));
    }

    [Fact]
    public void For_Contention_HasContentionReason()
    {
        Assert.Contains("contention", Json(DbFailureKind.Contention));
    }

    [Fact]
    public void For_Unknown_HasServerErrorReason()
    {
        Assert.Contains("server_error", Json(DbFailureKind.Unknown));
    }

    [Theory]
    [InlineData(DbFailureKind.ConstraintViolation, 409)]
    [InlineData(DbFailureKind.Contention, 503)]
    [InlineData(DbFailureKind.SchemaMissing, 503)]
    [InlineData(DbFailureKind.ConnectionFailure, 503)]
    [InlineData(DbFailureKind.Unknown, 500)]
    public void StatusCodeFor_MapsKindToHttpStatus(DbFailureKind kind, int expected)
    {
        Assert.Equal(expected, DbErrorResponse.StatusCodeFor(kind));
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
