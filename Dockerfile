# syntax=docker/dockerfile:1
# Production image: the Angular build is served from the API's wwwroot (one origin, no CORS).

# --- Frontend -----------------------------------------------------------------
FROM node:24-alpine AS frontend
WORKDIR /src
COPY src/frontend/package.json src/frontend/package-lock.json ./
RUN --mount=type=cache,target=/root/.npm npm ci --ignore-scripts
COPY src/frontend/ ./
RUN npx ng build --configuration production

# --- Backend ------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src
COPY src/backend/global.json src/backend/Directory.Build.props src/backend/Directory.Packages.props src/backend/.editorconfig ./
COPY src/backend/ExpenseInsights.Api/ExpenseInsights.Api.csproj ExpenseInsights.Api/
COPY src/backend/ExpenseInsights.Core/ExpenseInsights.Core.csproj ExpenseInsights.Core/
COPY src/backend/ExpenseInsights.Infrastructure/ExpenseInsights.Infrastructure.csproj ExpenseInsights.Infrastructure/
COPY src/backend/ExpenseInsights.Import.Ing/ExpenseInsights.Import.Ing.csproj ExpenseInsights.Import.Ing/
COPY src/backend/ExpenseInsights.Classification/ExpenseInsights.Classification.csproj ExpenseInsights.Classification/
# Packages stay in this layer (no cache mount): with a CI layer cache the restore layer can be
# reused while a cache mount starts empty, which breaks `publish --no-restore`.
RUN dotnet restore ExpenseInsights.Api/ExpenseInsights.Api.csproj
COPY src/backend/ ./
# Copied before publish so the static assets manifest (MapStaticAssets) includes the SPA files
# and pre-compresses them.
COPY --from=frontend /src/dist/expense-insights/browser/ ExpenseInsights.Api/wwwroot/
RUN dotnet publish ExpenseInsights.Api/ExpenseInsights.Api.csproj -c Release -o /app --no-restore

# --- Runtime ------------------------------------------------------------------
# Chiseled (distroless, non-root) with ICU and tzdata for pl-PL culture support.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=backend /app ./
ENV ASPNETCORE_HTTP_PORTS=8080 \
    TZ=Europe/Warsaw
EXPOSE 8080
ENTRYPOINT ["dotnet", "ExpenseInsights.Api.dll"]
