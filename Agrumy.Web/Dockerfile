# Roadmap #30 - see Agrumy.Api/Dockerfile's comments for the multi-stage/framework-dependent
# reasoning, identical here.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Agrumy.Shared/Agrumy.Shared.csproj Agrumy.Shared/
COPY Agrumy.Web/Agrumy.Web.csproj Agrumy.Web/
RUN dotnet restore Agrumy.Web/Agrumy.Web.csproj

COPY Agrumy.Shared/ Agrumy.Shared/
COPY Agrumy.Web/ Agrumy.Web/
RUN dotnet publish Agrumy.Web/Agrumy.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Roadmap #30/#79: DataProtection:KeyPath must survive a container recreate, not just a restart -
# install.sh mounts a named volume at this path (docker-compose.yml) so cookie-auth/antiforgery
# keys outlive `docker compose up` re-pulling a new image, same requirement as the bare-metal
# path's "outside bin/" rule, just expressed as a volume mount instead of a directory choice.
ENV ASPNETCORE_URLS=http://+:5001
EXPOSE 5001
ENTRYPOINT ["dotnet", "Agrumy.Web.dll"]
