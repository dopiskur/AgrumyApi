# Agrumy

Agrumy is a small multi-tenant backend + admin UI for managing IoT devices that
monitor and control greenhouse/citrus micro-climate - temperature, humidity, soil
moisture, light, CO2, water level - and drive relays (ventilation, heating,
irrigation, lighting) off configurable thresholds and time intervals. It is the
server side of a system whose device firmware lives in the separate `AgrumyDevice`
repository; this repository is the API those devices talk to, plus the MVC admin
UI operators use to manage devices, users and tenants.

**.NET 10 SDK required.**

`agrumy.sln` splits into three projects:

| Project | Type | What it is |
| --- | --- | --- |
| `Agrumy.Shared` | class library | Models (`api.Models`), `Config`, `Security` (`JwtTokenProvider`, `AuthenticationProvider`). Referenced by both apps. |
| `Agrumy.Api` | Web API | Device/sensor communication + admin API (`Controllers/API`), data access (`Dal/SqlRepository`, stored procedures), MySQL/MariaDB, JWT bearer auth, Swagger, startup DB health-check / schema auto-provisioning (`Schema/SchemaScripts`). |
| `Agrumy.Web` | MVC app | Admin UI (`Controllers/View`, `Views/`, `wwwroot/`). Talks to `Agrumy.Api` **only over HTTP** (`Dal/ApiRepository` + `HttpClient` with a JWT bearer token). No direct database access. |

`db/` holds the schema dump (`agrumyDB-final.sql`, `agrumyDB-withData.sql`) and the
old deployment notes (`README.txt`). The live schema is also versioned in code at
`Agrumy.Api/Schema/SchemaScripts.cs`.

## How it works

