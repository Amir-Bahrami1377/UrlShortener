# Build context must be the repo root (project references cross into ../Shortener.Application etc.):
#   docker build -f src/Shortener.Api/Dockerfile -t shortener-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props .
COPY src/Shortener.Domain/Shortener.Domain.csproj src/Shortener.Domain/
COPY src/Shortener.Application/Shortener.Application.csproj src/Shortener.Application/
COPY src/Shortener.Infrastructure/Shortener.Infrastructure.csproj src/Shortener.Infrastructure/
COPY src/Shortener.Api/Shortener.Api.csproj src/Shortener.Api/
RUN dotnet restore src/Shortener.Api/Shortener.Api.csproj

COPY src/Shortener.Domain/ src/Shortener.Domain/
COPY src/Shortener.Application/ src/Shortener.Application/
COPY src/Shortener.Infrastructure/ src/Shortener.Infrastructure/
COPY src/Shortener.Api/ src/Shortener.Api/
RUN dotnet publish src/Shortener.Api/Shortener.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

# §M8.6 — file storage is a mounted Volume, not baked into the image; the non-root "app" user
# (built into the aspnet base image since .NET 8) needs write access to it.
RUN mkdir -p /data/filestorage /data/logs
VOLUME ["/data/filestorage"]
ENV FileStorage__RootPath=/data/filestorage
ENV Serilog__LogDirectory=/data/logs

COPY --from=build /app .
RUN chown -R app:app /data
USER app

ENTRYPOINT ["dotnet", "Shortener.Api.dll"]
