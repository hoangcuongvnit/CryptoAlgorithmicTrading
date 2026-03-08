# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["global.json", "."]
# Copy project files
COPY ["src/Shared/Shared.csproj", "src/Shared/"]
COPY ["src/Services/Analyzer/Analyzer.Worker/Analyzer.Worker.csproj", "src/Services/Analyzer/Analyzer.Worker/"]

# Restore
RUN dotnet restore "src/Services/Analyzer/Analyzer.Worker/Analyzer.Worker.csproj"

# Copy all source code
COPY ["src/Shared/", "src/Shared/"]
COPY ["src/Services/Analyzer/Analyzer.Worker/", "src/Services/Analyzer/Analyzer.Worker/"]

# Build
RUN dotnet build "src/Services/Analyzer/Analyzer.Worker/Analyzer.Worker.csproj" -c Release -o /app/build

# Stage 2: Publish
FROM build AS publish
RUN dotnet publish "src/Services/Analyzer/Analyzer.Worker/Analyzer.Worker.csproj" \
    -c Release -o /app/publish

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "Analyzer.Worker.dll"]
