# Use the official .NET 8.0 runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

# Use the SDK image to build the application
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Accept version argument
ARG VERSION=1.0.0-docker
ARG BUILD_DATE

# Copy project file and restore dependencies
COPY ../TokenRelay/TokenRelay.csproj TokenRelay/
RUN dotnet restore "TokenRelay/TokenRelay.csproj"

# Copy source code and build (excluding sensitive config files)
COPY ../TokenRelay/ TokenRelay/
# Remove any configuration files that might have been copied
RUN rm -f TokenRelay/tokenrelay.json
WORKDIR "/src/TokenRelay"
RUN dotnet build "TokenRelay.csproj" -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish "TokenRelay.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Final stage - create runtime image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Remove any configuration files that might have been published
RUN rm -f tokenrelay.json

# Create directory for file uploads and logs
RUN mkdir -p /app/uploads /app/logs

# Install curl for health checks and ca-certificates for certificate management
RUN apt-get update && apt-get install -y curl ca-certificates && rm -rf /var/lib/apt/lists/*

# Set environment variables
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:80
ENV ConfigPath=/app/tokenrelay.json
ENV TOKENRELAY_VERSION=${VERSION}
ENV BUILD_DATE=${BUILD_DATE}

# Create directory for certificates
RUN mkdir -p /home/site/certs

# Create non-root user for security
RUN groupadd -r tokenrelay && useradd -r -g tokenrelay tokenrelay
RUN chown -R tokenrelay:tokenrelay /app /home/site/certs

# Create startup script that loads certificates and starts the application
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
# Start the .NET application as tokenrelay user\n\
exec runuser -u tokenrelay -- dotnet TokenRelay.dll\n\
' > /app/entrypoint.sh && chmod +x /app/entrypoint.sh

ENTRYPOINT ["/app/entrypoint.sh"]
