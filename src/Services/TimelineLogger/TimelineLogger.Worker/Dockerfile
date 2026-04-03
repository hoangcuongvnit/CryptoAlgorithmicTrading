# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["global.json", "."]

COPY ["src/Shared/Shared.csproj", "src/Shared/"]
COPY ["src/Services/TimelineLogger/TimelineLogger.Worker/TimelineLogger.Worker.csproj", "src/Services/TimelineLogger/TimelineLogger.Worker/"]

RUN dotnet restore "src/Services/TimelineLogger/TimelineLogger.Worker/TimelineLogger.Worker.csproj"

COPY ["src/Shared/", "src/Shared/"]
COPY ["src/Services/TimelineLogger/TimelineLogger.Worker/", "src/Services/TimelineLogger/TimelineLogger.Worker/"]

RUN dotnet publish "src/Services/TimelineLogger/TimelineLogger.Worker/TimelineLogger.Worker.csproj" \
    -c Release -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

EXPOSE 5096

HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD curl -f http://localhost:5096/health || exit 1

ENTRYPOINT ["dotnet", "TimelineLogger.Worker.dll"]
