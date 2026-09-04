FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Agrumy.Shared/Agrumy.Shared.csproj Agrumy.Shared/
COPY Agrumy.Relay/Agrumy.Relay.csproj Agrumy.Relay/
RUN dotnet restore Agrumy.Relay/Agrumy.Relay.csproj

COPY Agrumy.Shared/ Agrumy.Shared/
COPY Agrumy.Relay/ Agrumy.Relay/
RUN dotnet publish Agrumy.Relay/Agrumy.Relay.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# appsettings.json (AgrumyService/Relay/ChirpStack sections - see appsettings.json.example) must
# be supplied at deploy time, same as Agrumy.Api/Agrumy.Web. Relay:LocalPort's default (5080)
# drives Kestrel directly (Program.cs ConfigureKestrel), not an ASPNETCORE_URLS env var.
EXPOSE 5080
ENTRYPOINT ["dotnet", "Agrumy.Relay.dll"]
