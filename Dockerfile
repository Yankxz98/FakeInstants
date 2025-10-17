# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy Server project files first
COPY ["Server.csproj", "./"]
COPY ["Server/", "./Server/"]

# Copy the referenced Blazor project
COPY ["fakeinstants.csproj", "./"]
COPY ["Components/", "./Components/"]
COPY ["Models/", "./Models/"]
COPY ["Services/", "./Services/"]
COPY ["wwwroot/", "./wwwroot/"]

# Restore dependencies
RUN dotnet restore Server.csproj

# Build the Server project
RUN dotnet build Server.csproj -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish Server.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Create non-root user for security
RUN adduser --disabled-password --gecos '' appuser && chown -R appuser:appuser /app
USER appuser

# Copy published app
COPY --from=publish --chown=appuser:appuser /app/publish .

# Set environment variables for production
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV DOTNET_RUNNING_IN_CONTAINER=true

# Expose port
EXPOSE 8080


# Set entrypoint
ENTRYPOINT ["dotnet", "Server.dll"]
