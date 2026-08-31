using api.Controllers.API;
using api.Dal.Interface;
using api.Models;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Agrumy.Api.Tests;

/// <summary>
/// Roadmap #3 (OTA). Exercises the firmware-lookup branch added to
/// DeviceApiController.BuildDeviceConfigAsync() through the public Register action
/// (Config takes the same path). No database - IRepository is mocked.
/// </summary>
public class DeviceFirmwareOtaTests
{
    private readonly Mock<IRepository> _repo = new(MockBehavior.Strict);
    private readonly Mock<ICache> _cache = new();

    private DeviceConfig RegisterAndGetConfig(Device device)
    {
        _repo.Setup(r => r.UserGetAsync(null, "owner@example.com", null))
             .ReturnsAsync(new User { IDUser = 77, TenantID = device.TenantID, DevicePin = "ABC234", DevicePinExpires = DateTime.UtcNow.AddHours(1) });
        _repo.Setup(r => r.DeviceGetAsync(device.TenantID, null, null, "AABBCCDDEEFF"))
             .ReturnsAsync(device); // IDDevice set => controller skips DeviceAddAsync
        _repo.Setup(r => r.UserSetDevicePinAsync(77, null, null)).ReturnsAsync(true); // roadmap #70: success consumes the PIN

        var controller = new DeviceApiController(_repo.Object, _cache.Object);
        var result = controller.DeviceRegistration(new DeviceRegistration
        {
            Email = "owner@example.com",
            DevicePin = "ABC234",
            MacAddress = "AABBCCDDEEFF",
        }).GetAwaiter().GetResult();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<DeviceConfig>(ok.Value);
    }

    private static Device BaseDevice() => new()
    {
        IDDevice = 500,
        TenantID = 0,
        DeviceTypeID = 7,
        ApiId = "api-id",
        ApiKey = "api-key",
        DeviceSensorEnabled = false,
        DeviceControllerEnabled = false,
    };

    [Fact]
    public void FirmwareUpdateFlagSet_PopulatesVersionAndUrlFromLatestBuild()
    {
        var device = BaseDevice();
        device.FirmwareUpdate = true;

        _repo.Setup(r => r.DeviceFirmwareLatestGetAsync(7))
             .ReturnsAsync(new DeviceFirmware
             {
                 DeviceTypeID = 7,
                 Version = "0.2.0",
                 Url = "https://cdn.agrumy.com/fw/esp32/0.2.0.bin",
             });

        var cfg = RegisterAndGetConfig(device);

        Assert.True(cfg.FirmwareUpdate);
        Assert.Equal("0.2.0", cfg.FirmwareVersion);
        Assert.Equal("https://cdn.agrumy.com/fw/esp32/0.2.0.bin", cfg.FirmwareUrl);
    }

    [Fact]
    public void FirmwareUpdateFlagClear_DoesNotQueryFirmware_AndLeavesFieldsNull()
    {
        var device = BaseDevice();
        device.FirmwareUpdate = false;

        var cfg = RegisterAndGetConfig(device);

        Assert.Null(cfg.FirmwareVersion);
        Assert.Null(cfg.FirmwareUrl);
        // Strict mock: a call to DeviceFirmwareLatestGetAsync would have thrown.
        _repo.Verify(r => r.DeviceFirmwareLatestGetAsync(It.IsAny<int?>()), Times.Never);
    }

    [Fact]
    public void FirmwareUpdateFlagSet_ButNoBuildRow_LeavesFieldsNull_NoThrow()
    {
        var device = BaseDevice();
        device.FirmwareUpdate = true;
        _repo.Setup(r => r.DeviceFirmwareLatestGetAsync(7)).ReturnsAsync((DeviceFirmware?)null);

        var cfg = RegisterAndGetConfig(device);

        Assert.True(cfg.FirmwareUpdate);
        Assert.Null(cfg.FirmwareVersion);
        Assert.Null(cfg.FirmwareUrl);
    }

    [Fact]
    public void FirmwareUpdateFlagSet_ButDeviceTypeNull_DoesNotQueryFirmware()
    {
        var device = BaseDevice();
        device.FirmwareUpdate = true;
        device.DeviceTypeID = null;

        var cfg = RegisterAndGetConfig(device);

        Assert.Null(cfg.FirmwareVersion);
        _repo.Verify(r => r.DeviceFirmwareLatestGetAsync(It.IsAny<int?>()), Times.Never);
    }
}