1. **Registration.** A device calls `POST /api/Device/Register` with the owning
   user's email, that user's `DevicePin` (a 4-digit PIN generated at user
   creation), and its MAC address. The pin has to match; on success the API
   creates the device row (if it doesn't exist yet) under that user's tenant and
   hands back an `ApiId`/`ApiKey` pair plus its current config.
2. **Auth.** The device authenticates with `ApiId`/`ApiKey` via
   `POST /api/Device/Authenticate` (constant-time comparison,
   `DeviceAuthenticationProvider.VerifyDeviceAsync`) and gets back a short-lived
   `apiAuth` token that's cached server-side in memory, not a JWT.
3. **Config sync.** The device polls `POST /api/Device/Config` with its current
   `ConfigVersion`. The API only sends a new config body back if the version
   differs from what's stored; otherwise it replies with an empty 200 so the
   device does nothing. Sensor telemetry goes up the other way via
   `POST /api/SensorData`.
4. **Control is local to the device.** Relay decisions (ventilation, heating,
   water pump, lighting) run in the device firmware (`AgrumyDevice` repo) against
   whatever config it last saved to its own flash storage. If the config-sync
   request fails - no network, API unreachable - the firmware logs the failure
   and carries on with the previous config instead of halting, so irrigation/
   climate control keeps running on stale-but-known-good settings through an
   outage; it just won't pick up config *changes* until connectivity comes back.

## Why not just use Home Assistant / ESPHome / ThingsBoard?

Honestly, for a single greenhouse with off-the-shelf sensors, **Home Assistant or
ESPHome is almost certainly the better choice** - mature device support, a real
automation/rule engine, a huge community, and no backend to run or firmware to
maintain yourself. **ThingsBoard** covers similar ground to Agrumy (multi-tenant
device fleets, telemetry, dashboards) far more completely, with an actual rule
engine and support for many transport protocols.

Agrumy doesn't have a rule engine at all - device behavior is a fixed set of
threshold pairs (temp/humidity/moisture/light/water low-high) and interval
settings (ventilation/light/heating/water-pump enabled + interval + length),
configured per device through `DeviceConfigController`/`DeviceConfigSensor`. If
you need to express "if X and not Y between 6pm and 10pm, do Z," you're writing
C# to add it, not YAML.

Where Agrumy makes sense: you're building (or already run) your own firmware for
a specific vertical - here, citrus/greenhouse irrigation and micro-climate - and
want a small, self-hosted, multi-tenant backend you fully own and can shape
around that one domain, rather than adapting a general-purpose platform's data
model to fit it. It's a starting point for a narrow, vertical-specific product,
not a general home-automation hub.

## Quickstart

`appsettings.json` is git-ignored in every project (it holds real secrets). Copy
the template and fill it in:

```
cp Agrumy.Api/appsettings.json.example Agrumy.Api/appsettings.json
cp Agrumy.Web/appsettings.json.example Agrumy.Web/appsettings.json
```

Build the solution:

```
dotnet build agrumy.sln
```

Start the API first, then the web app (the web app calls the API on startup for
login):

```
# terminal 1
dotnet run --project Agrumy.Api     # http://localhost:5000  (Swagger at /swagger)

# terminal 2
dotnet run --project Agrumy.Web     # http://localhost:5001
```

`Agrumy.Web/appsettings.json` -> `WebView:ApiService` must match the port `Agrumy.Api`
listens on (5000 by default, set in `Agrumy.Api/Properties/launchSettings.json`).

## Configuration

**`Agrumy.Api/appsettings.json`**

| Key | Required | Notes |
| --- | --- | --- |
| `ConnectionStrings:DefaultConnection` | yes | MySQL/MariaDB connection string |
| `JWT:SecureKey` | yes | long random secret (>= 32 chars); app throws on startup without it |
| `JWT:Issuer` | yes | e.g. `https://api.agrumy.com` |
| `JWT:Audience` | yes | e.g. `agrumy-api` |
| `Security:EnforceHttps` | no (default `true`) | `false` = serve plain HTTP, no redirect/HSTS - needed while `AgrumyDevice` firmware still calls `http://` |
| `Startup:FailFastOnDbCheck` | no (default `false`) | `true` = stop the app if the DB check / provisioning fails |
| `WebView:Enabled`, `WebView:ApiService` | no | present in `appsettings.json.example` as a documented switch for a possible combined API+UI deployment, but **not currently read by any code** - `Agrumy.Web` is what actually serves the admin UI today |

**`Agrumy.Web/appsettings.json`**

| Key | Required | Notes |
| --- | --- | --- |
| `WebView:ApiService` | yes | base URL of `Agrumy.Api` (default `http://localhost:5000`) |
| `JWT:SecureKey` | yes | **must be identical** to `Agrumy.Api`'s `JWT:SecureKey`, otherwise cookie tokens fail validation and every page redirects to login |

## Schema provisioning

`Agrumy.Api/Schema/SchemaScripts.cs` is a git-versioned copy of the database
structure (tables, stored procedures, triggers) - the goal is that the schema
every `SqlRepository` call depends on lives in source control, not only inside a
live database. On startup, `SqlRepository.EnsureSchemaAsync` checks whether the
`device` table already exists; if it doesn't, it runs every script in
`SchemaScripts.AllObjects` against the configured connection to provision an
empty database from scratch. If the table already exists, provisioning is
skipped entirely - no manual SQL setup needed for a fresh environment, and no
repeated work against one that's already set up. Whether a *failed* check stops
the app or just logs a warning is controlled by `Startup:FailFastOnDbCheck`
(see Configuration above).

## API endpoints

All routes below are under `Agrumy.Api`. `[Authorize]` requires a JWT bearer
token from `POST /api/User/Login`; `[Authorize(Roles = "admin")]` requires the
`admin` role claim in that token. Device endpoints use the separate
apiId/apiKey/apiAuth scheme described in "How it works", not JWT.

| Endpoint | Auth | Purpose |
| --- | --- | --- |
| `POST /api/User/Register` | rate-limited, no auth | Self-service registration; new users are disabled by default |
| `POST /api/User/Login` | rate-limited, no auth | Returns a JWT bearer token |
| `POST /api/User/ChangePassword` | none | Change a user's password |
| `GET /api/User/All` | admin | List every user |
| `GET /api/User/Self` | admin | Fetch the caller's own user record (see README note: currently admin-only, not "admin or the user themselves") |
| `GET /api/User` | admin | Fetch a user by id |
| `POST /api/User` | admin | Create a user |
| `PUT /api/User` | admin | Update a user |
| `DELETE /api/User` | admin | Delete a user (ids 0 and 1 are protected) |
| `GET /api/User/Roles` | admin | List available roles |
| `GET/POST/DELETE /api/User/Group[/All]` | admin | List/create/delete user groups (tenant roles) |
| `POST /api/Device/Register` | device pin | Device self-registration (see "How it works") |
| `POST /api/Device/Authenticate` | apiId/apiKey | Issues the short-lived `apiAuth` token |
| `POST /api/Device/Config` | apiId/apiAuth | Config-version-checked config sync |
| `GET /api/Device/All`, `GET /api/Device` | JWT | List devices / fetch one |
| `PUT /api/Device`, `DELETE /api/Device` | JWT admin | Update / delete a device |
| `GET/PUT /api/Device/Sensor`, `GET/PUT /api/Device/Controller` | JWT (PUT admin) | Read/update a device's sensor or controller config |
| `GET /api/Device/Type`, `TypeService`, `TypeRelay`, `TypeSensor` | JWT | Fixed lookup lists used to build device config forms |
| `GET /api/SensorData` | JWT | Sensor readings for a device over a time range |
| `POST /api/SensorData` | rate-limited | Device telemetry push |
| `DELETE /api/SensorData` | JWT admin | Bulk-delete sensor data for a device/time range |
| `GET /api/SensorData/Report` | JWT | Saved sensor data reports |

## Deployment

Each deployable project has its own GitHub Actions workflow under
`.github/workflows/`, since they deploy to separate Azure Web Apps:

| Workflow | Deploys | Azure Web App |
| --- | --- | --- |
| `master_agrumy_api.yml` | `Agrumy.Api` | existing `agrumy` app - already set up, no action needed |
| `master_agrumy_web.yml` | `Agrumy.Web` | **new** app that must be created manually - see below |

Both trigger on push to `master` (and manually via `workflow_dispatch`).

**Before `master_agrumy_web.yml` can deploy successfully for the first time:**

1. Create the new Azure Web App in the portal and put its real name in
   `master_agrumy_web.yml`'s `app-name` (currently the placeholder `agrumy-web`).
2. Set up an app registration/federated credential for it (same pattern as the
   existing `agrumy` app) and add its client-id/tenant-id/subscription-id as
   GitHub repo secrets `AZUREAPPSERVICE_CLIENTID_WEB`,
   `AZUREAPPSERVICE_TENANTID_WEB`, `AZUREAPPSERVICE_SUBSCRIPTIONID_WEB`
   (Settings > Secrets and variables > Actions).
3. On the deployed `Agrumy.Web` app, set `WebView:ApiService` to the **public**
   URL where `Agrumy.Api` is reachable after deployment (e.g.
   `https://agrumy.azurewebsites.net`) - not `http://localhost:5000`, which is
   only correct for local dev.
