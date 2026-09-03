# Roadmap #30: multi-stage build - the SDK image compiles from source (no pre-built artifact
# needed, unlike install.sh's bare-metal path which downloads release.yml's self-contained
# tarball), the runtime image just runs the result. Framework-dependent publish (no
# --self-contained/-r linux-x64 here) on purpose - the aspnet base image below already ships the
# matching .NET runtime, so a self-contained publish would only duplicate it inside the image for
# no benefit; that combination is specifically for the bare-metal path, which has no runtime
# preinstalled.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files first so `dotnet restore` layers are cached across builds that only change
# .cs/.cshtml source - same reasoning as any other Docker/NuGet layering pattern.
COPY Agrumy.Shared/Agrumy.Shared.csproj Agrumy.Shared/
COPY Agrumy.Dal/Agrumy.Dal.csproj Agrumy.Dal/
COPY Agrumy.Api/Agrumy.Api.csproj Agrumy.Api/
RUN dotnet restore Agrumy.Api/Agrumy.Api.csproj

COPY Agrumy.Shared/ Agrumy.Shared/
COPY Agrumy.Dal/ Agrumy.Dal/
COPY Agrumy.Api/ Agrumy.Api/
RUN dotnet publish Agrumy.Api/Agrumy.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Roadmap #30: container deployment's appsettings.json arrives ALREADY populated (install.sh
# writes it from the compose file's environment section before `docker compose up` ever starts
# this container) - unlike the bare-metal path, the DB-setup wizard (Agrumy.Api/Setup/) never
# triggers here because ConnectionStrings__DefaultConnection is always already set via env vars.
# Urls is set the same way (CLAUDE.md's bare-metal note about needing an explicit "Urls" key
# applies here too - no .service file's ASPNETCORE_URLS to fall back on inside a container
# either), but as an env var instead of a JSON key.
ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000
ENTRYPOINT ["dotnet", "Agrumy.Api.dll"]
