# Agrumy

ASP.NET Core 9 solution, split into three projects inside `agrumy.sln`:

| Project | Type | What it is |
| --- | --- | --- |
| `Agrumy.Shared` | class library | Models (`api.Models`), `Config`, `Security` (`JwtTokenProvider`, `AuthenticationProvider`). Referenced by both apps. |
| `Agrumy.Api` | Web API | Device/sensor communication + admin API (`Controllers/API`), data access (`Dal/SqlRepository`, stored procedures), MySQL/MariaDB, JWT bearer auth, Swagger, startup DB health-check / schema auto-provisioning (`Schema/SchemaScripts`). |
| `Agrumy.Web` | MVC app | Admin UI (`Controllers/View`, `Views/`, `wwwroot/`). Talks to `Agrumy.Api` **only over HTTP** (`Dal/ApiRepository` + `HttpClient` with a JWT bearer token). No direct database access. |

`db/` holds the schema dump (`agrumyDB-final.sql`, `agrumyDB-withData.sql`) and the
old deployment notes (`README.txt`). The live schema is also versioned in code at
`Agrumy.Api/Schema/SchemaScripts.cs`.

## Configuration

`appsettings.json` is git-ignored in every project (it holds real secrets). Copy the
template and fill it in:

```
cp Agrumy.Api/appsettings.json.example Agrumy.Api/appsettings.json
cp Agrumy.Web/appsettings.json.example Agrumy.Web/appsettings.json
```

**`Agrumy.Api/appsettings.json`**

| Key | Required | Notes |
| --- | --- | --- |
| `ConnectionStrings:DefaultConnection` | yes | MySQL/MariaDB connection string |
| `JWT:SecureKey` | yes | long random secret (>= 32 chars); app throws on startup without it |
| `JWT:Issuer` | yes | e.g. `https://api.agrumy.com` |
| `JWT:Audience` | yes | e.g. `agrumy-api` |
| `Startup:FailFastOnDbCheck` | no (default `false`) | `true` = stop the app if the DB check / provisioning fails |

**`Agrumy.Web/appsettings.json`**

| Key | Required | Notes |
| --- | --- | --- |
| `WebView:ApiService` | yes | base URL of `Agrumy.Api` (default `http://localhost:5000`) |
| `JWT:SecureKey` | yes | **must be identical** to `Agrumy.Api`'s `JWT:SecureKey`, otherwise cookie tokens fail validation and every page redirects to login |

## Running locally

Start the API first, then the web app (the web app calls the API on startup for login):

```
# terminal 1
dotnet run --project Agrumy.Api     # http://localhost:5000  (Swagger at /swagger)

# terminal 2
dotnet run --project Agrumy.Web     # http://localhost:5001
```

`Agrumy.Web/appsettings.json` -> `WebView:ApiService` must match the port `Agrumy.Api`
listens on (5000 by default, set in `Agrumy.Api/Properties/launchSettings.json`).

> Requires the .NET 9 runtime. If only a newer runtime is installed, run with
> `DOTNET_ROLL_FORWARD=LatestMajor` or retarget the projects.

## Build

```
dotnet build agrumy.sln
```
