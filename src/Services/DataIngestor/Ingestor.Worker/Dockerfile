# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["global.json", "."]

# Copy project files
COPY ["src/Shared/Shared.csproj", "src/Shared/"]
COPY ["src/Services/DataIngestor/Ingestor.Worker/Ingestor.Worker.csproj", "src/Services/DataIngestor/Ingestor.Worker/"]

# Restore
RUN dotnet restore "src/Services/DataIngestor/Ingestor.Worker/Ingestor.Worker.csproj"

# Copy all source code
COPY ["src/", "src/"]

# Publish
RUN dotnet publish "src/Services/DataIngestor/Ingestor.Worker/Ingestor.Worker.csproj" -c Release -o /app/publish

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app/publish .

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD curl -f http://localhost:5010/health || exit 1

ENTRYPOINT ["dotnet", "Ingestor.Worker.dll"]
