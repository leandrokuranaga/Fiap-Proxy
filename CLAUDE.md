# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is FIAP Proxy, a .NET 8 API Gateway built with YARP (Yet Another Reverse Proxy). The project serves as a reverse proxy with health checks, Swagger documentation, and CORS support.

## Architecture

- **Single Project Structure**: The solution contains one main project in `src/Gateway/`
- **YARP Integration**: Uses Microsoft's YARP reverse proxy library for routing requests
- **Configuration-based Routing**: Proxy routes and clusters are defined in `appsettings.json`
- **Health Checks**: Built-in health monitoring at `/health` endpoint
- **Swagger Integration**: API documentation available via Swagger UI

## Development Commands

### Build and Test
```bash
# Create temporary solution file (required due to .slnx format)
dotnet new sln -n Fiap.Proxy
find . -name "*.csproj" -exec dotnet sln Fiap.Proxy.sln add {} \;

# Restore dependencies
dotnet restore *.sln

# Build solution
dotnet build *.sln --no-restore --configuration Release

# Run tests
dotnet test *.sln --no-build --configuration Release --verbosity normal
```

### Run Application
```bash
# Run from Gateway directory
cd src/Gateway
dotnet run

# Or build and run from root
dotnet run --project src/Gateway/Fiap.Proxy.csproj
```

### Docker Build
```bash
# Build Docker image
docker build -t fiap-proxy .

# Run container
docker run -p 8080:8080 fiap-proxy
```

## Configuration

### Reverse Proxy Configuration
The proxy configuration is in `src/Gateway/appsettings.json` under the `ReverseProxy` section:
- **Routes**: Define URL patterns and matching rules
- **Clusters**: Define destination endpoints for routing
- **Default Route**: Currently routes `/api/{**catch-all}` to `fcgCluster`

### Key Configuration Files
- `src/Gateway/appsettings.json` - Main configuration including proxy routes
- `src/Gateway/appsettings.Development.json` - Development-specific settings
- `src/Gateway/Dockerfile` - Multi-stage Docker build configuration

## CI/CD Workflows

The project uses GitHub Actions with specific naming conventions:

### Branch and Release Strategy
- **Release branches**: `release/X.0.0` (where X is incremental major version)
- **Hotfix branches**: `hotfix/X.Y.Z` (sequential patch versions)
- **Main branch**: Protected, releases are created when PRs are merged
- **Dev branch**: Development integration branch

### Available Workflows
- `validate-pr.yml` - Validates PRs to main and dev branches
- `validate-release-pr.yml` - Pre-validation for release/hotfix PRs
- `release.yml` - Creates GitHub releases with versioned artifacts
- `deploy-dockerhub.yml` - Publishes Docker images
- `deploy-aws.yml` - Deploys to AWS ECS

## Project Structure

```
src/Gateway/
├── Program.cs              # Main application entry point with middleware configuration
├── Fiap.Proxy.csproj      # Project file with dependencies
├── appsettings.json       # Production configuration including proxy routes
├── appsettings.Development.json # Development configuration
└── Dockerfile             # Multi-stage Docker build
```

## Key Dependencies

- **Microsoft.AspNetCore.App** (NET 8.0) - Core web framework
- **Yarp.ReverseProxy** (2.3.0) - Microsoft's reverse proxy library
- **Swashbuckle.AspNetCore** (6.6.2) - Swagger/OpenAPI documentation

## Development Notes

- The project uses .slnx format, requiring temporary .sln creation for CLI operations
- CORS is configured to allow all origins for development
- Health checks are available at both `/health` endpoint and root health mapping
- Swagger UI is accessible in development mode
- Authentication/Authorization middleware is configured but may need implementation