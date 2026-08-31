# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src
COPY backend/NuGet.Config ./NuGet.Config
COPY backend/SalesPlattform.Backend.csproj SalesPlattform/SalesPlattform/backend/

RUN --mount=type=secret,id=github_packages_token \
    GITHUB_PACKAGES_TOKEN="$(cat /run/secrets/github_packages_token)" && \
    NuGetPackageSourceCredentials_github="Username=github;Password=${GITHUB_PACKAGES_TOKEN}" \
    dotnet restore SalesPlattform/SalesPlattform/backend/SalesPlattform.Backend.csproj
COPY backend/ SalesPlattform/SalesPlattform/backend/
RUN dotnet publish SalesPlattform/SalesPlattform/backend/SalesPlattform.Backend.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "SalesPlattform.Backend.dll"]
