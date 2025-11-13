from the main root directory: docker build -f docker/Dockerfile -t tokenrelay:1.0.2 .

# Docker Deployment Guide

⚠️ **SECURITY NOTICE**: The Docker image does NOT include `tokenrelay.json` for security reasons. You must provide configuration via volume mount or environment variables.

This directory contains Docker configuration files for deploying TokenRelay.

## 🔒 Configuration Security

For security, the Docker image excludes sensitive configuration files. You have two options:

### Option 1: File-Based Configuration (Recommended for Development)
1. Copy the template: `cp config/tokenrelay.template.json config/tokenrelay.json`
2. Edit `config/tokenrelay.json` with your actual credentials and API endpoints
3. The file will be mounted as a volume to the container

### Option 2: Environment-Based Configuration (Recommended for Production)
1. Set `TOKENRELAY_CONFIG_MODE=env` in your `.env` file
2. Provide complete configuration in `TOKENRELAY_CONFIG_JSON` environment variable
3. No configuration files are stored on disk

## Quick Start

1. **Copy environment template:**
   ```bash
   cp .env.template .env
   ```

2. **Edit configuration:**
   - For file-based config: Edit `config/tokenrelay.json`
   - For environment-based config: Set `TOKENRELAY_CONFIG_MODE=env` in `.env` and provide JSON in `TOKENRELAY_CONFIG_JSON`

3. **Build and run:**
   ```bash
   docker-compose up -d
   ```

4. **With Nginx reverse proxy:**
   ```bash
   docker-compose --profile with-nginx up -d
   ```

   **Note:** The OAuth2 test server is included by default for testing OAuth integration. To run without it, remove the oauth-server service from docker-compose.yml.

## Configuration Options

### File-Based Configuration (Default)
- Configuration is loaded from `config/tokenrelay.json`
- Mount your configuration file to `/app/tokenrelay.json` in the container
- Supports hot-reload when file changes

### Environment-Based Configuration
- Set `TOKENRELAY_CONFIG_MODE=env` in environment
- Provide complete configuration as JSON in `TOKENRELAY_CONFIG_JSON`
- Useful for container orchestration platforms (Kubernetes, Docker Swarm)

### Example Environment Configuration
```bash
export TOKENRELAY_CONFIG_MODE=env
export TOKENRELAY_CONFIG_JSON='{
  "proxy": {
    "auth": {
      "tokens": ["your-secure-token"],
      "encryptionKey": "your-32-character-encryption-key"
    },
    "targets": {
      "api": {
        "endpoint": "https://api.example.com",
        "description": "Example API"
      }
    }
  }
}'
```

## Services

### TokenRelay
- **Port:** 80 (internal), 5163 (external)
- **Health Check:** `http://localhost:5163/health`
- **Swagger UI:** `http://localhost:5163` (development mode)

### OAuth2 Server (For Testing Only - Included by Default)
- **Port:** 8080 (external)
- **Image:** Custom Python Flask mock server
- **Health Check:** `http://localhost:8080/health`
- **Token Endpoint:** `http://localhost:8080/v1/oauth/tokens`
- **Purpose:** Testing TokenRelay OAuth integration

**Test Credentials:**
- **Client ID:** `test_client_1`
- **Client Secret:** `test_secret_1`
- **Username:** `test_user`
- **Password:** `test_password`
- **Supported Grant Types:** password, client_credentials, refresh_token

**Pre-configured TokenRelay Targets:**
- `oauth-test-api` - Password grant flow
- `oauth-test-client-creds` - Client credentials grant flow

### Nginx (Optional - Profile: with-nginx)
- **Port:** 8080 (external) - Note: Conflicts with OAuth server port, use 8081 if running both
- **Features:** Rate limiting, security headers, file upload optimization
- **Access:** `http://localhost:8080`

## Volumes

- `config/`: Configuration files
- `uploads/`: File upload storage
- `logs/`: Application logs

## Security Considerations

1. **Use encrypted tokens:** Use the encryption tool to encrypt your API tokens
2. **Secure the encryption key:** Store the encryption key securely
3. **Network security:** Use proper firewall rules and network policies
4. **Regular updates:** Keep the Docker images updated

## Testing OAuth Integration

The OAuth server is included by default for testing. You can test the OAuth integration:

### 1. Test OAuth Token Acquisition

Request a token directly from the OAuth server:

```bash
# Password Grant
curl -X POST http://localhost:8080/v1/oauth/tokens \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -u "test_client_1:test_secret_1" \
  -d "grant_type=password&username=test_user&password=test_password&scope=read write"

# Client Credentials Grant
curl -X POST http://localhost:8080/v1/oauth/tokens \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -u "test_client_1:test_secret_1" \
  -d "grant_type=client_credentials&scope=read"
```

### 2. Test TokenRelay OAuth Proxy

Make a request through TokenRelay to the OAuth-protected target:

```bash
# Through TokenRelay (it will automatically acquire OAuth token)
curl -X GET http://localhost:5163/proxy/oauth-test-api/users \
  -H "TOKEN-RELAY-TARGET: oauth-test-api" \
  -H "Authorization: Bearer your-tokenrelay-auth-token"
```

### 3. Monitor OAuth Token Caching

Check TokenRelay logs to see OAuth token caching in action:

```bash
docker-compose logs -f tokenrelay | grep -i oauth
```

You should see:
- First request: Token acquisition from OAuth server
- Subsequent requests: Token retrieved from cache
- Expired token: Automatic refresh

## Troubleshooting

### View logs:
```bash
# TokenRelay logs
docker-compose logs -f tokenrelay

# OAuth server logs
docker-compose logs -f oauth-server

# Database logs
docker-compose logs -f oauth-db

# All services
docker-compose logs -f
```

### Check health:
```bash
# TokenRelay health
curl http://localhost:5163/health

# OAuth server health
curl http://localhost:8080/health

# PostgreSQL health
docker-compose exec oauth-db pg_isready -U oauth2
```

### Rebuild images:
```bash
docker-compose build --no-cache
```

### Reset volumes:
```bash
# Reset all services
docker-compose down -v
docker-compose up -d
```

### OAuth Server Issues

**Problem:** OAuth server fails to start
```bash
# Check if database is ready
docker-compose logs oauth-db

# Check OAuth server logs
docker-compose logs oauth-server
```

**Problem:** TokenRelay can't connect to OAuth server
```bash
# Verify network connectivity
docker-compose exec tokenrelay ping oauth-server

# Check if OAuth server is accessible
docker-compose exec tokenrelay curl http://oauth-server:8080/health
```

**Problem:** Token acquisition fails
```bash
# Verify OAuth server credentials in config/tokenrelay.json
# Ensure client_id, client_secret, username, password are correct

# Test OAuth server directly
curl -v http://localhost:8080/v1/oauth/tokens \
  -u "test_client_1:test_secret_1" \
  -d "grant_type=client_credentials"
```
