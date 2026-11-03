FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["DrivingLicenceWorkflow.Api/DrivingLicenceWorkflow.Api.csproj", "DrivingLicenceWorkflow.Api/"]
COPY ["DrivingLicenceWorkflow.Shared/DrivingLicenceWorkflow.Shared.csproj", "DrivingLicenceWorkflow.Shared/"]
RUN dotnet restore "DrivingLicenceWorkflow.Api/DrivingLicenceWorkflow.Api.csproj"

COPY . .
WORKDIR /src/DrivingLicenceWorkflow.Api
RUN dotnet publish "DrivingLicenceWorkflow.Api.csproj" --configuration Release --output /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
ENTRYPOINT ["dotnet", "DrivingLicenceWorkflow.Api.dll"]
