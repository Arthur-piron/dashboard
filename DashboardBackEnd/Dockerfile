# Stage 1: build with the full SDK
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, on its own layer: the layer is reused as long as the .csproj is unchanged,
# so editing a .cs file does not re-download NuGet packages.
COPY DashboardBackEnd/DashboardBackEnd.csproj DashboardBackEnd/
RUN dotnet restore DashboardBackEnd/DashboardBackEnd.csproj

COPY DashboardBackEnd/ DashboardBackEnd/
RUN dotnet publish DashboardBackEnd/DashboardBackEnd.csproj -c Release -o /app --no-restore

# Stage 2: run with the much smaller runtime-only image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# The image ships a non-root `app` user ($APP_UID). Make the keys volume writable by it.
RUN mkdir -p /keys && chown $APP_UID /keys
USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "DashboardBackEnd.dll"]
