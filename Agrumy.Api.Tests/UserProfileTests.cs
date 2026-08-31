using System.Security.Claims;
using api.Controllers.API;
using api.Dal.Interface;
using api.Models;
using api.Notifications;
using api.Security;
using api.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Agrumy.Api.Tests;

/// <summary>
/// Roadmap #71 follow-up: TimeZoneHelper display conversion (UTC stays the only stored format)
/// and the self-scoped PUT /api/User/Profile endpoint. No database - IRepository is mocked;
/// the write-only-profile-columns guarantee is proven against real engines in
/// <see cref="RelationalIntegrationTests.UserProfileSet_Writes_Only_Profile_Fields"/>.
/// </summary>
public class UserProfileTests
{
    // ---- TimeZoneHelper.ToUserLocalTime ------------------------------------------------

    [Theory]
    // Europe/Zagreb across the DST boundary: +2h in summer (CEST), +1h in winter (CET).
    [InlineData("2026-07-15 10:00:00", "2026-07-15 12:00:00")]
    [InlineData("2026-01-15 10:00:00", "2026-01-15 11:00:00")]
    // Spring-forward 2026-03-29: 00:30 UTC is still CET (+1), 01:30 UTC is already CEST (+2) -
    // proving the IANA id resolves the transition itself, which a stored fixed offset cannot.
    [InlineData("2026-03-29 00:30:00", "2026-03-29 01:30:00")]
    [InlineData("2026-03-29 01:30:00", "2026-03-29 03:30:00")]
    // Fall-back 2026-10-25: 00:30 UTC is CEST (+2), 01:30 UTC is CET (+1).
    [InlineData("2026-10-25 00:30:00", "2026-10-25 02:30:00")]
    [InlineData("2026-10-25 01:30:00", "2026-10-25 02:30:00")]
    public void ToUserLocalTime_Zagreb_Applies_Correct_DST_Offset(string utc, string expectedLocal)
    {
        DateTime input = DateTime.Parse(utc);
        Assert.Equal(DateTime.Parse(expectedLocal), TimeZoneHelper.ToUserLocalTime(input, "Europe/Zagreb"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ToUserLocalTime_NoZone_Returns_Utc_Unchanged(string? zone)
    {
        var utc = new DateTime(2026, 7, 15, 10, 0, 0);
        Assert.Equal(utc, TimeZoneHelper.ToUserLocalTime(utc, zone));
    }

    [Theory]
    [InlineData("Not/AZone")]
    [InlineData("garbage")]
    public void ToUserLocalTime_UnknownZone_Falls_Back_To_Utc_Without_Throwing(string zone)
    {
        var utc = new DateTime(2026, 7, 15, 10, 0, 0);
        Assert.Equal(utc, TimeZoneHelper.ToUserLocalTime(utc, zone));
    }

    // ---- TimeZoneHelper.TryNormalizeToIana ---------------------------------------------

    [Fact]
    public void TryNormalizeToIana_Iana_Id_Passes_Through()
    {
        Assert.True(TimeZoneHelper.TryNormalizeToIana("Europe/Zagreb", out string iana));
        Assert.Equal("Europe/Zagreb", iana);
    }

    [Fact]
    public void TryNormalizeToIana_Windows_Id_Becomes_Iana()
    {
        // Only meaningful where the Windows catalog exists (dev boxes); on Linux the id is unknown.
        if (TimeZoneHelper.TryNormalizeToIana("Central European Standard Time", out string iana))
        {
            Assert.DoesNotContain(" ", iana); // IANA ids are Area/Location, never spaced Windows names
            Assert.Contains("/", iana);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Not/AZone")]
    public void TryNormalizeToIana_Invalid_Returns_False(string? id)
    {
        Assert.False(TimeZoneHelper.TryNormalizeToIana(id, out _));
    }

    [Fact]
    public void GetTimeZoneOptions_Returns_Deduped_Iana_Ids()
    {
        var options = TimeZoneHelper.GetTimeZoneOptions();
        Assert.NotEmpty(options);
        Assert.Equal(options.Count, options.Select(o => o.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(options, o => Assert.True(TimeZoneHelper.TryNormalizeToIana(o.Id, out _)));
    }

    // ---- SensorDataTimeLocalizer (chart payload display conversion) --------------------

    [Fact]
    public void LocalizeDates_Rewrites_DateCreated_To_User_Zone()
    {
        const string json = "{\"sensorData\":[{\"temperature\":21.5,\"dateCreated\":\"2026-07-15 10:00:00\"},{\"temperature\":22,\"dateCreated\":\"2026-01-15 10:00:00\"}]}";
        string? localized = SensorDataTimeLocalizer.LocalizeDates(json, "Europe/Zagreb");

        Assert.NotNull(localized);
        Assert.Contains("2026-07-15 12:00:00", localized); // CEST +2
        Assert.Contains("2026-01-15 11:00:00", localized); // CET +1
        Assert.Contains("21.5", localized);                // measurements untouched
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void LocalizeDates_NoZone_Returns_Input_Unchanged(string? zone)
    {
        const string json = "{\"sensorData\":[{\"dateCreated\":\"2026-07-15 10:00:00\"}]}";
        Assert.Equal(json, SensorDataTimeLocalizer.LocalizeDates(json, zone));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{\"somethingElse\":1}")]
    public void LocalizeDates_Empty_Or_Malformed_Payload_Is_Passed_Through(string? json)
    {
        Assert.Equal(json, SensorDataTimeLocalizer.LocalizeDates(json, "Europe/Zagreb"));
    }

    // ---- PUT /api/User/Profile ---------------------------------------------------------

    private readonly Mock<IRepository> _repo = new(MockBehavior.Strict);
    private readonly Mock<ICache> _cache = new();
    private readonly Mock<INotificationDispatcher> _notifications = new();

    private UserApiController NewController(string? email)
    {
        var controller = new UserApiController(_repo.Object, _cache.Object, _notifications.Object);
        var claims = new List<Claim> { new("TenantID", "1") };
        if (email != null)
        {
            claims.Add(new Claim(ClaimTypes.Name, email));
        }
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) }
        };
        return controller;
    }

    [Fact]
    public async Task UserProfileSet_Writes_Own_Row_With_Normalized_Zone()
    {
        _repo.Setup(r => r.UserProfileSetAsync("me@x.com", "Ana", "Anić", "Europe/Zagreb")).ReturnsAsync(true);

        var result = await NewController("me@x.com").UserProfileSet(
            new UserProfileUpdate { FirstName = "Ana", LastName = "Anić", TimeZone = "Europe/Zagreb" });

        Assert.IsType<OkObjectResult>(result.Result);
        _repo.VerifyAll();
    }

    [Fact]
    public async Task UserProfileSet_UnknownZone_Returns400_And_Writes_Nothing()
    {
        var result = await NewController("me@x.com").UserProfileSet(
            new UserProfileUpdate { FirstName = "Ana", TimeZone = "Not/AZone" });

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Unknown time zone", bad.Value!.ToString());
        // MockBehavior.Strict: any repo call would already have thrown - verify for clarity.
        _repo.Verify(r => r.UserProfileSetAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task UserProfileSet_NullZone_Stores_Null()
    {
        _repo.Setup(r => r.UserProfileSetAsync("me@x.com", "Ana", null, null)).ReturnsAsync(true);

        var result = await NewController("me@x.com").UserProfileSet(
            new UserProfileUpdate { FirstName = "Ana", TimeZone = null });

        Assert.IsType<OkObjectResult>(result.Result);
        _repo.VerifyAll();
    }

    [Fact]
    public async Task UserProfileSet_NoIdentity_Returns401()
    {
        var result = await NewController(null).UserProfileSet(new UserProfileUpdate { FirstName = "Ana" });
        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    /// <summary>The payload type itself is the containment: if someone ever adds an
    /// authorization-bearing property to UserProfileUpdate, this fails and forces a conscious look.</summary>
    [Fact]
    public void UserProfileUpdate_Carries_No_Authorization_Fields()
    {
        var names = typeof(UserProfileUpdate).GetProperties().Select(p => p.Name).ToList();
        Assert.Equal(new[] { "FirstName", "LastName", "TimeZone" }.OrderBy(n => n), names.OrderBy(n => n));
    }

    // ---- POST /api/User/DevicePin (roadmap #70) ----------------------------------------

    [Fact]
    public async Task DevicePinGenerate_StoresAndReturns_FreshPin_WithExpiry()
    {
        _repo.Setup(r => r.UserGetAsync(null, "me@x.com", null)).ReturnsAsync(new User { IDUser = 5, Email = "me@x.com" });

        string? storedPin = null;
        DateTime? storedExpiry = null;
        _repo.Setup(r => r.UserSetDevicePinAsync(5, It.IsAny<string?>(), It.IsAny<DateTime?>()))
             .Callback<int, string?, DateTime?>((_, pin, exp) => { storedPin = pin; storedExpiry = exp; })
             .ReturnsAsync(true);

        var result = await NewController("me@x.com").DevicePinGenerate();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<DevicePinResult>(ok.Value);
        Assert.Equal(storedPin, body.DevicePin);       // the caller sees exactly what was stored
        Assert.Equal(storedExpiry, body.ExpiresAt);
        Assert.Equal(AuthenticationProvider.PinLength, body.DevicePin!.Length);
        Assert.True(body.ExpiresAt > DateTime.UtcNow.AddHours(23)); // ~PinValidHours out
    }

    [Fact]
    public async Task DevicePinGenerate_NoIdentity_Returns401_And_StoresNothing()
    {
        var result = await NewController(null).DevicePinGenerate();
        Assert.IsType<UnauthorizedResult>(result.Result);
        _repo.Verify(r => r.UserSetDevicePinAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<DateTime?>()), Times.Never);
    }
}
