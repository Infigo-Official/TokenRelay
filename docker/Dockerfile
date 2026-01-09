# Use the official .NET 10.0 runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

# Use the SDK image to build the application
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Accept version argument
ARG VERSION=1.0.0-docker
ARG BUILD_DATE

# Copy project file and restore dependencies
COPY ../TokenRelay/TokenRelay.csproj TokenRelay/
RUN dotnet restore "TokenRelay/TokenRelay.csproj" /p:Version=${VERSION}

# Copy source code and build (excluding sensitive config files)
COPY ../TokenRelay/ TokenRelay/
# Remove any configuration files that might have been copied
RUN rm -f TokenRelay/tokenrelay.json
WORKDIR "/src/TokenRelay"
RUN dotnet build "TokenRelay.csproj" -c Release -o /app/build \
    /p:Version=${VERSION} \
    /p:AssemblyVersion=${VERSION}.0 \
    /p:FileVersion=${VERSION}.0

# Publish the application
FROM build AS publish
ARG VERSION=1.0.0-docker
RUN dotnet publish "TokenRelay.csproj" -c Release -o /app/publish \
    /p:UseAppHost=false \
    /p:Version=${VERSION} \
    /p:AssemblyVersion=${VERSION}.0 \
    /p:FileVersion=${VERSION}.0

# Final stage - create runtime image
FROM base AS final

# Accept version arguments for environment variables
ARG VERSION=1.0.0-docker
ARG BUILD_DATE

WORKDIR /app
COPY --from=publish /app/publish .

# Remove any configuration files that might have been published
RUN rm -f tokenrelay.json

# Create directory for file uploads and logs
RUN mkdir -p /app/uploads /app/logs

# Install curl for health checks, ca-certificates, and network tools
# iproute2 provides 'ss' for socket statistics, dnsutils provides 'nslookup' and 'dig'
RUN apt-get update \
    && apt-get install -y curl ca-certificates iproute2 dnsutils \
    && rm -rf /var/lib/apt/lists/*

# New Relic .NET agent directory (agent can be mounted or installed separately if needed)
RUN mkdir -p /usr/local/newrelic-dotnet-agent

# Set environment variables
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:80
ENV ConfigPath=/app/tokenrelay.json
ENV TOKENRELAY_VERSION=${VERSION}
ENV BUILD_DATE=${BUILD_DATE}

# New Relic APM environment variables (disabled by default)
# Set NEW_RELIC_ENABLED=true to enable APM monitoring
ENV NEW_RELIC_ENABLED=false
ENV CORECLR_ENABLE_PROFILING=0
ENV CORECLR_PROFILER={36032161-FFC0-4B61-B559-F6C5D41BAE5A}
ENV CORECLR_NEWRELIC_HOME=/usr/local/newrelic-dotnet-agent
ENV CORECLR_PROFILER_PATH=/usr/local/newrelic-dotnet-agent/libNewRelicProfiler.so
# Distributed tracing enabled by default when New Relic is active
ENV NEW_RELIC_DISTRIBUTED_TRACING_ENABLED=true

# Create directory for certificates
RUN mkdir -p /home/site/certs

# Create non-root user for security
RUN groupadd -r tokenrelay && useradd -r -g tokenrelay tokenrelay
RUN chown -R tokenrelay:tokenrelay /app /home/site/certs

# Create startup script that loads certificates, configures New Relic, and starts the application
# This script runs as root to update certificates, then drops to tokenrelay user
RUN echo '#!/bin/sh\n\
set -e\n\
\n\
# Optionally load custom certificates from persistent storage\n\
if [ -d "/home/site/certs" ] && [ -n "$(ls -A /home/site/certs/*.crt 2>/dev/null)" ]; then\n\
  echo "Loading custom certificates from /home/site/certs..."\n\
  mkdir -p /usr/local/share/ca-certificates\n\
  cp /home/site/certs/*.crt /usr/local/share/ca-certificates/ 2>/dev/null || true\n\
  update-ca-certificates || true\n\
  echo "Certificate loading completed."\n\
else\n\
  echo "No custom certificates found in /home/site/certs, skipping certificate loading."\n\
fi\n\
\n\
# Configure New Relic APM based on NEW_RELIC_ENABLED environment variable\n\
if [ "$NEW_RELIC_ENABLED" = "true" ]; then\n\
  echo "New Relic APM is ENABLED"\n\
  export CORECLR_ENABLE_PROFILING=1\n\
  \n\
  # Validate required environment variables\n\
  if [ -z "$NEW_RELIC_LICENSE_KEY" ]; then\n\
    echo "WARNING: NEW_RELIC_LICENSE_KEY is not set. New Relic will not report data."\n\
  fi\n\
  \n\
  # Log Infinite Tracing status\n\
  if [ -n "$NEW_RELIC_INFINITE_TRACING_TRACE_OBSERVER_HOST" ]; then\n\
    echo "Infinite Tracing enabled with observer: $NEW_RELIC_INFINITE_TRACING_TRACE_OBSERVER_HOST"\n\
  else\n\
    echo "Infinite Tracing not configured (standard distributed tracing will be used)"\n\
  fi\n\
  \n\
  echo "App Name: ${NEW_RELIC_APP_NAME:-TokenRelay}"\n\
else\n\
  echo "New Relic APM is DISABLED (set NEW_RELIC_ENABLED=true to enable)"\n\
  export CORECLR_ENABLE_PROFILING=0\n\
fi\n\
\n\
# Start the .NET application as tokenrelay user\n\
exec runuser -u tokenrelay -- dotnet TokenRelay.dll\n\
' > /app/entrypoint.sh && chmod +x /app/entrypoint.sh

ENTRYPOINT ["/app/entrypoint.sh"]
