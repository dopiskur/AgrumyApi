FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Agrumy.Shared/Agrumy.Shared.csproj Agrumy.Shared/
COPY Agrumy.Gateway/Agrumy.Gateway.csproj Agrumy.Gateway/
RUN dotnet restore Agrumy.Gateway/Agrumy.Gateway.csproj

COPY Agrumy.Shared/ Agrumy.Shared/
COPY Agrumy.Gateway/ Agrumy.Gateway/
RUN dotnet publish Agrumy.Gateway/Agrumy.Gateway.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# appsettings.json (AgrumyService/Gateway/ChirpStack sections - see appsettings.json.example) must
# be supplied at deploy time, same as Agrumy.Api/Agrumy.Web. Gateway:LocalPort's default (5080)
# drives Kestrel directly (Program.cs ConfigureKestrel), not an ASPNETCORE_URLS env var.
EXPOSE 5080
ENTRYPOINT ["dotnet", "Agrumy.Gateway.dll"]
