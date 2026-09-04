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

## Mission

Everyone should have the right to grow their own food. In practice, most people
today have neither the knowledge nor the time to keep a food garden alive -
that's the problem Agrumy exists to lower the barrier to, not just a target
market. The same software serves both ends without compromise: an agricultural
cooperative or commercial nursery gets real multi-tenancy and granular RBAC
(`api.Security.RoleNames`) to manage many operators from one backend, while a
single hobbyist on the default tenant never has to see any of that complexity -
just their own greenhouse.

FarmBot is the closest comparison for "I don't have the knowledge or time" -
and it's a real, shipping product that solves that problem well. But it does
so at $2,000-$8,000, which excludes exactly the people who'd benefit most from
it. Agrumy targets the same problem without that price floor: free, open-source
software running on hardware priced like AliExpress components, not a
purpose-built appliance.

**.NET 10 SDK required.**

`agrumy.sln` splits into these projects:

| Project | Type | What it is |
| --- | --- | --- |
| `Agrumy.Shared` | class library | Models (`api.Models`), `Config`, `Security` (`JwtTokenProvider`, `AuthenticationProvider`). Referenced by both apps. |
| `Agrumy.Dal` | class library | Data-access model: `AgrumyDbContext`, EF entities (`api.Dal.Entities`), provider selection (`DbProviderKind`, `DbOptionsFactory`). No stored procedures - every query is LINQ. |
| `Agrumy.Api` | Web API | Device/sensor communication + admin API (`Controllers/API`), the `IRepository` implementation (`Dal/EfRepository`, EF Core over `Agrumy.Dal`), MySQL/MariaDB **or** PostgreSQL, JWT bearer auth, Swagger, startup DB health-check + schema creation on an empty database. |
| `Agrumy.Web` | MVC app | Admin UI (`Controllers/View`, `Views/`, `wwwroot/`). Talks to `Agrumy.Api` **only over HTTP** (`Dal/ApiRepository` + `HttpClient` with a JWT bearer token). No direct database access. |
| `Agrumy.Api.Tests` | test project | Integration tests that run the real EF Core stack against both providers in parallel (`AGRUMY_TEST_MYSQL`/`AGRUMY_TEST_POSTGRES` connection strings), plus unit tests for the alert/schedule/hysteresis evaluators. |

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
5. **Heartbeat and commands.** Every config-poll doubles as a heartbeat -
   `Uptime`/`Rssi`/`FreeHeap`/`FirmwareVersion`/`Board` land in `deviceDiagnostic`
   and drive the Fleet page's online/offline status. The same poll response
   carries any command an operator queued for that device (`POST
   /api/DeviceCommand` - Reboot / ForceOTA / ForceConfigSync); the device acts on
   it and reports back via `POST /api/Device/Command/Ack`.

## Why not just use Home Assistant / ESPHome / ThingsBoard?

Honestly, for a single greenhouse with off-the-shelf sensors, **Home Assistant or
ESPHome is almost certainly the better choice** - mature device support, a real
automation/rule engine, a huge community, and no backend to run or firmware to
maintain yourself. **ThingsBoard** covers similar ground to Agrumy (multi-tenant
device fleets, telemetry, dashboards) far more completely, with an actual rule
engine and support for many transport protocols.

Agrumy's rule engine (roadmap #21) is deliberately narrow, not general-purpose:
each relay function (ventilation/light/heating/water-pump) on a zone holds a set
of Threshold/Interval/Schedule rules, any one of which turning "on" wins (OR
only - no AND/composite conditions across rules or across functions). Threshold's
metric and direction are fixed per function, not user-configurable. If you need
"if X and not Y between 6pm and 10pm, do Z," you're still writing C# to add it,
not YAML.

Where Agrumy makes sense: you're building (or already run) your own firmware for
a specific vertical - here, citrus/greenhouse irrigation and micro-climate - and
want a small, self-hosted, multi-tenant backend you fully own and can shape
around that one domain, rather than adapting a general-purpose platform's data
model to fit it. It's a starting point for a narrow, vertical-specific product,
not a general home-automation hub.

## How Agrumy differs from Mycodo / OpenSprinkler

Mycodo and OpenSprinkler are mature, single-install controllers - point one of
them at one greenhouse or sprinkler system and they do that job well. Agrumy
targets a different case: a fleet of installations under one backend. Neither
is "better" here; they're built for different scale.

