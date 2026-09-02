using System.Text.Json;
using api.Dal.Interface;
using api.Firmware;
using api.Models;
using Moq;

namespace Agrumy.Api.Tests;

/// <summary>
/// Roadmap #94/#93. Exercises FirmwareCatalogService with a mocked repository and a canned
/// IFirmwareFetcher - no database, no network. The Local-repository paths write real files, but
/// only into a per-test temp directory.
/// </summary>
public class FirmwareCatalogServiceTests
{
    private readonly Mock<IRepository> _repo = new(MockBehavior.Strict);
    private readonly FakeFirmwareFetcher _fetcher = new();
    private readonly FirmwareStorage _storage;
    private readonly string _root;

    // In-memory catalog the mock reads/writes, so multi-step flows (sync then list) behave.
    private readonly List<DeviceFirmware> _rows = [];
    private int _nextId = 1;

    public FirmwareCatalogServiceTests()
    {
        _storage = FirmwareTestSupport.NewStorage(out _root);

        _repo.Setup(r => r.FirmwareListAsync()).ReturnsAsync(() => _rows.ToList());
        _repo.Setup(r => r.FirmwareListForBoardAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<FirmwareSource>>()))
             .ReturnsAsync((string board, IReadOnlyCollection<FirmwareSource> sources) => _rows.Where(x => x.Board == board && sources.Contains(x.Source)).ToList());
        _repo.Setup(r => r.FirmwareGetAsync(It.IsAny<int>())).ReturnsAsync((int id) => _rows.FirstOrDefault(x => x.IDDeviceFirmware == id));
        _repo.Setup(r => r.FirmwareAddAsync(It.IsAny<DeviceFirmware>()))
             .ReturnsAsync((DeviceFirmware f) => { f.IDDeviceFirmware = _nextId++; _rows.Add(f); return f.IDDeviceFirmware.Value; });
        _repo.Setup(r => r.FirmwareDeleteAsync(It.IsAny<int>()))
             .Returns((int id) => { _rows.RemoveAll(x => x.IDDeviceFirmware == id); return Task.CompletedTask; });
        _repo.Setup(r => r.FirmwareDeleteBySourceAsync(It.IsAny<FirmwareSource>()))
             .ReturnsAsync((FirmwareSource s) => _rows.RemoveAll(x => x.Source == s && x.Board != null));
    }

    private FirmwareCatalogService NewService() => FirmwareTestSupport.NewCatalog(_repo.Object, _fetcher, _storage);

    private void SetSource(FirmwareSource source, string? customUrl = null) =>
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig
        {
            FirmwareSource = source,
            FirmwareGitHubRepository = "dopiskur/AgrumyDevice",
            FirmwareCustomRepositoryUrl = customUrl,
        });

    private const string GitHubReleasesUrl = "https://api.github.com/repos/dopiskur/AgrumyDevice/releases?per_page=100";

    /// <summary>Two releases (v1.1.0, v1.0.0), each with both boards + a manifest asset, plus a draft
    /// that must be ignored and a stray asset name that must be skipped.</summary>
    private void SetupGitHubReleases()
    {
        static string Asset(string tag, string name) => $"https://github.com/dopiskur/AgrumyDevice/releases/download/{tag}/{name}";
        string manifest110 = JsonSerializer.Serialize(new FirmwareManifest
        {
            Releases = [new FirmwareManifestRelease { Version = "1.1.0", Files = [
                new FirmwareManifestFile { Board = "esp32dev", FileName = "agrumy-esp32dev-v1.1.0.bin", Sha256 = Sha("bin-esp32dev-1.1.0") },
                new FirmwareManifestFile { Board = "esp32s3usbotg", FileName = "agrumy-esp32s3usbotg-v1.1.0.bin", Sha256 = Sha("bin-s3-1.1.0") }] }],
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        _fetcher.Texts[GitHubReleasesUrl] = $$"""
            [
              {"tag_name":"v1.1.0","draft":false,"prerelease":false,"published_at":"2026-09-01T10:00:00Z","assets":[
                 {"name":"agrumy-esp32dev-v1.1.0.bin","browser_download_url":"{{Asset("v1.1.0", "agrumy-esp32dev-v1.1.0.bin")}}","size":100},
                 {"name":"agrumy-esp32s3usbotg-v1.1.0.bin","browser_download_url":"{{Asset("v1.1.0", "agrumy-esp32s3usbotg-v1.1.0.bin")}}","size":101},
                 {"name":"manifest.json","browser_download_url":"{{Asset("v1.1.0", "manifest.json")}}","size":5},
                 {"name":"SHA256SUMS.txt","browser_download_url":"{{Asset("v1.1.0", "SHA256SUMS.txt")}}","size":5}]},
              {"tag_name":"v1.0.0","draft":false,"prerelease":false,"published_at":"2026-08-01T10:00:00Z","assets":[
                 {"name":"agrumy-esp32dev-v1.0.0.bin","browser_download_url":"{{Asset("v1.0.0", "agrumy-esp32dev-v1.0.0.bin")}}","size":90}]},
              {"tag_name":"v9.9.9","draft":true,"prerelease":false,"published_at":null,"assets":[
                 {"name":"agrumy-esp32dev-v9.9.9.bin","browser_download_url":"{{Asset("v9.9.9", "agrumy-esp32dev-v9.9.9.bin")}}","size":1}]}
            ]
            """;
        _fetcher.Texts[Asset("v1.1.0", "manifest.json")] = manifest110;
        _fetcher.Binaries[Asset("v1.1.0", "agrumy-esp32dev-v1.1.0.bin")] = FakeFirmwareFetcher.Bytes("bin-esp32dev-1.1.0");
        _fetcher.Binaries[Asset("v1.1.0", "agrumy-esp32s3usbotg-v1.1.0.bin")] = FakeFirmwareFetcher.Bytes("bin-s3-1.1.0");
        _fetcher.Binaries[Asset("v1.0.0", "agrumy-esp32dev-v1.0.0.bin")] = FakeFirmwareFetcher.Bytes("bin-esp32dev-1.0.0");
    }

    private static string Sha(string text) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(FakeFirmwareFetcher.Bytes(text))).ToLowerInvariant();

    // ---- GitHub source: Refresh --------------------------------------------------------

    [Fact]
    public async Task GitHub_Refresh_Maps_Release_Assets_To_Catalog_Rows_With_Manifest_Checksums()
    {
        SetSource(FirmwareSource.GitHub);
        SetupGitHubReleases();
        _rows.Add(new DeviceFirmware { IDDeviceFirmware = 99, Board = "esp32dev", Version = "0.9.0", Source = FirmwareSource.GitHub, Url = "stale" });

        FirmwareSyncResult result = await NewService().SyncAsync(FirmwareSyncMode.Refresh, "https://api.agrumy.com");

        Assert.Equal(1, result.Removed); // the stale GitHub row was replaced
        Assert.Equal(3, result.Added);   // 2 assets in v1.1.0 + 1 in v1.0.0; draft and non-.bin assets ignored
        Assert.DoesNotContain(_rows, r => r.Version == "9.9.9");
        Assert.DoesNotContain(_rows, r => r.Version == "0.9.0");

        var dev110 = Assert.Single(_rows, r => r.Board == "esp32dev" && r.Version == "1.1.0");
        Assert.Equal(FirmwareSource.GitHub, dev110.Source);
        Assert.Equal(Sha("bin-esp32dev-1.1.0"), dev110.Sha256);          // from the manifest asset
        Assert.StartsWith("https://github.com/", dev110.Url);            // no download in GitHub mode
        Assert.Null(Assert.Single(_rows, r => r.Version == "1.0.0").Sha256); // that release shipped no manifest
        Assert.False(Directory.Exists(_root) && Directory.EnumerateFiles(_root).Any());
    }

    [Fact]
    public async Task LatestForBoard_Uses_Semver_Order_Across_Visible_Sources()
    {
        SetSource(FirmwareSource.GitHub);
        _rows.Add(new DeviceFirmware { IDDeviceFirmware = 1, Board = "esp32dev", Version = "1.9.0", Source = FirmwareSource.GitHub });
        _rows.Add(new DeviceFirmware { IDDeviceFirmware = 2, Board = "esp32dev", Version = "1.10.0", Source = FirmwareSource.Local });  // Local always visible
        _rows.Add(new DeviceFirmware { IDDeviceFirmware = 3, Board = "esp32dev", Version = "2.0.0", Source = FirmwareSource.Custom }); // not the active source
        _rows.Add(new DeviceFirmware { IDDeviceFirmware = 4, Board = "esp32s3usbotg", Version = "3.0.0", Source = FirmwareSource.GitHub });

        DeviceFirmware? latest = await NewService().LatestForBoardAsync("esp32dev");

        Assert.Equal("1.10.0", latest!.Version);
    }

    // ---- Local repository: pull from GitHub (roadmap #94-2a) ---------------------------------

    [Fact]
    public async Task Local_PullIncremental_Downloads_Only_Missing_Files_And_Verifies_Checksums()
    {
        SetSource(FirmwareSource.Local);
        SetupGitHubReleases();
        _rows.Add(new DeviceFirmware { IDDeviceFirmware = 50, Board = "esp32dev", Version = "1.0.0", Source = FirmwareSource.Local, FileName = "agrumy-esp32dev-v1.0.0.bin" });

        FirmwareSyncResult result = await NewService().SyncAsync(FirmwareSyncMode.PullIncremental, "https://api.agrumy.com/");

        Assert.Equal(2, result.Added);
        Assert.Equal(1, result.Skipped);
        Assert.Empty(result.Warnings);
        Assert.DoesNotContain(_fetcher.Requested, u => u.EndsWith("agrumy-esp32dev-v1.0.0.bin")); // already local - not re-downloaded

        var dev110 = Assert.Single(_rows, r => r.Board == "esp32dev" && r.Version == "1.1.0");
        Assert.Equal(FirmwareSource.Local, dev110.Source);
        Assert.Equal("https://api.agrumy.com/api/Firmware/Download/agrumy-esp32dev-v1.1.0.bin", dev110.Url);
        Assert.Equal(Sha("bin-esp32dev-1.1.0"), dev110.Sha256);
        Assert.True(File.Exists(Path.Combine(_root, "agrumy-esp32dev-v1.1.0.bin")));
    }

    [Fact]
    public async Task Local_Pull_Discards_A_File_Whose_Checksum_Does_Not_Match_The_Release_Manifest()
    {
        SetSource(FirmwareSource.Local);
        SetupGitHubReleases();
        _fetcher.Binaries["https://github.com/dopiskur/AgrumyDevice/releases/download/v1.1.0/agrumy-esp32dev-v1.1.0.bin"] = FakeFirmwareFetcher.Bytes("CORRUPTED");

        FirmwareSyncResult result = await NewService().SyncAsync(FirmwareSyncMode.PullIncremental, "https://api.agrumy.com");

        Assert.Equal(2, result.Added); // the other two are fine
        Assert.Contains(result.Warnings, w => w.Contains("agrumy-esp32dev-v1.1.0.bin") && w.Contains("SHA-256"));
        Assert.DoesNotContain(_rows, r => r.Board == "esp32dev" && r.Version == "1.1.0");
        Assert.False(File.Exists(Path.Combine(_root, "agrumy-esp32dev-v1.1.0.bin")));
    }

    [Fact]
    public async Task Local_PullFull_Wipes_Existing_Local_Rows_And_Files_First()
    {
        SetSource(FirmwareSource.Local);
        SetupGitHubReleases();
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "agrumy-esp32dev-v0.5.0.bin"), "old");
        _rows.Add(new DeviceFirmware { IDDeviceFirmware = 50, Board = "esp32dev", Version = "0.5.0", Source = FirmwareSource.Local, FileName = "agrumy-esp32dev-v0.5.0.bin" });
        _rows.Add(new DeviceFirmware { IDDeviceFirmware = 51, Board = "esp32dev", Version = "0.4.0", Source = FirmwareSource.GitHub }); // other sources untouched

        FirmwareSyncResult result = await NewService().SyncAsync(FirmwareSyncMode.PullFull, "https://api.agrumy.com");

        Assert.Equal(1, result.Removed);
        Assert.Equal(3, result.Added);
        Assert.False(File.Exists(Path.Combine(_root, "agrumy-esp32dev-v0.5.0.bin")));
        Assert.Contains(_rows, r => r.Version == "0.4.0" && r.Source == FirmwareSource.GitHub);
    }

    // ---- Local repository: import a server directory (roadmap #94-2b) ----------------------

    [Fact]
    public async Task Import_Verifies_Manifest_Checksums_And_Rejects_Off_Convention_Files()
    {
        SetSource(FirmwareSource.Local);
        string usb = Path.Combine(Path.GetTempPath(), "agrumy-fw-tests", "usb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(usb);
        await File.WriteAllTextAsync(Path.Combine(usb, "agrumy-esp32dev-v2.0.0.bin"), "good");
        await File.WriteAllTextAsync(Path.Combine(usb, "agrumy-esp32s3usbotg-v2.0.0.bin"), "tampered");
        await File.WriteAllTextAsync(Path.Combine(usb, "firmware.bin"), "whatever");
        await File.WriteAllTextAsync(Path.Combine(usb, "manifest.json"), JsonSerializer.Serialize(new FirmwareManifest
        {
            Releases = [new FirmwareManifestRelease { Version = "2.0.0", Files = [
                new FirmwareManifestFile { FileName = "agrumy-esp32dev-v2.0.0.bin", Sha256 = Sha("good") },
                new FirmwareManifestFile { FileName = "agrumy-esp32s3usbotg-v2.0.0.bin", Sha256 = Sha("original") }] }],
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        FirmwareSyncResult result = await NewService().ImportFromDirectoryAsync(usb, "https://api.agrumy.com");

        Assert.Equal(1, result.Added);
        Assert.Equal(2, result.Skipped);
        Assert.Contains(result.Warnings, w => w.Contains("agrumy-esp32s3usbotg-v2.0.0.bin") && w.Contains("SHA-256"));
        Assert.Contains(result.Warnings, w => w.Contains("firmware.bin") && w.Contains("naming convention"));
        var imported = Assert.Single(_rows);
        Assert.Equal(("esp32dev", "2.0.0", FirmwareSource.Local), (imported.Board, imported.Version, imported.Source));
        Assert.True(File.Exists(Path.Combine(_root, "agrumy-esp32dev-v2.0.0.bin")));
    }

    [Fact]
    public async Task Import_Of_Missing_Directory_Is_A_Warning_Not_An_Exception()
    {
        SetSource(FirmwareSource.Local);
        FirmwareSyncResult result = await NewService().ImportFromDirectoryAsync(@"Z:\does\not\exist", "https://api.agrumy.com");
        Assert.Equal(0, result.Added);
        Assert.Single(result.Warnings);
    }

    // ---- Local repository: manual upload (roadmap #94-2c / #93-c-2) ---------------------------

    [Fact]
    public async Task Upload_Rejects_A_File_Name_Outside_The_Convention()
    {
        SetSource(FirmwareSource.GitHub);
        (DeviceFirmware? fw, string? error) = await NewService().UploadAsync("firmware.bin", new MemoryStream([1, 2, 3]), "https://api.agrumy.com");
        Assert.Null(fw);
        Assert.Contains("agrumy-<board>-v<version>.bin", error);
        Assert.Empty(_rows);
    }

    [Fact]
    public async Task Upload_Replaces_An_Existing_Local_Row_For_The_Same_Board_And_Version()
    {
        SetSource(FirmwareSource.GitHub); // uploads are Local rows regardless of the active source
        _rows.Add(new DeviceFirmware { IDDeviceFirmware = 7, Board = "esp32dev", Version = "1.0.0", Source = FirmwareSource.Local, FileName = "agrumy-esp32dev-v1.0.0.bin", Sha256 = "old" });

        (DeviceFirmware? fw, string? error) = await NewService().UploadAsync("agrumy-esp32dev-v1.0.0.bin", new MemoryStream(FakeFirmwareFetcher.Bytes("new")), "https://api.agrumy.com");

        Assert.Null(error);
        var only = Assert.Single(_rows);
        Assert.Equal(fw!.IDDeviceFirmware, only.IDDeviceFirmware);
        Assert.Equal(Sha("new"), only.Sha256);
        Assert.Equal(3, only.SizeBytes);
    }

    // ---- Custom source ---------------------------------------------------------------------

    [Fact]
    public async Task Custom_Refresh_Resolves_Relative_Manifest_Urls_Against_The_Manifest_Location()
    {
        SetSource(FirmwareSource.Custom, "https://fw.example.com/agrumy/manifest.json");
        _fetcher.Texts["https://fw.example.com/agrumy/manifest.json"] = JsonSerializer.Serialize(new FirmwareManifest
        {
            Releases = [new FirmwareManifestRelease { Version = "3.0.0", Files = [
                new FirmwareManifestFile { Board = "esp32dev", FileName = "agrumy-esp32dev-v3.0.0.bin", Sha256 = "abc" },                        // no url - next to the manifest
                new FirmwareManifestFile { Board = "esp32s3usbotg", FileName = "agrumy-esp32s3usbotg-v3.0.0.bin", Url = "https://cdn.example.com/s3.bin" }] }],
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        FirmwareSyncResult result = await NewService().SyncAsync(FirmwareSyncMode.Refresh, "https://api.agrumy.com");

        Assert.Equal(2, result.Added);
        Assert.Equal("https://fw.example.com/agrumy/agrumy-esp32dev-v3.0.0.bin", Assert.Single(_rows, r => r.Board == "esp32dev").Url);
        Assert.Equal("https://cdn.example.com/s3.bin", Assert.Single(_rows, r => r.Board == "esp32s3usbotg").Url);
        Assert.All(_rows, r => Assert.Equal(FirmwareSource.Custom, r.Source));
    }

    [Fact]
    public async Task Custom_Refresh_Without_A_Manifest_Url_Is_A_Warning()
    {
        SetSource(FirmwareSource.Custom, null);
        FirmwareSyncResult result = await NewService().SyncAsync(FirmwareSyncMode.Refresh, "https://api.agrumy.com");
        Assert.Single(result.Warnings);
        Assert.Empty(_fetcher.Requested);
    }

    // ---- per-device offer + request (roadmap #93) ---------------------------------------------

    [Fact]
    public async Task ResolveOffer_Nothing_When_FirmwareUpdate_Not_Set()
    {
        Assert.Null(await NewService().ResolveOfferAsync(new Device { IDDevice = 1, FirmwareUpdate = false, DeviceTypeID = 3 }, "esp32dev"));
        // Strict mock: no repository call was set up - any lookup would have thrown.
    }

    [Fact]
    public async Task ResolveOffer_Pinned_Target_Wins_Over_Latest()
    {
        SetSource(FirmwareSource.GitHub);
        _rows.Add(new DeviceFirmware { IDDeviceFirmware = 1, Board = "esp32dev", Version = "1.0.0", Source = FirmwareSource.GitHub, Url = "u100" });
        _rows.Add(new DeviceFirmware { IDDeviceFirmware = 2, Board = "esp32dev", Version = "1.1.0", Source = FirmwareSource.GitHub, Url = "u110" });
        var device = new Device { IDDevice = 1, FirmwareUpdate = true, DeviceTypeID = 3 };

        Assert.Equal("1.1.0", (await NewService().ResolveOfferAsync(device, "esp32dev"))!.Version);
        device.FirmwareTargetVersion = "1.0.0"; // rollback
        Assert.Equal("1.0.0", (await NewService().ResolveOfferAsync(device, "esp32dev"))!.Version);
    }

    [Fact]
    public async Task ResolveOffer_Falls_Back_To_Legacy_DeviceType_Row_When_Board_Unknown()
    {
        _repo.Setup(r => r.DeviceFirmwareLatestGetAsync(3)).ReturnsAsync(new DeviceFirmware { Version = "0.1.5", Url = "legacy" });

        DeviceFirmware? offer = await NewService().ResolveOfferAsync(new Device { IDDevice = 1, FirmwareUpdate = true, DeviceTypeID = 3 }, board: null);

        Assert.Equal("legacy", offer!.Url);
    }

    [Fact]
    public async Task RequestUpdate_Specific_Version_Must_Exist_For_The_Device_Board()
    {
        SetSource(FirmwareSource.GitHub);
        _rows.Add(new DeviceFirmware { IDDeviceFirmware = 1, Board = "esp32dev", Version = "1.0.0", Source = FirmwareSource.GitHub });
        _repo.Setup(r => r.DeviceBoardGetAsync(5)).ReturnsAsync("esp32dev");
        _repo.Setup(r => r.DeviceFirmwareUpdateSetAsync(5, true, "1.0.0")).Returns(Task.CompletedTask);
        var service = NewService();
        var device = new Device { IDDevice = 5 };

        Assert.Null(await service.RequestUpdateAsync(device, "v1.0.0"));
        Assert.Contains("not in the catalog", await service.RequestUpdateAsync(device, "1.2.0"));
        Assert.Contains("not a valid version", await service.RequestUpdateAsync(device, "latest"));
        _repo.Verify(r => r.DeviceFirmwareUpdateSetAsync(5, true, "1.0.0"), Times.Once);
    }

    [Fact]
    public async Task RequestUpdate_Latest_Needs_No_Board()
    {
        _repo.Setup(r => r.DeviceFirmwareUpdateSetAsync(5, true, null)).Returns(Task.CompletedTask);
        Assert.Null(await NewService().RequestUpdateAsync(new Device { IDDevice = 5 }, null));
        _repo.Verify(r => r.DeviceFirmwareUpdateSetAsync(5, true, null), Times.Once);
    }

    [Fact]
    public async Task NoteHeartbeat_Clears_The_Request_Only_When_The_Reported_Version_Matches()
    {
        SetSource(FirmwareSource.GitHub);
        _rows.Add(new DeviceFirmware { IDDeviceFirmware = 1, Board = "esp32dev", Version = "1.1.0", Source = FirmwareSource.GitHub });
        _repo.Setup(r => r.DeviceFirmwareUpdateSetAsync(5, false, null)).Returns(Task.CompletedTask);
        var service = NewService();
        var device = new Device { IDDevice = 5, FirmwareUpdate = true };

        Assert.False(await service.NoteHeartbeatAsync(device, "1.0.0", "esp32dev")); // still on the old one
        Assert.True(await service.NoteHeartbeatAsync(device, "1.1.0", "esp32dev"));  // latest arrived
        _repo.Verify(r => r.DeviceFirmwareUpdateSetAsync(5, false, null), Times.Once);

        device.FirmwareTargetVersion = "1.0.0"; // pinned rollback: only THAT version counts
        Assert.False(await service.NoteHeartbeatAsync(device, "1.1.0", "esp32dev"));
        Assert.True(await service.NoteHeartbeatAsync(device, "1.0.0", "esp32dev"));
    }

    // ---- manifest (roadmap #94-C1) --------------------------------------------------------------

    [Fact]
    public async Task Manifest_Groups_Visible_Rows_By_Version_Newest_First_Preferring_Local_Per_Board()
    {
        SetSource(FirmwareSource.GitHub);
        _rows.Add(new DeviceFirmware { IDDeviceFirmware = 1, Board = "esp32dev", Version = "1.0.0", Source = FirmwareSource.GitHub, FileName = "agrumy-esp32dev-v1.0.0.bin", Url = "gh" });
        _rows.Add(new DeviceFirmware { IDDeviceFirmware = 2, Board = "esp32dev", Version = "1.0.0", Source = FirmwareSource.Local, FileName = "agrumy-esp32dev-v1.0.0.bin", Url = "local" });
        _rows.Add(new DeviceFirmware { IDDeviceFirmware = 3, Board = "esp32dev", Version = "1.1.0", Source = FirmwareSource.GitHub, FileName = "agrumy-esp32dev-v1.1.0.bin", Url = "gh2" });
        _rows.Add(new DeviceFirmware { IDDeviceFirmware = 4, Board = "esp32dev", Version = "5.0.0", Source = FirmwareSource.Custom, FileName = "agrumy-esp32dev-v5.0.0.bin" }); // not visible

        FirmwareManifest manifest = await NewService().BuildManifestAsync("https://api.agrumy.com");

        Assert.Equal(["1.1.0", "1.0.0"], manifest.Releases.Select(r => r.Version));
        Assert.Equal("local", Assert.Single(manifest.Releases[1].Files).Url);
    }
}
