# PlaceContext Host — portal + MCP (Streamable HTTP) + trigger scheduler.
# Multi-stage .NET 10 build. Cross-arch aware: the SDK stage always runs on the BUILD machine's
# native platform and .NET cross-publishes for TARGETARCH, so `docker build --platform
# linux/arm64` (Mac/ARM-fleet packages) doesn't crawl through qemu emulation.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# Restore against the full solution so project references resolve.
COPY . .
RUN dotnet restore src/PlaceContext.Host/PlaceContext.Host.csproj -a $TARGETARCH
RUN dotnet publish src/PlaceContext.Host/PlaceContext.Host.csproj -c Release -o /app -a $TARGETARCH --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./

# Portal + MCP listen on 7700 (see appsettings / Kestrel).
EXPOSE 7700
ENV ASPNETCORE_URLS=http://+:7700
ENTRYPOINT ["dotnet", "PlaceContext.Host.dll"]
