# Build context: parent of infra/ (repository root).
# Requires build-arg: DEVEXPRESS_NUGET_KEY (trial key from https://nuget.devexpress.com)

FROM node:22-bookworm-slim AS npm
WORKDIR /npm
COPY src/DxReportReview.Web/package.json .
RUN npm install --no-audit --fund=false

# Tailwind CSS build: scan .cshtml views and produce a static app.css
FROM node:22-bookworm-slim AS tailwind
WORKDIR /tw
COPY --from=npm /npm/node_modules ./node_modules
COPY src/DxReportReview.Web/tailwind.config.js .
COPY src/DxReportReview.Web/wwwroot/css/tw-input.css ./input.css
COPY src/DxReportReview.Web/Views/ ./Views/
RUN npx tailwindcss -i ./input.css -o ./app.css --minify

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG DEVEXPRESS_NUGET_KEY
WORKDIR /src

COPY nuget.config ./
COPY infra/inject-devexpress-nuget.sh /tmp/inject-devexpress-nuget.sh
RUN chmod +x /tmp/inject-devexpress-nuget.sh \
    && /tmp/inject-devexpress-nuget.sh "$DEVEXPRESS_NUGET_KEY" /src/nuget.config

COPY --from=npm /npm/node_modules ./src/DxReportReview.Web/node_modules
COPY src/DxReportReview.Web/DxReportReview.Web.csproj ./src/DxReportReview.Web/
RUN dotnet restore ./src/DxReportReview.Web/DxReportReview.Web.csproj

COPY src/ ./src/

# Inject Tailwind output into wwwroot before publish
COPY --from=tailwind /tw/app.css ./src/DxReportReview.Web/wwwroot/css/app.css

# Copy DevExpress icon fonts (referenced by bundled CSS as icons/dxicons.*)
RUN mkdir -p ./src/DxReportReview.Web/wwwroot/css/icons \
    && cp ./src/DxReportReview.Web/node_modules/devextreme-dist/css/icons/* \
          ./src/DxReportReview.Web/wwwroot/css/icons/

RUN dotnet publish ./src/DxReportReview.Web/DxReportReview.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
       libfontconfig1 \
       libfreetype6 \
       libx11-6 \
       libice6 \
       libsm6 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "DxReportReview.Web.dll"]