- **Multi-tenant with granular RBAC.** Neither Mycodo nor OpenSprinkler has a
  tenant concept - a single install is a single install. Agrumy's `TenantID`
  scoping and composable roles (`api.Security.RoleNames`) let one backend host
  many independent operators, each seeing only their own devices/users/data.
- **A Unit/Zone hierarchy with a live dashboard.** `GET /api/DeviceUnit/Dashboard`
  rolls up status across units and zones of devices - neither competitor has a
  comparable way to organize and view a *fleet*, since they're built around
  managing one controller/installation at a time.
- **Fleet-wide commands with server-side fan-out.** `POST /api/DeviceCommand`
  targets a unit or zone and the server resolves that into the actual set of
  devices to deliver Reboot/ForceOTA/ForceConfigSync to on their next poll -
  not something either competitor's single-controller model needs to solve.
- **Firmware-side native unit tests in CI.** The relay/hysteresis/schedule/
  safety-limit logic in `AgrumyFirmware` (`test/test_native_*`) runs as native
  unit tests on every push, independent of real hardware - a level of
  formalized firmware testing not evident in either competitor's repo.
- **A configurable firmware distribution source.** Roadmap #94's GitHub/Local/
  Custom firmware source, with offline-USB-repository support and lazy fetch,
  has no equivalent in either project.

