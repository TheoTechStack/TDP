# syntax=docker/dockerfile:1.7
# One image serves both the Vue SPA and the .NET API (same origin: no CORS, one deployable unit).

# ---- 1. Build the Vue app --------------------------------------------------------------------
FROM node:24-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

# ---- 2. Restore, test-ready build and publish the API ---------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
# Copy project files first so `dotnet restore` is cached until dependencies change.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/DisputePortal.Api/DisputePortal.Api.csproj src/DisputePortal.Api/
RUN dotnet restore src/DisputePortal.Api/DisputePortal.Api.csproj
COPY src/ src/
RUN dotnet publish src/DisputePortal.Api/DisputePortal.Api.csproj \
      --configuration Release --no-restore --output /app

# ---- 3. Runtime: chiseled (distroless) ASP.NET image, non-root by default ------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /web/dist ./wwwroot
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "DisputePortal.Api.dll"]
