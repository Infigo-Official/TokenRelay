# TokenRelay Docker Deployment Guide

## Quick Start

### 1. Build and Run with Docker Compose (Recommended)
```bash
# Build and start the application
docker-compose up --build

# Run in background
docker-compose up -d --build

# View logs
docker-compose logs -f tokenrelay

# Stop the application
docker-compose down
```

### 2. Manual Docker Build and Run
```bash
# Build the Docker image
docker build -t tokenrelay:latest .

# Run the container
docker run -d \
  --name tokenrelay \
  -p 80:80 \
  -v ./TokenRelay/tokenrelay.json:/app/tokenrelay.json:ro \
  -v ./uploads:/app/uploads \
  tokenrelay:latest
```

## Configuration

### Required Files
- **`TokenRelay/tokenrelay.json`** - Main configuration file (mounted read-only)
- **`uploads/`** - Directory for file uploads (created automatically)

### Environment Variables
- **`ASPNETCORE_ENVIRONMENT`** - Set to `Production` for production deployment
- **`ASPNETCORE_URLS`** - HTTP binding configuration (default: `http://+:80`)

### Ports
- **Port 80** - Main HTTP port for production
- **Port 5163** - Alternative development port (mapped as backup)

## Health Monitoring

The container includes built-in health checks:
- **Health Check URL**: `http://localhost/health`
- **Check Interval**: 30 seconds
- **Timeout**: 10 seconds
- **Start Period**: 40 seconds

## Security Features

### Container Security
- **Non-root user**: Application runs as `tokenrelay` user
- **Minimal base image**: Uses official .NET 9 runtime image
- **Read-only config**: Configuration file mounted read-only
- **Upload isolation**: File uploads stored in separate volume

### Application Security
- **Token authentication**: All API endpoints require valid tokens
- **Input validation**: Comprehensive request validation
- **Stream management**: Proper cleanup of file upload resources

## Storage Volumes

### File Uploads
```bash
# Upload directory structure
./uploads/
├── documents/      # Organized by subdirectory
├── images/        # (if configured)
└── temp/          # Default upload location
```

### Logs (Optional)
```bash
# Log directory (if needed)
./logs/
└── tokenrelay.log
```

## Production Deployment

### Docker Swarm
```yaml
version: '3.8'
services:
  tokenrelay:
    image: tokenrelay:latest
    ports:
      - "80:80"
    volumes:
      - config:/app/tokenrelay.json:ro
      - uploads:/app/uploads
    deploy:
      replicas: 2
      restart_policy:
        condition: on-failure
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:80/health"]
      interval: 30s
      timeout: 10s
      retries: 3

volumes:
  config:
  uploads:
```

### Kubernetes
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: tokenrelay
spec:
  replicas: 2
  selector:
    matchLabels:
      app: tokenrelay
  template:
    metadata:
      labels:
        app: tokenrelay
    spec:
      containers:
      - name: tokenrelay
        image: tokenrelay:latest
        ports:
        - containerPort: 80
        volumeMounts:
        - name: config
          mountPath: /app/tokenrelay.json
          readOnly: true
        - name: uploads
          mountPath: /app/uploads
        livenessProbe:
          httpGet:
            path: /health/live
            port: 80
          initialDelaySeconds: 30
          periodSeconds: 30
        readinessProbe:
          httpGet:
            path: /health/ready
            port: 80
          initialDelaySeconds: 5
          periodSeconds: 10
      volumes:
      - name: config
        configMap:
          name: tokenrelay-config
      - name: uploads
        persistentVolumeClaim:
          claimName: tokenrelay-uploads
```

## Testing the Deployment

### Health Check
```bash
curl http://localhost/health
```

### File Upload Test
```bash
curl -X POST "http://localhost/function/FileStorage/httppostfile" \
  -H "TOKEN-RELAY-AUTH: your-token" \
  -F "file=@test-file.txt"
```

### Proxy Test
```bash
curl -X GET "http://localhost/proxy/get" \
  -H "TOKEN-RELAY-TARGET: httpbin" \
  -H "TOKEN-RELAY-AUTH: your-token"
```

## Troubleshooting

### Common Issues
1. **Configuration not found**: Ensure `tokenrelay.json` path is correct
2. **Permission denied**: Check volume mount permissions
3. **Health check failing**: Wait for application startup (40s start period)
4. **File upload errors**: Verify upload directory is writable

### Debug Commands
```bash
# View container logs
docker logs tokenrelay

# Access container shell
docker exec -it tokenrelay /bin/bash

# Check health status
docker inspect tokenrelay | grep Health -A 10
```

## Performance Tuning

### Resource Limits
```yaml
services:
  tokenrelay:
    deploy:
      resources:
        limits:
          memory: 512M
          cpus: '0.5'
        reservations:
          memory: 256M
          cpus: '0.25'
```

### File Upload Limits
- **In-memory files**: ≤50MB (configurable in code)
- **Container storage**: Limited by available disk space
- **Upload timeout**: 300 seconds for large files

This simplified setup removes nginx complexity while maintaining all essential features for production deployment.
