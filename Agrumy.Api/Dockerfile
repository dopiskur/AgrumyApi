# Framework-dependent publish (no --self-contained/-r linux-x64) is intentional here - the aspnet
# base image below already ships the matching .NET runtime; that combination is only needed for
# the bare-metal path, which has no runtime preinstalled.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

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

# appsettings.json arrives already populated by install.sh before the container starts, so the
# Setup wizard never triggers here; Urls is set via the ASPNETCORE_URLS env var instead of a JSON key.
ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000

# curl for docker-compose.yml's healthcheck; roadmap #319: a non-root, non-shell service account so a
# compromised process can't rewrite its own binary. firmware-store is created (and chowned) here so
# an empty named volume mounted over it at first run inherits this ownership instead of root's.
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --system --no-create-home --shell /usr/sbin/nologin agrumy \
    && mkdir -p /app/firmware-store \
    && chown -R agrumy:agrumy /app
USER agrumy

ENTRYPOINT ["dotnet", "Agrumy.Api.dll"]
