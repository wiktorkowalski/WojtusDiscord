# Build context is the REPO ROOT (see .github/workflows/build.yml: context: .),
# so all COPY paths are repo-root-relative. This lets the image bundle the
# sibling Vite project at src/Dashboard alongside the backend.

# Stage 1: Build the React/Vite dashboard
FROM node:20-alpine AS frontend-build
WORKDIR /app/frontend
COPY src/Dashboard/package*.json ./
RUN npm ci
COPY src/Dashboard/ ./
# vite.config.ts points outDir at ../DiscordEventService/wwwroot for LOCAL
# builds; that sibling path does not exist in this isolated stage, so override
# to a local dist/ here and COPY it into wwwroot in the backend stage.
RUN npm run build -- --outDir=dist

# Stage 2: Build the .NET backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/DiscordEventService/*.csproj ./
RUN dotnet restore
COPY src/DiscordEventService/ ./
# Bundle the built SPA so the .NET host serves it from wwwroot.
COPY --from=frontend-build /app/frontend/dist ./wwwroot
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false /p:EnforceCodeStyleInBuild=false /p:RunAnalyzers=false

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Install curl for healthcheck
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DiscordEventService.dll"]
