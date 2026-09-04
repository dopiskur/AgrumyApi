# Same multi-stage/framework-dependent reasoning as Agrumy.Api/Dockerfile.
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

# DataProtection:KeyPath must survive a container recreate, not just a restart - install.sh mounts
# a named volume at this path so cookie-auth/antiforgery keys outlive a new image pull.
ENV ASPNETCORE_URLS=http://+:5001
EXPOSE 5001
ENTRYPOINT ["dotnet", "Agrumy.Web.dll"]
