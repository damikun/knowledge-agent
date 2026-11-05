# docker build -f src/KnowledgeAgent.Api/Dockerfile -t knowledge-agent-api .

ARG DOTNET_VERSION=10.0

# The embeddable widget (loader and chat frame) is served by the API, so its build ships in this image.
FROM node:24-alpine AS widget
WORKDIR /web
ENV CI=true
COPY web/package.json web/package-lock.json ./
RUN --mount=type=cache,target=/root/.npm npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build:widget

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/KnowledgeAgent/KnowledgeAgent.csproj src/KnowledgeAgent/
COPY src/KnowledgeAgent.Api/KnowledgeAgent.Api.csproj src/KnowledgeAgent.Api/
COPY src/KnowledgeAgent.ServiceDefaults/KnowledgeAgent.ServiceDefaults.csproj src/KnowledgeAgent.ServiceDefaults/
RUN dotnet restore src/KnowledgeAgent.Api/KnowledgeAgent.Api.csproj

COPY src/ src/
COPY evals/ evals/
# The in-app help is built from these documents.
COPY README.md ./
COPY docs/ docs/
RUN dotnet publish src/KnowledgeAgent.Api/KnowledgeAgent.Api.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish \
        -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
EXPOSE 8080
COPY --from=build /app/publish .
COPY --from=widget /web/dist-widget ./wwwroot/widget
USER $APP_UID
ENTRYPOINT ["dotnet", "KnowledgeAgent.Api.dll"]
