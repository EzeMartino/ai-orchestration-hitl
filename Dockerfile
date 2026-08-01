FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Copy project files before source files so package restore remains cacheable.
COPY backend/Orchestration.Domain/Orchestration.Domain.csproj backend/Orchestration.Domain/
COPY backend/Orchestration.Application/Orchestration.Application.csproj backend/Orchestration.Application/
COPY backend/Orchestration.Infrastructure/Orchestration.Infrastructure.csproj backend/Orchestration.Infrastructure/
COPY backend/Orchestration.ServiceDefaults/Orchestration.ServiceDefaults.csproj backend/Orchestration.ServiceDefaults/
COPY backend/Orchestration.Api/Orchestration.Api.csproj backend/Orchestration.Api/
RUN dotnet restore backend/Orchestration.Api/Orchestration.Api.csproj

COPY tools/CnvRegulation.McpServer/src/CnvRegulation.Domain/CnvRegulation.Domain.csproj tools/CnvRegulation.McpServer/src/CnvRegulation.Domain/
COPY tools/CnvRegulation.McpServer/src/CnvRegulation.Application/CnvRegulation.Application.csproj tools/CnvRegulation.McpServer/src/CnvRegulation.Application/
COPY tools/CnvRegulation.McpServer/src/CnvRegulation.Infrastructure/CnvRegulation.Infrastructure.csproj tools/CnvRegulation.McpServer/src/CnvRegulation.Infrastructure/
COPY tools/CnvRegulation.McpServer/src/CnvRegulation.McpServer/CnvRegulation.McpServer.csproj tools/CnvRegulation.McpServer/src/CnvRegulation.McpServer/
RUN dotnet restore tools/CnvRegulation.McpServer/src/CnvRegulation.McpServer/CnvRegulation.McpServer.csproj -r linux-x64

COPY backend/ backend/
COPY tools/CnvRegulation.McpServer/ tools/CnvRegulation.McpServer/
COPY python-agents/data_agent/ python-agents/data_agent/

RUN dotnet publish backend/Orchestration.Api/Orchestration.Api.csproj -c Release --no-restore -o /artifacts/api
RUN dotnet publish tools/CnvRegulation.McpServer/src/CnvRegulation.McpServer/CnvRegulation.McpServer.csproj \
    -c Release -r linux-x64 --self-contained true --no-restore \
    -p:PublishSingleFile=true -p:DebugType=None \
    -o /artifacts/mcp

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS python-staging

RUN dotnet tool install --tool-path /tools CSnakes.Stage --version 1.2.1
RUN mkdir -p /app/python/data_agent /home/app \
    && chown -R $APP_UID:$APP_UID /app/python /home/app
USER $APP_UID
ENV HOME=/home/app
COPY --chown=$APP_UID:$APP_UID python-agents/data_agent/requirements.lock /app/python/data_agent/requirements.lock
RUN /tools/setup-python --python 3.12 --venv /app/python/data_agent/.venv --pip-requirements /app/python/data_agent/requirements.lock
RUN find /home/app/.config/CSnakes -type d -path '*/python/build' -prune -exec rm -rf {} + \
    && find /home/app/.config/CSnakes -type d -path '*/python/install/lib/python3.12/test' -prune -exec rm -rf {} + \
    && find /home/app/.config/CSnakes -type f -name '*.o' -delete

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        curl \
        poppler-utils \
        tesseract-ocr \
        tesseract-ocr-eng \
        tesseract-ocr-spa \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY --from=build --chown=$APP_UID:$APP_UID /artifacts/api/ ./
COPY --from=build --chown=$APP_UID:$APP_UID /artifacts/mcp/ /app/mcp/
COPY --chown=$APP_UID:$APP_UID python-agents/data_agent/ /app/python/data_agent/
COPY --from=python-staging --chown=$APP_UID:$APP_UID /app/python/data_agent/.venv /app/python/data_agent/.venv
COPY --from=python-staging --chown=$APP_UID:$APP_UID /home/app/.config/CSnakes /home/app/.config/CSnakes

ENV ASPNETCORE_URLS=http://0.0.0.0:10000
ENV Python__Home=/app/python/data_agent
ENV PATH=/app/python/data_agent/.venv/bin:$PATH
EXPOSE 10000
RUN install -d -m 0700 -o $APP_UID -g $APP_UID /home/app/.ssh
USER $APP_UID
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD curl --fail --silent http://127.0.0.1:10000/alive || exit 1
ENTRYPOINT ["dotnet", "Orchestration.Api.dll"]
