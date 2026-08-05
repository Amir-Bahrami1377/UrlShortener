# Build context must be the repo root (project references cross into ../Shortener.Infrastructure etc.):
#   docker build -f src/Shortener.Admin/Dockerfile -t shortener-admin .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props .
COPY src/Shortener.Domain/Shortener.Domain.csproj src/Shortener.Domain/
COPY src/Shortener.Application/Shortener.Application.csproj src/Shortener.Application/
COPY src/Shortener.Infrastructure/Shortener.Infrastructure.csproj src/Shortener.Infrastructure/
COPY src/Shortener.Admin/Shortener.Admin.csproj src/Shortener.Admin/
RUN dotnet restore src/Shortener.Admin/Shortener.Admin.csproj

COPY src/Shortener.Domain/ src/Shortener.Domain/
COPY src/Shortener.Application/ src/Shortener.Application/
COPY src/Shortener.Infrastructure/ src/Shortener.Infrastructure/
COPY src/Shortener.Admin/ src/Shortener.Admin/
RUN dotnet publish src/Shortener.Admin/Shortener.Admin.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

# §M8.6 — "the Admin panel must never be published to the internet, internal network or behind a
# VPN only" is an infrastructure/network decision (reverse proxy, firewall, VPN gateway), not
# something this image can enforce on its own — flagged here and in the runbook, not silently assumed.
RUN mkdir -p /data/filestorage /data/logs
VOLUME ["/data/filestorage"]
ENV FileStorage__RootPath=/data/filestorage
ENV Serilog__LogDirectory=/data/logs

COPY --from=build /app .
RUN chown -R app:app /data
USER app

ENTRYPOINT ["dotnet", "Shortener.Admin.dll"]
