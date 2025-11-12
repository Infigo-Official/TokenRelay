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

### Nginx (Optional)
- **Port:** 8080 (external)
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

## Troubleshooting

### View logs:
```bash
docker-compose logs -f tokenrelay
```

### Check health:
```bash
curl http://localhost:5163/health
```

### Rebuild image:
```bash
docker-compose build --no-cache
```

### Reset volumes:
```bash
docker-compose down -v
docker-compose up -d
```
