# Device <-> API contract (`contracts/device-api/`)

Narrow, machine-checkable JSON Schemas for the **four** HTTP endpoints the AgrumyDevice
firmware actually calls. This is deliberately **not** a full OpenAPI document - Swashbuckle
already generates general API docs from the C# models. The point here is a tight contract
that a test can enforce, so that renaming/removing a JSON field on one side breaks a build
instead of a device in the field.

## Endpoints & files

| Endpoint | Method | Request schema | Response schema |
|---|---|---|---|
| `/api/Device/Register` | POST | `register.request.schema.json` | `register.response.schema.json` |
| `/api/Device/Authenticate` | POST | `authenticate.request.schema.json` | `authenticate.response.schema.json` |
| `/api/Device/Config` | POST | `config.request.schema.json` | `config.response.schema.json` |
| `/api/SensorData` | POST | `sensordata.request.schema.json` | *(none - endpoint returns a bare cached ConfigVersion)* |

Draft-07. `register.response.schema.json` and `config.response.schema.json` are **identical
by construction** (both are the serialized `api.Models.DeviceConfig`, produced by the same
`DeviceApiController.BuildDeviceConfigAsync()`); keep the two files in sync.

## How the schemas were derived

From the **actual current behaviour of both code bases**, not from docs:

* Request shapes: read out of `AgrumyDevice/src/Controller/*.cpp` (`registerDevice()`,
  `apiAuthenticate()`, `apiConfig()`, `buildSensorDataPayload()`).
* Response shapes: the API was run locally and each endpoint called; the live JSON
  confirmed **camelCase** property names, that **null values are kept** (not omitted),
  that `deviceID` stays lowercase, and that `deviceConfigSensor` / `deviceConfigController`
  are `null` unless the corresponding `*Enabled` flag is true.

Gotchas captured in the schemas (see per-field `description` text):
`devicePin` is sent as a **string**; the config-request key is **PascalCase**
(`ConfigVersion`) unlike every other payload; `sensorPH` keeps an uppercase suffix;
the SensorData `rainLevel` key does **not** match the stored proc's `$.rainlevel` path.

## Enforcement

* **API side** - `Agrumy.Api.Tests/ContractTests.cs` serializes the C# models with the
  same `System.Text.Json` Web defaults MVC uses and validates the output against these
  schemas. Runs in the normal `dotnet test` CI step (`.github/workflows/build.yml`).
* **Firmware side** - `AgrumyDevice/tools/contract-check/` keeps a hand-maintained list
  of the JSON keys the firmware sends/expects and checks it against a **copy** of these
  schemas (copied, not submoduled - see that repo's `contracts/device-api/README.md` for
  the source commit hash). Runs in `.github/workflows/contract-check.yml` there.

## When you change the contract

If you touch any of:

* `Agrumy.Shared/Models/Device.cs` - `DeviceConfig`, `DeviceRegistration`,
  `DeviceAuthentication`, `DeviceConfigSensor`, `DeviceConfigController`
* `DeviceApiController.BuildDeviceConfigAsync()` / `GetConfig` / `DeviceRegistration` / `ReqAuth`
* `SensorDataController.Post` or the `SensorDataPush` proc's column list
* the JSON serialization options in `Program.cs`

then, in the same change:

1. Update the affected schema file(s) here.
2. Run `dotnet test` - `ContractTests` must stay green.
3. Copy the changed schema file(s) into `AgrumyDevice/contracts/device-api/` and update
   the source-commit line in that repo's README, plus the field list in
   `AgrumyDevice/tools/contract-check/firmware_fields.py`.
4. If the firmware's actual payload/parsing changed, update `AgrumyDevice/src/...` too.
