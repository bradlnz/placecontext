# syntax=docker/dockerfile:1
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY . .
RUN arch="$TARGETARCH"; [ "$arch" != amd64 ] || arch=x64; \
    dotnet restore src/PlaceContext.Host/PlaceContext.Host.csproj -a "$arch" && \
    dotnet publish src/PlaceContext.Host/PlaceContext.Host.csproj -c Release -o /app/host -a "$arch" --no-restore && \
    dotnet restore src/PlaceContext.ClusterHost/PlaceContext.ClusterHost.csproj -a "$arch" && \
    dotnet publish src/PlaceContext.ClusterHost/PlaceContext.ClusterHost.csproj -c Release -o /app/cluster -a "$arch" --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/host ./host/
COPY --from=build /app/cluster ./cluster/
EXPOSE 7700 8081
USER $APP_UID
ENTRYPOINT ["sh", "-c", "if [ \"$CLUSTER\" = \"1\" ]; then exec dotnet cluster/PlaceContext.ClusterHost.dll --urls=http://+:8081; else exec dotnet host/PlaceContext.Host.dll; fi"]
