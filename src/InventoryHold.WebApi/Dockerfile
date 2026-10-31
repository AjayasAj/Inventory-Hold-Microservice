# Build context is the repository root (see docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from project files only, so the package layer is cached across source edits.
COPY InventoryHold.sln ./
COPY src/InventoryHold.Contracts/InventoryHold.Contracts.csproj src/InventoryHold.Contracts/
COPY src/InventoryHold.Domain/InventoryHold.Domain.csproj src/InventoryHold.Domain/
COPY src/InventoryHold.Infrastructure/InventoryHold.Infrastructure.csproj src/InventoryHold.Infrastructure/
COPY src/InventoryHold.WebApi/InventoryHold.WebApi.csproj src/InventoryHold.WebApi/
COPY src/InventoryHold.UnitTests/InventoryHold.UnitTests.csproj src/InventoryHold.UnitTests/
RUN dotnet restore InventoryHold.sln

COPY src/ src/

# The image is only produced if the unit tests pass.
RUN dotnet test src/InventoryHold.UnitTests/InventoryHold.UnitTests.csproj -c Release --no-restore
RUN dotnet publish src/InventoryHold.WebApi/InventoryHold.WebApi.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "InventoryHold.WebApi.dll"]
