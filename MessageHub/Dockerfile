# Build stage: full SDK. Restore is its own layer so code-only edits reuse the NuGet cache.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY MessageHub.csproj ./
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet restore

COPY . .
# No --no-restore: the generated WorkerExtensions sub-project targets net8.0 and runs its
# own restore to pull the 8.0 targeting pack (this image only ships the 10.0 SDK). The
# cache mount keeps that from re-downloading on every rebuild.
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet publish MessageHub.csproj -c Release -o /app/publish

# Runtime stage: the same Functions host image Azure runs. It already sets
# ASPNETCORE_URLS=http://+:80, AzureWebJobsScriptRoot and FUNCTIONS_WORKER_RUNTIME.
FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0
ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true

COPY --from=build /app/publish /home/site/wwwroot

# Wrap the base image's startup so we can wait for the Service Bus emulator. Declaring
# ENTRYPOINT resets the inherited CMD, so the startup script has to be restated here.
COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
RUN chmod +x /usr/local/bin/docker-entrypoint.sh
ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
CMD ["/opt/startup/start_nonappservice.sh"]
