# Build context must be the repo root (project references cross into ../Shortener.Infrastructure etc.):
#   docker build -f src/Shortener.PublicWeb/Dockerfile -t shortener-publicweb .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props .
COPY src/Shortener.Domain/Shortener.Domain.csproj src/Shortener.Domain/
COPY src/Shortener.Application/Shortener.Application.csproj src/Shortener.Application/
COPY src/Shortener.Infrastructure/Shortener.Infrastructure.csproj src/Shortener.Infrastructure/
COPY src/Shortener.PublicWeb/Shortener.PublicWeb.csproj src/Shortener.PublicWeb/
RUN dotnet restore src/Shortener.PublicWeb/Shortener.PublicWeb.csproj

COPY src/Shortener.Domain/ src/Shortener.Domain/
COPY src/Shortener.Application/ src/Shortener.Application/
COPY src/Shortener.Infrastructure/ src/Shortener.Infrastructure/
COPY src/Shortener.PublicWeb/ src/Shortener.PublicWeb/
RUN dotnet publish src/Shortener.PublicWeb/Shortener.PublicWeb.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

RUN mkdir -p /data/filestorage /data/logs
VOLUME ["/data/filestorage"]
ENV FileStorage__RootPath=/data/filestorage
ENV Serilog__LogDirectory=/data/logs

COPY --from=build /app .
RUN chown -R app:app /data
USER app

ENTRYPOINT ["dotnet", "Shortener.PublicWeb.dll"]
