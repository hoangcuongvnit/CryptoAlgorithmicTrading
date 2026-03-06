# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["global.json", "."]
COPY ["CryptoAlgorithmicTrading.sln", "."]

# Copy project files
COPY ["src/Shared/Shared.csproj", "src/Shared/"]
COPY ["src/Services/Analyzer/Analyzer.Worker/Analyzer.Worker.csproj", "src/Services/Analyzer/Analyzer.Worker/"]

# Restore
RUN dotnet restore "CryptoAlgorithmicTrading.sln"

# Copy all source code
COPY ["src/", "src/"]

# Build
RUN dotnet build "src/Services/Analyzer/Analyzer.Worker/Analyzer.Worker.csproj" -c Release -o /app/build

# Stage 2: Publish
FROM build AS publish
RUN dotnet publish "src/Services/Analyzer/Analyzer.Worker/Analyzer.Worker.csproj" \
    -c Release -o /app/publish \
    --no-build

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=publish /app/publish .

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD curl -f http://localhost:5011/health || exit 1

ENTRYPOINT ["dotnet", "Analyzer.Worker.dll"]
