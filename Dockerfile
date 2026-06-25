# PlaceContext Host — portal + MCP (Streamable HTTP) + trigger scheduler.
# Multi-stage .NET 10 build.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore against the full solution so project references resolve.
COPY . .
RUN dotnet restore src/PlaceContext.Host/PlaceContext.Host.csproj
RUN dotnet publish src/PlaceContext.Host/PlaceContext.Host.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# Portal + MCP listen on 7700 (see appsettings / Kestrel).
EXPOSE 7700
ENV ASPNETCORE_URLS=http://+:7700
ENTRYPOINT ["dotnet", "PlaceContext.Host.dll"]
