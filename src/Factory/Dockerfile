# syntax=docker/dockerfile:1
# Build context is the repository root: this service shares projects with the others.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first so the restore layer is cached independently of source changes.
COPY Directory.Build.props ./
COPY src/BananaFarm.Contracts/BananaFarm.Contracts.csproj src/BananaFarm.Contracts/
COPY src/BananaFarm.Messaging/BananaFarm.Messaging.csproj src/BananaFarm.Messaging/
COPY src/Factory/Factory.csproj src/Factory/
RUN dotnet restore src/Factory/Factory.csproj

COPY src/ src/
RUN dotnet publish src/Factory/Factory.csproj --configuration Release --output /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl is only here to give the container a healthcheck.
RUN apt-get update \
 && apt-get install --no-install-recommends --yes curl \
 && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_gcServer=1

COPY --from=build /app ./

EXPOSE 8080

HEALTHCHECK --interval=10s --timeout=5s --start-period=40s --retries=10 \
    CMD curl --fail --silent http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Factory.dll"]