None of this makes Agrumy a drop-in replacement for either - if you're running
one greenhouse with off-the-shelf sensors, Mycodo or OpenSprinkler (or Home
Assistant/ESPHome, see above) is very likely the simpler, more mature choice.
Agrumy is for when "one greenhouse" becomes "many, under one roof."

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
| `Security:KnownProxies` | no (default empty = loopback only) | roadmap #84 - comma-separated IPs of reverse proxies trusted to set `X-Forwarded-For`/`X-Forwarded-Proto` for the rate limiter. Never point this at an untrusted/public address - that lets a client spoof its own IP and dodge rate limiting. |
| `Startup:FailFastOnDbCheck` | no (default `false`) | `true` = stop the app if the DB check / provisioning fails |
| `ServerConfig:Reload` | no (default `false`) | `true` = overwrite the DB `serverConfig` row's hysteresis fields from `ServerConfig:Hysteresis` below on every startup, discarding admin-UI edits. Seed-once is the normal mode; flip to `true` only to force a reset, then back to `false`. |
| `ServerConfig:Hysteresis:*` | no | roadmap #10 - dead-zone margins (`WaterLevel`/`Temperature`/`Humidity`/`Light`) new devices are seeded with |
| `ServerConfig:BatteryLowThreshold`, `BatteryLowHysteresis` | no (default `20.0`/`5.0`) | roadmap #12 - percent. `LowBatteryAlertEvaluator` alerts at/below the threshold, rearms only once the reading climbs back to threshold+hysteresis |
| `ServerConfig:EventDedupeMinutes` | no (default `10`) | roadmap #28 - a device repeating the same event type within this window is dropped, not stored |
| `ServerConfig:ActivationResendCooldownMinutes` | no (default `10`) | roadmap #24 - minimum minutes between "resend activation email" requests |
| `ServerConfig:AllowSelfServiceTenantCreation` | no (default `false`) | roadmap #64 - `true` lets a registration for an unknown tenant name create that tenant (min. 6 chars) with the registrant as its admin |
| `Notifications:Email:*` | no (default off) | SMTP alert email (roadmap #6). `Enabled` + `Host` + `FromAddress` are the minimum; `Port`/`UseStartTls`/`Username`/`Password`/`FromName` optional. Disabled or incomplete = channel skipped, not an error. |
| `Notifications:Push:*` | no (default off) | FCM push channel - **prepared but inert**. Stays skipped until the Android app registers device tokens and the OAuth step in `FcmPushNotificationChannel` is wired. Leave `Enabled=false`. |
| `Notifications:Webhook:*` | no (default off) | roadmap #214 - generic HTTP POST channel for notifying an external system. `Enabled` + `Url` (must be `https://`) are the minimum; optional `Secret` adds an `X-Agrumy-Signature` HMAC-SHA256 header the receiver can verify. `Url` goes through the same `SsrfGuard` as firmware fetches before every send. |
| `Notifications:OfflineCheckIntervalMinutes` | no (default `5`) | roadmap #40 - how often `OfflineAlertBackgroundService` sweeps every device for a newly-offline one and notifies its tenant's admins via whatever `Notifications:*` channels are configured above |
| `Notifications:BatteryCheckIntervalMinutes` | no (default `30`) | roadmap #12 - how often `LowBatteryAlertEvaluator` sweeps every device's latest battery telemetry; longer than the offline interval by default since a battery drains over hours/days, not seconds |
| `Firmware:LocalPath` | no (default `firmware-store`) | roadmap #94 - directory the **Local** firmware repository stores/serves `.bin` files from (`GET /api/Firmware/Download/{file}`). Relative = under the content root; must be writable by the service user. |
| `Firmware:GitHubRepository` | no (default `dopiskur/AgrumyFirmware`) | `owner/name` whose GitHub Releases feed the catalog - only seeds the `serverConfig` row, the live value is edited on the Server Settings page |
| `Firmware:GitHubToken` | no | optional GitHub API token; public repositories need none |
| `WebView:Enabled`, `WebView:ApiService` | no | present in `appsettings.json.example` as a documented switch for a possible combined API+UI deployment, but **not currently read by any code** - `Agrumy.Web` is what actually serves the admin UI today |

**`Agrumy.Web/appsettings.json`**

| Key | Required | Notes |
| --- | --- | --- |
| `WebView:ApiService` | yes | base URL of `Agrumy.Api` (default `http://localhost:5000`) |
| `JWT:SecureKey`, `JWT:Issuer`, `JWT:Audience` | yes | **must be identical** to `Agrumy.Api`'s values (roadmap #48), otherwise cookie tokens fail validation and every page redirects to login |
| `DataProtection:KeyPath` | no (default: a `dataprotection-keys` dir next to, not inside, the app folder) | roadmap #79 - where cookie-auth/antiforgery encryption keys persist. Must sit outside anything a redeploy wipes (not `bin/`), and be read/write for the account the service actually runs as. |

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

**Versioning (roadmap #215):** every route below is implicitly API version `1.0`
(`ApiControllerBase` carries `[ApiVersion("1.0")]`, inherited by every controller) served at the
same unversioned URL it always has - `AssumeDefaultVersionWhenUnspecified` means device firmware
and `Agrumy.Web`'s Refit client keep working with zero version info in the request. A future
breaking change should land as a new `[ApiVersion("2.0")]` controller under its own `api/v2/...`
route rather than altering an existing v1 action, so old and new clients both keep working. An
explicit version can be sent via `?api-version=1.0`, an `X-Api-Version` header, or a `v{version}`
URL segment; an unsupported version gets `400` with an `api-supported-versions` response header.

All routes below are under `Agrumy.Api`. `[Authorize]` requires a JWT bearer
token from `POST /api/User/Login`. Most write/admin endpoints require one of
the **composable roles** in `api.Security.RoleNames` (roadmap #66/#91) rather
than a single `admin` flag - `UserManagers`/`DeviceManagers` match any of
`LegacyAdmin`, `GlobalAdmin`, `GlobalUser`/`GlobalDevice`, `TenantAdmin`,
`TenantUser`/`TenantDevice`; `Admins` matches `LegacyAdmin`/`GlobalAdmin`/
`TenantAdmin`; a handful of server-wide actions (below) require the literal
`admin` role, i.e. Global admin only, because they cross every tenant at once.
Device endpoints use the separate apiId/apiKey/apiAuth scheme described in
"How it works", not JWT.

**User** (`UserApiController`, `api/User`)

| Endpoint | Auth | Purpose |
| --- | --- | --- |
| `POST /api/User/Register` | rate-limited, no auth | Self-service registration; account is inactive until `Activate` |
| `GET /api/User/Activate` | rate-limited, no auth | Confirms the emailed activation token |
| `POST /api/User/ResendActivation` | rate-limited, no auth | Re-sends the activation email |
| `POST /api/User/Login` | rate-limited, no auth | Returns a JWT access token + refresh token |
| `POST /api/User/RefreshToken` | rate-limited, no auth | Silent renewal - rotates the refresh token, detects reuse of an already-rotated one |
| `POST /api/User/RevokeRefreshToken` | rate-limited, no auth | Logout - invalidates one refresh token |
| `GET /api/User/BootstrapPending` | no auth | Roadmap #91: true while the fresh-install bootstrap Global Admin still has no password |
| `POST /api/User/BootstrapSetPassword` | rate-limited, no auth | Roadmap #91: one-shot - sets the bootstrap Global Admin's password, then this always returns 403 |
| `POST /api/User/ChangePassword` | rate-limited, JWT (self) | Change the caller's own password (old password required) |
| `PUT /api/User/Profile` | JWT (self) | Roadmap #71: update the caller's own display name / IANA time zone |
| `POST /api/User/DevicePin` | JWT (self) | Roadmap #70/#76: issue/reuse the caller's still-valid 6-char device-registration PIN (24h expiry, multi-use within that window) |
| `GET /api/User/All`, `GET /api/User/Self`, `GET /api/User` | JWT | List users / fetch own record / fetch a user by id |
| `POST /api/User`, `PUT /api/User` | UserManagers | Create / update a user |
| `DELETE /api/User` | UserManagers | Delete a user (ids 0 and 1 are protected) |
| `GET /api/User/Roles`, `GET /api/User/UserRoles` | UserManagers | List available roles / a user's assigned roles |
| `PUT /api/User/UserRoles` | Admins | Set a user's roles |
| `GET /api/User/Group/All`, `GET /api/User/Group` | UserManagers | List / fetch tenant user groups |
| `POST /api/User/Group`, `DELETE /api/User/Group` | Admins | Create / delete a user group |

**Device** (`DeviceApiController`, `api/Device`)

| Endpoint | Auth | Purpose |
| --- | --- | --- |
| `POST /api/Device/Register` | device pin | Device self-registration (see "How it works") |
| `POST /api/Device/Authenticate` | apiId/apiKey | Issues the short-lived `apiAuth` token |
| `POST /api/Device/Config` | apiId/apiAuth | Config-version-checked config sync; also carries any queued `DeviceCommand` and diagnostic heartbeat fields |
| `POST /api/Device/Event`, `POST /api/Device/Command/Ack` | apiId/apiAuth | Device pushes an event / acknowledges a command |
| `GET /api/Device/All`, `GET /api/Device` | JWT | List devices / fetch one |
| `GET /api/Device/Fleet` | JWT | Roadmap #7/#8: every device's latest diagnostic + online/offline status |
| `GET /api/Device/Events` | JWT | A device's event log |
| `PUT /api/Device`, `DELETE /api/Device` | DeviceManagers | Update / delete a device |
| `GET/PUT /api/Device/Sensor`, `GET/PUT /api/Device/Controller` | JWT (PUT DeviceManagers) | Read/update a device's sensor or controller config |
| `POST /api/Device/FirmwareUpdate`, `DELETE /api/Device/FirmwareUpdate` | DeviceManagers | Queue / cancel an OTA update for one device |
| `GET /api/Device/Type`, `TypeService`, `TypeRelay`, `TypeSensor` | JWT | Fixed lookup lists used to build device config forms |

**DeviceCommand** (`DeviceCommandApiController`, roadmap #34)

| Endpoint | Auth | Purpose |
| --- | --- | --- |
| `POST /api/DeviceCommand` | DeviceManagers | Queue Reboot / ForceOTA / ForceConfigSync for one or more devices; delivered on the device's next config poll |

**DeviceUnit** (`DeviceUnitApiController`, roadmap #81/#82, `api/DeviceUnit`)

| Endpoint | Auth | Purpose |
| --- | --- | --- |
| `GET /api/DeviceUnit/All`, `GET /api/DeviceUnit` | JWT | List units / fetch one |
| `POST /api/DeviceUnit`, `PUT /api/DeviceUnit`, `DELETE /api/DeviceUnit` | DeviceManagers | Create / update / delete a unit |
| `GET /api/DeviceUnit/Zone` | JWT | Zones under a unit |
| `POST/PUT/DELETE /api/DeviceUnit/Zone` | DeviceManagers | Create / update / delete a zone |
| `GET /api/DeviceUnit/Unassigned` | DeviceManagers | Devices not yet placed in any zone |
| `POST /api/DeviceUnit/Assign`, `POST /api/DeviceUnit/Unassign` | DeviceManagers | Place / remove a device from a zone |
| `GET /api/DeviceUnit/Zone/Rule` | JWT | Roadmap #21: a zone's automation rules (Threshold/Interval/Schedule per relay function) |
| `POST/DELETE /api/DeviceUnit/Zone/Rule` | DeviceManagers | Add / remove one rule - several rules per relay function are OR'd together, there is no whole-list replace |
| `GET /api/DeviceUnit/Dashboard`, `Dashboard/Zones`, `Dashboard/Zone` | JWT | Roadmap #116: hierarchical dashboard rollups (per-unit, per-zone-list, per-zone) |

**Firmware** (`FirmwareApiController`, roadmap #94/#93, `api/Firmware`)

| Endpoint | Auth | Purpose |
| --- | --- | --- |
| `GET /api/Firmware` | DeviceManagers | List catalog entries, optionally filtered by board |
| `POST /api/Firmware/Sync` | Global admin | Refresh the catalog from the configured source (GitHub / Custom repository) |
| `POST /api/Firmware/Import` | Global admin | Pull one release's files into the **Local** repository |
| `POST /api/Firmware/Upload` | Global admin | Manually add a `.bin` to the Local repository |
| `DELETE /api/Firmware` | Global admin | Remove a catalog entry |
| `GET /api/Firmware/Manifest`, `GET /api/Firmware/Fetch` | DeviceManagers | Offline-USB-repository preparation (`tools/offline-repo/*`) |
| `GET /api/Firmware/Download/{fileName}` | no auth | Serves a `.bin` from the Local store (device OTA download) |

**`Download` is anonymous by design** (roadmap #245) - a device's OTA fetch is a bare HTTP GET
with no auth headers, the same as a public GitHub release asset. This is only safe because
`fileName` is unpredictable in the sense that matters: it must match `FirmwareVersion`'s
release convention (`agrumy-{board}-v{semver}.bin`), i.e. a caller must already know a real
board name and an actually-released semver - not a guessable sequential id - and that
information is itself public (the catalog and GitHub Releases already list it to anyone with
API access). Rate-limited via the `device-data` policy (60 req/min/IP) against bulk-download
abuse. If this ever needs to be tightened further, short-lived signed URLs are the natural
next step - not required today.

**ServerConfig** (`ServerConfigApiController`, `api/ServerConfig`)

| Endpoint | Auth | Purpose |
| --- | --- | --- |
| `GET /api/ServerConfig`, `PUT /api/ServerConfig` | admin | Global defaults (hysteresis, alert thresholds, firmware source, etc.) - Global admin only, applies across every tenant |
| `GET /api/ServerConfig/Public` | no auth | The subset of server config safe to expose pre-login (e.g. registration open/closed) |

**DataMaintenance** (`DataMaintenanceApiController`, roadmap #126, `api/DataMaintenance`)

| Endpoint | Auth | Purpose |
| --- | --- | --- |
| `GET /api/DataMaintenance/Provider` | admin | Whether the DB is MySQL/MariaDB (affects whether "shrink files on disk" is offered) |
| `POST /api/DataMaintenance/Optimize`, `POST /api/DataMaintenance/Purge` | admin | Queue an old-data optimize/purge job on `BackgroundJobQueue`; returns 202 immediately, runs async |

**SensorData** (`SensorDataApiController`, `api/SensorData`)

| Endpoint | Auth | Purpose |
| --- | --- | --- |
| `GET /api/SensorData` | JWT | Sensor readings for a device over a time range |
| `POST /api/SensorData` | rate-limited | Device telemetry push (includes `Battery` since roadmap #12) |
| `DELETE /api/SensorData` | JWT admin | Bulk-delete sensor data for a device/time range |

## Self-hosted install (roadmap #30)

For anyone standing up their own instance (not the maintainer's own alpha
deployment - see "Deployment" below for that):

```
curl -fsSL https://raw.githubusercontent.com/dopiskur/AgrumyService/master/install.sh | bash
```

`install.sh` asks two independent questions - a deployment preset (Simple/Small:
MariaDB, no Redis; Large/Scaled: PostgreSQL+TimescaleDB, Redis; or Custom: pick
each option individually) and a deployment mode:

- **Container** (Docker or Podman) - builds and runs `docker-compose.yml`
  (Small preset) or `docker-compose.large.yml` (Large/Scaled preset;
  `--profile redis` toggles the Redis container within it). `appsettings.json`
  is never touched here - config arrives as environment variables in the
  compose file instead, already fully populated before either container starts.
- **Bare-metal/standalone** - downloads the latest tagged release (see
  `.github/workflows/release.yml`) as self-contained `linux-x64` binaries, no
  .NET runtime needed on the target. Installs them as systemd services
  (`deploy/agrumy-api.service.template`, `deploy/agrumy-web.service.template`) behind nginx or Apache
  (`deploy/nginx.conf.template` / `apache.conf.template`) with a certbot TLS
  cert. This path never asks about the database up front - `Agrumy.Api` boots
  into a minimal setup wizard the first time `appsettings.json` has no
  `ConnectionStrings:DefaultConnection` (`Agrumy.Api/Setup/SetupWizard.cs`);
  saving a connection there restarts the service, and the existing bootstrap
  Global Admin wizard (roadmap #91) takes over from there unchanged.

Safe to re-run - each step checks whether it's already done before acting, so
re-running to add a component later (e.g. turn on Redis) doesn't repeat
completed steps or overwrite existing secrets.

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
