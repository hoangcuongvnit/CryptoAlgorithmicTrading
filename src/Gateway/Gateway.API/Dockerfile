# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["global.json", "."]

# Copy project files
COPY ["src/Shared/Shared.csproj", "src/Shared/"]
COPY ["src/Gateway/Gateway.API/Gateway.API.csproj", "src/Gateway/Gateway.API/"]

# Restore only Gateway.API project (not the full solution)
RUN dotnet restore "src/Gateway/Gateway.API/Gateway.API.csproj"

# Copy all source code
COPY ["src/Shared/", "src/Shared/"]
COPY ["src/Gateway/Gateway.API/", "src/Gateway/Gateway.API/"]

# Build
RUN dotnet build "src/Gateway/Gateway.API/Gateway.API.csproj" -c Release -o /app/build

# Stage 2: Publish
FROM build AS publish
RUN dotnet publish "src/Gateway/Gateway.API/Gateway.API.csproj" \
    -c Release -o /app/publish

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish .

# HTTP port exposure
EXPOSE 5000

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD curl -f http://localhost:5000/health || exit 1

ENTRYPOINT ["dotnet", "Gateway.API.dll"]
