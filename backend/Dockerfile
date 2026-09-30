# One image recipe for every .NET backend unit (architecture §4, §14).
# Build from the repository root, choosing the project and its entry assembly:
#   docker build -f backend/Dockerfile --build-arg PROJECT=backend/apps/sync/sync-gateway/SyncGateway.csproj \
#                --build-arg ENTRY=SyncGateway.dll -t cdss/sync-gateway .
# docker-compose.yml does this for each service.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
WORKDIR /src
COPY Directory.Build.props ./
COPY contracts/ contracts/
COPY backend/ backend/
# sharing=locked: compose builds several images in parallel and they must not write the cache at once.
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet publish "$PROJECT" -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
# Npgsql probes for the Kerberos (GSSAPI) library at start-up; the slim image does not ship it.
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
# The db-migrator looks for db/migrations next to it; other services ignore this folder.
COPY db/ db/
ARG ENTRY
ENV ENTRY_DLL=${ENTRY} \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_ENVIRONMENT=Production
USER $APP_UID
ENTRYPOINT ["sh", "-c", "exec dotnet \"$ENTRY_DLL\" \"$@\"", "--"]
