# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["global.json", "."]

# Copy project files
COPY ["src/Services/HouseKeeper/HouseKeeper.Worker/HouseKeeper.Worker.csproj", "src/Services/HouseKeeper/HouseKeeper.Worker/"]

# Restore
RUN dotnet restore "src/Services/HouseKeeper/HouseKeeper.Worker/HouseKeeper.Worker.csproj"

# Copy all source code
COPY ["src/", "src/"]

# Publish
RUN dotnet publish "src/Services/HouseKeeper/HouseKeeper.Worker/HouseKeeper.Worker.csproj" -c Release -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "HouseKeeper.Worker.dll"]
