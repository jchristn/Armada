# --- Dashboard build stage ---
# Base images are pinned to exact patch versions; bump them deliberately (see .github/workflows/security.yml).
FROM node:22.23.3-alpine3.23 AS dashboard-build
WORKDIR /dashboard

COPY src/Armada.Dashboard/package.json src/Armada.Dashboard/package-lock.json* ./
RUN npm ci

COPY src/Armada.Dashboard/ ./
RUN npm run build

# --- Build stage ---
FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble AS build
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
FROM mcr.microsoft.com/dotnet/runtime:10.0.12-noble AS runtime
WORKDIR /app
COPY --from=build /app .

# Run as the image's built-in non-root user (APP_UID, 1654); the working directory stays writable for its data.
RUN chown -R $APP_UID:$APP_UID /app
USER $APP_UID

# Armada.Proxy default port: 7893
EXPOSE 7893

ENTRYPOINT ["dotnet", "Armada.Proxy.dll"]
