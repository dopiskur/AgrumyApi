# Agrumy

> Agrumy core (API, firmware, enclosures) is free and open source under the
> [Apache 2.0 license](LICENSE.txt). The mobile apps are proprietary.
> If you use Agrumy, I'd genuinely love to hear about it — open an issue or
> drop me a line.

Agrumy is a small multi-tenant backend + admin UI for managing IoT devices that
monitor and control greenhouse/citrus micro-climate - temperature, humidity, soil
moisture, light, CO2, water level - and drive relays (ventilation, heating,
irrigation, lighting) off configurable thresholds and time intervals. It is the
server side of a system whose device firmware lives in the separate `AgrumyFirmware`
repository; this repository is the API those devices talk to, plus the MVC admin
UI operators use to manage devices, users and tenants.

**.NET 10 SDK required.**

`agrumy.sln` splits into these projects:

| Project | Type | What it is |
| --- | --- | --- |
| `Agrumy.Shared` | class library | Models (`api.Models`), `Config`, `Security` (`JwtTokenProvider`, `AuthenticationProvider`). Referenced by both apps. |
| `Agrumy.Dal` | class library | Data-access model: `AgrumyDbContext`, EF entities (`api.Dal.Entities`), provider selection (`DbProviderKind`, `DbOptionsFactory`). No stored procedures - every query is LINQ. |
| `Agrumy.Api` | Web API | Device/sensor communication + admin API (`Controllers/API`), the `IRepository` implementation (`Dal/EfRepository`, EF Core over `Agrumy.Dal`), MySQL/MariaDB **or** PostgreSQL, JWT bearer auth, Swagger, startup DB health-check + schema creation on an empty database. |
| `Agrumy.Web` | MVC app | Admin UI (`Controllers/View`, `Views/`, `wwwroot/`). Talks to `Agrumy.Api` **only over HTTP** (`Dal/ApiRepository` + `HttpClient` with a JWT bearer token). No direct database access. |

`db/` holds a historical schema dump (`agrumyDB-final.sql`, `agrumyDB-withData.sql`)
and old deployment notes (`README.txt`), kept for reference only - the schema is now
owned by the `AgrumyDbContext` model. `db/migrations/` holds a few hand-written SQL
patches applied to the pre-existing legacy database, unrelated to EF.

## How it works

