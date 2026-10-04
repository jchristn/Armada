# --- Dashboard build stage ---
FROM node:20-alpine AS dashboard-build
WORKDIR /dashboard

COPY src/Armada.Dashboard/package.json src/Armada.Dashboard/package-lock.json* ./
RUN npm ci

COPY src/Armada.Dashboard/ ./
RUN npm run build

# --- Build stage ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (layer cache) using only the projects needed for the proxy image.
COPY src/Directory.Build.props src/Directory.Build.props
COPY src/Armada.Core/Armada.Core.csproj src/Armada.Core/
COPY src/Armada.Proxy/Armada.Proxy.csproj src/Armada.Proxy/
RUN dotnet restore src/Armada.Proxy/Armada.Proxy.csproj

# Build
COPY src/Armada.Core/ src/Armada.Core/
COPY src/Armada.Proxy/ src/Armada.Proxy/
COPY src/Armada.Server/wwwroot/i18n/ src/Armada.Server/wwwroot/i18n/
COPY --from=dashboard-build /dashboard/dist/ src/Armada.Dashboard/dist/
RUN dotnet publish src/Armada.Proxy -c Release -f net10.0 -o /app --no-restore

# --- Runtime stage ---
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

# curl is required for the container healthcheck (probes 127.0.0.1:7893/proxy-api/v1/status/health).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

# Armada.Proxy default port: 7893
EXPOSE 7893

ENTRYPOINT ["dotnet", "Armada.Proxy.dll"]
