# Runs the back-end integration tests inside the compose network (db + mailpit reachable by name).
FROM mcr.microsoft.com/dotnet/sdk:10.0
WORKDIR /src

COPY DashboardBackEnd/DashboardBackEnd.csproj DashboardBackEnd/
COPY DashboardBackEnd.Tests/DashboardBackEnd.Tests.csproj DashboardBackEnd.Tests/
RUN dotnet restore DashboardBackEnd.Tests/DashboardBackEnd.Tests.csproj

COPY DashboardBackEnd/ DashboardBackEnd/
COPY DashboardBackEnd.Tests/ DashboardBackEnd.Tests/
RUN dotnet build DashboardBackEnd.Tests/DashboardBackEnd.Tests.csproj -c Release --no-restore

ENTRYPOINT ["dotnet", "test", "DashboardBackEnd.Tests/DashboardBackEnd.Tests.csproj", "-c", "Release", "--no-build", "--logger", "console;verbosity=normal"]
