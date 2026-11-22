# syntax=docker/dockerfile:1.7
# NEXUS C Foundation API — Multi-stage non-root production Dockerfile (pinned .NET 10 LTS)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props NEXUS.sln ./
COPY src/Nexus.SharedKernel/Nexus.SharedKernel.csproj src/Nexus.SharedKernel/
COPY src/Nexus.BuildingBlocks/Nexus.BuildingBlocks.csproj src/Nexus.BuildingBlocks/
COPY src/Modules/Tenancy/Nexus.Modules.Tenancy.csproj src/Modules/Tenancy/
COPY src/Modules/Organization/Nexus.Modules.Organization.csproj src/Modules/Organization/
COPY src/Modules/Identity/Nexus.Modules.Identity.csproj src/Modules/Identity/
COPY src/Modules/AccessControl/Nexus.Modules.AccessControl.csproj src/Modules/AccessControl/
COPY src/Modules/Audit/Nexus.Modules.Audit.csproj src/Modules/Audit/
COPY src/Modules/Workflow/Nexus.Modules.Workflow.csproj src/Modules/Workflow/
COPY src/Modules/Subscription/Nexus.Modules.Subscription.csproj src/Modules/Subscription/
COPY src/Modules/Configuration/Nexus.Modules.Configuration.csproj src/Modules/Configuration/
COPY src/Modules/Integration/Nexus.Modules.Integration.csproj src/Modules/Integration/
COPY src/Modules/Notification/Nexus.Modules.Notification.csproj src/Modules/Notification/
COPY src/Modules/FileStorage/Nexus.Modules.FileStorage.csproj src/Modules/FileStorage/
COPY src/Nexus.Bootstrap/Nexus.Bootstrap.csproj src/Nexus.Bootstrap/
COPY src/Nexus.Api/Nexus.Api.csproj src/Nexus.Api/

RUN dotnet restore src/Nexus.Api/Nexus.Api.csproj

COPY src/ src/
RUN dotnet publish src/Nexus.Api/Nexus.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install curl for container liveness/readiness HEALTHCHECK before dropping privileges
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Security: run as non-root built-in ASP.NET user (UID 1654)
USER app

COPY --from=build --chown=app:app /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=3 \
  CMD curl -fsS http://127.0.0.1:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "Nexus.Api.dll"]
