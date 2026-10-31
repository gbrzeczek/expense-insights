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

# --- Embedding model (plan 07) -------------------------------------------------
# multilingual-e5-small (MIT licence), the int8 ONNX export from the model's own repository, pinned
# by revision and SHA-256 (the same as E5Model.cs; a test checks they match). Built into the image so
# the server needs no download at run time; a layer of ~135 MB that only changes with the model.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS model
ARG E5_URL=https://huggingface.co/intfloat/multilingual-e5-small/resolve/614241f622f53c4eeff9890bdc4f31cfecc418b3/onnx
WORKDIR /models
RUN curl -fsSL -o model_qint8_avx512_vnni.onnx "$E5_URL/model_qint8_avx512_vnni.onnx" \
 && curl -fsSL -o tokenizer.json "$E5_URL/tokenizer.json" \
 && printf '%s\n' \
      "dd476dd0c2514e9b9be83aeb3853fac0763e0bdf4a71645407587d77c48a2d88  model_qint8_avx512_vnni.onnx" \
      "0b44a9d7b51c3c62626640cda0e2c2f70fdacdc25bbbd68038369d14ebdf4c39  tokenizer.json" \
    | sha256sum -c -

# --- Runtime ------------------------------------------------------------------
# Chiseled (distroless, non-root) with ICU and tzdata for pl-PL culture support.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=backend /app ./
COPY --from=model /models ./models
ENV ASPNETCORE_HTTP_PORTS=8080 \
    TZ=Europe/Warsaw \
    Classification__Embeddings__ModelDirectory=/app/models \
    Classification__Embeddings__Download=false
EXPOSE 8080
ENTRYPOINT ["dotnet", "ExpenseInsights.Api.dll"]
