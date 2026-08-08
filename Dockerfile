# PlaceContext — multi-stage build for React, Host + ClusterHost sidecar.
FROM node:24-alpine AS frontend-build
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN PLACE_CONTEXT_FRONTEND_OUT_DIR=dist npm run build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

COPY . .
COPY --from=frontend-build /src/frontend/dist ./src/PlaceContext.Host/wwwroot/app
RUN TARGET_ARCH="$TARGETARCH"; \
  if [ "$TARGET_ARCH" = "amd64" ]; then TARGET_ARCH="x64"; fi; \
  dotnet restore src/PlaceContext.Host/PlaceContext.Host.csproj -a "$TARGET_ARCH"
RUN TARGET_ARCH="$TARGETARCH"; \
  if [ "$TARGET_ARCH" = "amd64" ]; then TARGET_ARCH="x64"; fi; \
  dotnet publish src/PlaceContext.Host/PlaceContext.Host.csproj -c Release -o /app/host -a "$TARGET_ARCH" --no-restore
RUN TARGET_ARCH="$TARGETARCH"; \
  if [ "$TARGET_ARCH" = "amd64" ]; then TARGET_ARCH="x64"; fi; \
  dotnet restore src/PlaceContext.ClusterHost/PlaceContext.ClusterHost.csproj -a "$TARGET_ARCH"
RUN TARGET_ARCH="$TARGETARCH"; \
  if [ "$TARGET_ARCH" = "amd64" ]; then TARGET_ARCH="x64"; fi; \
  dotnet publish src/PlaceContext.ClusterHost/PlaceContext.ClusterHost.csproj -c Release -o /app/cluster -a "$TARGET_ARCH" --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/host ./host/
COPY --from=build /app/cluster ./cluster/

# Host: portal + MCP on 7700
EXPOSE 7700
# ClusterHost: cluster proxy pipeline on 8081
EXPOSE 8081

# Default entrypoint runs the Host. Use CLUSTER=1 env var to run the cluster sidecar instead.
ENTRYPOINT ["sh", "-c", "if [ \"$CLUSTER\" = \"1\" ]; then exec dotnet cluster/PlaceContext.ClusterHost.dll --urls=http://+:8081; else exec dotnet host/PlaceContext.Host.dll; fi"]