1. **Registration.** A device calls `POST /api/Device/Register` with the owning
   user's email, that user's `DevicePin` (a 6-char alphanumeric code the user
   generates from My Profile / `POST /api/User/DevicePin`, valid 24 hours and
   reusable for as many devices as needed in that window), and its MAC
   address. The pin has to match and be unexpired; on success the API creates
   the device row (if it doesn't exist yet) under that user's tenant and hands
   back an `ApiId`/`ApiKey` pair plus its current config.
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
   water pump, lighting) run in the device firmware (`AgrumyFirmware` repo) against
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
| `ConnectionStrings:DefaultConnection` | yes | Connection string for the engine selected by `Database:Provider` |
| `Database:Provider` | no (default `mysql`) | `mysql`/`mariadb` (Pomelo) or `postgres`/`postgresql` (Npgsql). Also overridable via the `AGRUMY_DB_PROVIDER` env var. |
| `JWT:SecureKey` | yes | long random secret (>= 32 chars); app throws on startup without it |
| `JWT:Issuer` | yes | e.g. `https://api.agrumy.com` |
| `JWT:Audience` | yes | e.g. `agrumy-api` |
| `Security:EnforceHttps` | no (default `true`) | `false` = serve plain HTTP, no redirect/HSTS - needed while `AgrumyFirmware` firmware still calls `http://` |
| `Startup:FailFastOnDbCheck` | no (default `false`) | `true` = stop the app if the DB check / provisioning fails |
| `Notifications:Email:*` | no (default off) | SMTP alert email (roadmap #6). `Enabled` + `Host` + `FromAddress` are the minimum; `Port`/`UseStartTls`/`Username`/`Password`/`FromName` optional. Disabled or incomplete = channel skipped, not an error. |
| `Notifications:Push:*` | no (default off) | FCM push channel - **prepared but inert**. Stays skipped until the Android app registers device tokens and the OAuth step in `FcmPushNotificationChannel` is wired. Leave `Enabled=false`. |
| `Firmware:LocalPath` | no (default `firmware-store`) | roadmap #94 - directory the **Local** firmware repository stores/serves `.bin` files from (`GET /api/Firmware/Download/{file}`). Relative = under the content root; must be writable by the service user. |
| `Firmware:GitHubRepository` | no (default `dopiskur/AgrumyFirmware`) | `owner/name` whose GitHub Releases feed the catalog - only seeds the `serverConfig` row, the live value is edited on the Server Settings page |
| `Firmware:GitHubToken` | no | optional GitHub API token; public repositories need none |
| `Notifications:OfflineCheckIntervalMinutes` | no (default `5`) | roadmap #40 - how often `OfflineAlertBackgroundService` sweeps every device for a newly-offline one and notifies its tenant's admins via whatever `Notifications:*` channels are configured above. |
| `WebView:Enabled`, `WebView:ApiService` | no | present in `appsettings.json.example` as a documented switch for a possible combined API+UI deployment, but **not currently read by any code** - `Agrumy.Web` is what actually serves the admin UI today |

**`Agrumy.Web/appsettings.json`**

| Key | Required | Notes |
| --- | --- | --- |
| `WebView:ApiService` | yes | base URL of `Agrumy.Api` (default `http://localhost:5000`) |
| `JWT:SecureKey` | yes | **must be identical** to `Agrumy.Api`'s `JWT:SecureKey`, otherwise cookie tokens fail validation and every page redirects to login |

## Database & schema provisioning

The data-access layer is EF Core (`Agrumy.Dal/AgrumyDbContext` + `Dal/EfRepository`),
LINQ only - no stored procedures. It runs on **MySQL/MariaDB** (Pomelo) or
**PostgreSQL** (Npgsql), chosen by `Database:Provider` (see Configuration).

On startup `EfRepository.EnsureSchemaAsync` calls `EnsureCreatedAsync()`: an empty
database gets every table straight from the `AgrumyDbContext` model, and a database
that already has tables is left untouched - no manual SQL setup for a fresh
environment, no repeated work against an existing one. Whether a *failed* check
stops the app or just logs a warning is controlled by `Startup:FailFastOnDbCheck`
(see Configuration above).

`EnsureSchemaAsync` also seeds rows, never just tables (roadmap #91, continuing the
#66 role-catalog pattern): the four `deviceType*` lookup tables get the product's
fixed catalog (device types, service types, relay types, sensor types) if empty,
and a completely empty `user` table gets exactly one bootstrap **Global Admin**
account (`TenantID=0`, `PwdHash`/`PwdSalt` left `NULL` on purpose). A `NULL`
password hash means nothing can log in as that account yet -
`POST /api/User/BootstrapSetPassword` is the one-shot call that gives it a real
password (Agrumy.Web shows a "set password" screen instead of the login form while
`GET /api/User/BootstrapPending` is true). None of this runs against a database
that already has any users - existing installs, including api.agrumy.com, are
unaffected.

### Schema evolution

Pre-beta there are no EF migrations. The project has no real users or data to
preserve across schema changes, so the model is the single source of truth and a
fresh database is built with `EnsureCreatedAsync()`. Changing the schema during
development means recreating the dev database (drop it, let startup re-create it).
Migrations are planned to return for the beta once the schema settles - see the
roadmap. `db/migrations/` holds unrelated hand-written SQL patches for the legacy
database and is not part of this.

### Provider notes

- **EF Core is held at 9.0.x** (`Microsoft.EntityFrameworkCore*`, Pomelo 9.0.0,
  Npgsql 9.0.4). The runtime still targets net10.0; the pin is only because the
  official Pomelo MySQL provider has no EF Core 10 build yet.
- **Legacy foreign keys.** The model configures primary keys, the unique indexes
  the app depends on (`email_UNIQUE`, `Username_UNIQUE`, `ApiID_UNIQUE`,
  `Name_UNIQUE`) and the legacy `NO ACTION` FKs; navigation properties are not
  mapped - `EfRepository` joins explicitly in LINQ. A legacy database keeps
  whatever FKs it already had (`EnsureCreatedAsync` never touches a non-empty DB).
- **PostgreSQL:** `NpgsqlCompat` opts into pre-6.0 timestamp behaviour
  (`DateTime` -> `timestamp without time zone`, any `DateTimeKind`) because the
  schema stores naive local datetimes throughout. Legacy MySQL `0000-00-00`
  values must be cleaned before such data can be loaded into PostgreSQL; for
  MySQL itself, add `AllowZeroDateTime=True;ConvertZeroDateTime=True` to the
  connection string if the data contains any.
- **TimescaleDB (roadmap #14, tiered-hybrid deployment).** The provider choice
  *is* the deployment-size choice: MariaDB/MySQL is the small-deployment tier
  and stays an ordinary table, no code path runs for it. Choosing PostgreSQL
  is choosing the large-deployment tier - on every startup,
  `EfRepository.EnsureTimescaleHypertableAsync` runs `CREATE EXTENSION IF NOT
  EXISTS timescaledb` and converts `sensorData` into a hypertable partitioned
  on `DateCreated` (widening its PK to `(IDSensorData, DateCreated)`, which
  TimescaleDB requires). This needs no application code branching - EF Core
  LINQ queries against `sensorData` run unchanged on both providers, Timescale
  just partitions/prunes transparently underneath. A self-hosted Postgres
  without the extension installed isn't a startup failure: the `CREATE
  EXTENSION` call is caught, a warning is logged, and `sensorData` is left as
  a plain table, same as the MariaDB tier. Verified against
  `timescale/timescaledb:latest-pg17` - a bare `postgres:17` container (as
  used by the dev/test fixture above) exercises the same warn-and-skip path.

## API endpoints

All routes below are under `Agrumy.Api`. `[Authorize]` requires a JWT bearer
token from `POST /api/User/Login`; `[Authorize(Roles = "admin")]` requires the
`admin` role claim in that token. Device endpoints use the separate
apiId/apiKey/apiAuth scheme described in "How it works", not JWT.

| Endpoint | Auth | Purpose |
| --- | --- | --- |
| `POST /api/User/Register` | rate-limited, no auth | Self-service registration; new users are disabled by default |
| `POST /api/User/Login` | rate-limited, no auth | Returns a JWT bearer token |
| `GET /api/User/BootstrapPending` | no auth | Roadmap #91: true while the fresh-install bootstrap Global Admin still has no password |
| `POST /api/User/BootstrapSetPassword` | rate-limited, no auth | Roadmap #91: one-shot - sets the bootstrap Global Admin's password, then this always returns 403 |
| `POST /api/User/ChangePassword` | rate-limited, JWT (self) | Change the caller's own password (identity from JWT, old password required) |
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

CI (`.github/workflows/build.yml`) builds and tests on every push to `master`;
there is no automated deployment. The old Azure Web App workflow
(`master_agrumy.yml`) was removed after its credentials went stale and every
run failed at the Azure login step - restore it from git history if Azure
deployment ever comes back.

The test/alpha environment is deployed manually: self-contained `linux-x64`
`dotnet publish` of `Agrumy.Api` and `Agrumy.Web`, copied to the server over
SSH, run as systemd services behind a reverse proxy. The per-machine
`appsettings.json` (connection string, JWT keys, `Urls`) is git-ignored and
lives only on the server.

## License

Copyright 2016-2026 Domagoj Piškur

Licensed under the Apache License, Version 2.0 (the "License"); you may not
use this project except in compliance with the License. You may obtain a copy
of the License at http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software distributed
under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR
CONDITIONS OF ANY KIND, either express or implied.

The Android and iOS applications (AgrumyAndroid, AgrumyiOS) are separate,
proprietary projects and are not covered by this license.
