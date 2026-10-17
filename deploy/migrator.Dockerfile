# Cloud Run Job / migration runner image for Grid.DbMigrator.
# Build from the repository root:
# docker build -f deploy/migrator.Dockerfile -t grid-migrator .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/Grid/ .
RUN dotnet publish src/Grid.DbMigrator/Grid.DbMigrator.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "Grid.DbMigrator.dll"]
