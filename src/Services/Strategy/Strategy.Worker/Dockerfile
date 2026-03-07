# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["global.json", "."]
COPY ["CryptoAlgorithmicTrading.sln", "."]

# Copy project files
COPY ["src/Shared/Shared.csproj", "src/Shared/"]
COPY ["src/Services/Strategy/Strategy.Worker/Strategy.Worker.csproj", "src/Services/Strategy/Strategy.Worker/"]

# Restore
RUN dotnet restore "CryptoAlgorithmicTrading.sln"

# Copy all source code
COPY ["src/", "src/"]

# Build
RUN dotnet build "src/Services/Strategy/Strategy.Worker/Strategy.Worker.csproj" -c Release -o /app/build

# Stage 2: Publish
FROM build AS publish
RUN dotnet publish "src/Services/Strategy/Strategy.Worker/Strategy.Worker.csproj" \
    -c Release -o /app/publish \
    --no-build

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=publish /app/publish .

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD curl -f http://localhost:5012/health || exit 1

ENTRYPOINT ["dotnet", "Strategy.Worker.dll"]
