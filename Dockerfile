# Use the .NET 10 SDK to build the application
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -o /app/out

# Use the ASP.NET 10 runtime to run the application
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# mysqldump/mysql client tools for the backup/restore feature —
# industry-standard way to back up and restore a MySQL database
# wholesale, rather than a hand-rolled export/import mechanism.
RUN apt-get update && apt-get install -y --no-install-recommends default-mysql-client \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/out .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "portal-backend-dotnet.dll"]
