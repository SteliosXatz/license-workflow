FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["DrivingLicenceWorkflow.Worker/DrivingLicenceWorkflow.Worker.csproj", "DrivingLicenceWorkflow.Worker/"]
COPY ["DrivingLicenceWorkflow.Shared/DrivingLicenceWorkflow.Shared.csproj", "DrivingLicenceWorkflow.Shared/"]
RUN dotnet restore "DrivingLicenceWorkflow.Worker/DrivingLicenceWorkflow.Worker.csproj"

COPY . .
WORKDIR /src/DrivingLicenceWorkflow.Worker
RUN dotnet publish "DrivingLicenceWorkflow.Worker.csproj" --configuration Release --output /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DrivingLicenceWorkflow.Worker.dll"]
