# Stage 1: Base for building the .NET API
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS base-api

# Restore inputs first, so a source-only change reuses the cached restore layer.
COPY ImmichFrame.sln Directory.Build.props Directory.Packages.props /source/
COPY ImmichFrame.Core/ImmichFrame.Core.csproj /source/ImmichFrame.Core/
COPY ImmichFrame.WebApi/ImmichFrame.WebApi.csproj /source/ImmichFrame.WebApi/
WORKDIR /source/ImmichFrame.WebApi

# Restore dependencies with cache. Declared before the build args, which differ per platform and
# per release, so one restore layer serves every platform and version.
RUN dotnet restore

COPY . /source

# Set default architecture argument for multi-arch builds
ARG TARGETARCH
ARG VERSION

ENV APP_VERSION=$VERSION

# Stage 2: Publish .NET API
FROM base-api AS publish-api

ARG TARGETARCH
ARG VERSION
ENV APP_VERSION=$VERSION

# Publish the app for the target architecture
RUN dotnet publish --runtime linux-${TARGETARCH} --self-contained false -p:AssemblyVersion=$VERSION -o /app

# Stage 3: Build frontend with Node.js
# The output is static files, so build it once on the build machine rather than once per target
# platform under emulation.
FROM --platform=$BUILDPLATFORM node:22-alpine AS build-node

USER node
WORKDIR /app
COPY --chown=node:node ./immichFrame.Web/package*.json ./

# Cache npm dependencies; ci installs exactly what the lockfile pins.
RUN npm ci
COPY --chown=node:node ./immichFrame.Web ./
RUN npm run build

# Stage 4: Final production stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS final

WORKDIR /app

# Optional: für Diagnostik bei Laufzeit
ARG VERSION
ENV APP_VERSION=$VERSION

# Copy .NET API and frontend assets
COPY --from=publish-api /app ./
COPY --from=build-node /app/build ./wwwroot

# Set non-privileged user
ARG APP_UID=1000
USER $APP_UID

ENTRYPOINT ["dotnet", "ImmichFrame.WebApi.dll"]
