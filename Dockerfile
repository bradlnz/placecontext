# syntax=docker/dockerfile:1
# QEMU images are x86-64, independently of the application host architecture.
FROM --platform=linux/amd64 rust:1.98-bookworm AS qemu-agent
WORKDIR /agent
COPY src/PlaceContext.DesktopAgent/ ./
RUN cargo test --locked && \
    cargo rustc --locked --release --target x86_64-unknown-linux-gnu -- -C target-feature=+crt-static

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.403 AS build
ARG TARGETARCH
WORKDIR /src
COPY . .
COPY --from=qemu-agent /agent/target/x86_64-unknown-linux-gnu/release/pc-jev /src/dist/qemu-agent-build/x86_64-unknown-linux-gnu/release/pc-jev
RUN arch="$TARGETARCH"; [ "$arch" != amd64 ] || arch=x64; \
    dotnet restore src/PlaceContext.Host/PlaceContext.Host.csproj -a "$arch" && \
    dotnet publish src/PlaceContext.Host/PlaceContext.Host.csproj -c Release -o /app/host -a "$arch" --no-restore -p:QemuAgentPrebuilt=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14 AS runtime
LABEL org.opencontainers.image.source="https://github.com/bradlnz/placecontext" \
      org.opencontainers.image.description="Self-hosted job engine for local and distributed infrastructure" \
      org.opencontainers.image.licenses="MIT"
# QEMU uses software emulation by default; prepared desktop images are supplied by the operator.
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 qemu-system-x86 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/host ./host/
EXPOSE 7700
USER $APP_UID
ENTRYPOINT ["dotnet", "host/PlaceContext.Host.dll"]
