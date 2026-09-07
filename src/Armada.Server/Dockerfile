# --- Build stage ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (layer cache) — restore individual projects, not the sln
# (sln references test projects we don't need in the image)
COPY src/Directory.Build.props src/Directory.Build.props
COPY src/Armada.Core/Armada.Core.csproj src/Armada.Core/
COPY src/Armada.Runtimes/Armada.Runtimes.csproj src/Armada.Runtimes/
COPY src/Armada.Server/Armada.Server.csproj src/Armada.Server/
RUN dotnet restore src/Armada.Server/Armada.Server.csproj

# Build
COPY src/Armada.Core/ src/Armada.Core/
COPY src/Armada.Runtimes/ src/Armada.Runtimes/
COPY src/Armada.Server/ src/Armada.Server/
RUN dotnet publish src/Armada.Server -c Release -f net10.0 -o /app --no-restore

# --- Runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl is required for the container healthcheck (probes 127.0.0.1) and for any git-only
# operations that stay on the server in local-in-container edge cases.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

# Armada default ports: 7890 (Admiral + WebSocket at /ws), 7891 (MCP)
EXPOSE 7890 7891

ENTRYPOINT ["dotnet", "Armada.Server.dll"]
