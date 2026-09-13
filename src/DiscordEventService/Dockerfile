# Build context is the REPO ROOT (see .github/workflows/build.yml: context: .),
# so all COPY paths are repo-root-relative. This lets the image bundle the
# sibling Vite project at src/Dashboard alongside the backend.

# Multi-arch (#342): build stages are pinned to the builder's own platform
# (--platform=$BUILDPLATFORM) and cross-compile for $TARGETARCH. The .NET SDK
# does not run under QEMU (segfaults at restore, see #329), so the SDK must
# never be emulated. Only the runtime stage resolves to the target platform.

# Stage 1: Build the React/Vite dashboard (output is static files, arch-independent)
FROM --platform=$BUILDPLATFORM node:20-alpine AS frontend-build
WORKDIR /app/frontend
COPY src/Dashboard/package*.json ./
RUN npm ci
COPY src/Dashboard/ ./
# vite.config.ts points outDir at ../DiscordEventService/wwwroot for LOCAL
# builds; that sibling path does not exist in this isolated stage, so override
# to a local dist/ here and COPY it into wwwroot in the backend stage.
RUN npm run build -- --outDir=dist

# Stage 2: Build the .NET backend (runs natively, cross-compiles for TARGETARCH)
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# #193: the workflow passes the commit sha so /health can report what is deployed;
# an empty value leaves SourceRevisionId unset and the endpoint says "unknown".
ARG GIT_SHA
# BuildKit sets TARGETARCH in the global scope only; redeclare to use it in RUN.
# The SDK accepts amd64/arm64 directly (-a maps amd64 -> x64).
ARG TARGETARCH
WORKDIR /src
COPY src/DiscordEventService/*.csproj ./
RUN dotnet restore -a $TARGETARCH
COPY src/DiscordEventService/ ./
# Bundle the built SPA so the .NET host serves it from wwwroot.
COPY --from=frontend-build /app/frontend/dist ./wwwroot
RUN dotnet publish -a $TARGETARCH --no-restore -c Release -o /app/publish /p:UseAppHost=false /p:EnforceCodeStyleInBuild=false /p:RunAnalyzers=false /p:SourceRevisionId=$GIT_SHA

# Stage 3: Runtime. No --platform: resolves to the *target* platform. The apt-get
# below is the only step that runs under emulation for a non-native target.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Install curl for healthcheck
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DiscordEventService.dll"]
