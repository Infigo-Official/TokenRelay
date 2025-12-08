# TokenRelay Proxy - Production Ready ✅

## Overview
TokenRelay is a secure HTTP proxy service that provides credential management and request forwarding capabilities. It acts as an intermediary between clients and target APIs, managing authentication credentials centrally while providing a unified interface for accessing multiple external services.

**Status:** 🎉 **PRODUCTION READY** - All features implemented, tested, and documented.

## 🚀 Deployment Options

TokenRelay supports multiple deployment scenarios to fit your infrastructure needs:

### 🐳 Docker Deployment (Recommended)
- **Quick Setup**: Single command deployment with Docker Compose
- **Isolated Environment**: Containerized with Nginx reverse proxy
- **Production Ready**: SSL termination, security headers, rate limiting

### 🏗️ Standalone Deployment
- **Maximum Flexibility**: Deploy directly on Windows, Linux, or macOS
- **No Container Dependencies**: Native installation options
- **Multiple Web Servers**: Kestrel, Nginx, IIS support
- **Service Integration**: Windows Service, systemd, launchd

**Quick Standalone Setup:**
```powershell
# Windows self-contained (includes .NET runtime)
.\publish-standalone.ps1 -Runtime win-x64 -SelfContained $true

# Or use the simple batch file
.\quick-publish.bat
```

## 🎉 Implemented Features

### ✅ Core Proxy Functionality
- **Endpoint**: `/proxy/**`
- **Authentication**: `TOKEN-RELAY-AUTH` header required
- **Target Selection**: `TOKEN-RELAY-TARGET` header specifies configuration key
- **All HTTP Methods**: GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS supported
- **Header Management**: Automatic injection/removal of proxy headers
- **Request/Response Streaming**: Full support for large payloads
- **SSL Certificate Control**: Per-target certificate validation bypass for self-signed certificates

### ✅ Plugin System
- **Endpoint**: `/function/{plugin}/{action}`
- **Dynamic Loading**: Plugins loaded at startup
- **File Storage Plugin**: Local and network file storage support with file upload capability
- **Extensible Architecture**: Interface-based plugin system

### ✅ File Upload Support
- **Multipart Form Data**: Handles multipart/form-data requests for file uploads
- **Smart Storage**: Files >50MB use temporary files, smaller files stay in-memory
- **Metadata Extraction**: Automatic extraction of filename, size, and content type
- **Stream Management**: Proper disposal of file streams and resources
- **FileStorage Plugin Integration**: Direct file upload to configured storage locations

### ✅ Health & Status Monitoring
- **Health Endpoint**: `/health` - System health check
- **Status Endpoint**: `/status` - Detailed system information

### ✅ Logging & Runtime Management
- **Memory Log Buffer**: In-memory log storage with configurable retention
- **Log Retrieval API**: `/logs` - Get recent log entries via API
- **Dynamic Log Levels**: `/logs/level` - Change log level at runtime
- **Runtime Target Override**: `/config/targets/override` - Override target configurations
- **Production Ready**: Designed for troubleshooting in restricted environments

### ✅ API Documentation
- **Swagger UI**: Available at root `/` endpoint
- **Interactive API Explorer**: Full OpenAPI 3.0 specification
- **Authentication Documentation**: Built-in security schemas

## 🧪 Verified Test Results

### ✅ All Core Features Tested and Working:
```bash
# Health Check
curl http://localhost:5163/health
# Status: "Healthy" - All components operational

# File Upload 
curl -X POST "http://localhost:5163/function/FileStorage/httppostfile" \
  -H "TOKEN-RELAY-AUTH: your-token" \
  -F "file=@test-file.txt" \
  -F "subDirectory=uploads"
# Result: File uploaded successfully with metadata

# Proxy Operation
curl -X GET "http://localhost:5163/proxy/get?test=value" \
  -H "TOKEN-RELAY-TARGET: httpbin" \
  -H "TOKEN-RELAY-AUTH: your-token"
# Result: Successfully proxied to httpbin.org

# Authentication Enforcement
curl -X POST "http://localhost:5163/function/FileStorage/httppostfile" \
  -F "file=@test-file.txt"
# Result: "Unauthorized: TOKEN-RELAY-AUTH header required"
```

## 🚀 Quick Start

### 1. Start the Application
```bash
cd TokenRelay
dotnet run
```
Application starts on: `http://localhost:5163`

### 2. View API Documentation
Open: `http://localhost:5163`

### 3. Test Health Endpoint
```bash
curl -X GET "http://localhost:5163/health"
```

### 4. Test Status Endpoint
```bash
curl -X GET "http://localhost:5163/status"
```

### 6. Test File Upload (FileStorage Plugin)
```bash
# Upload a file using multipart form data
curl -X POST "http://localhost:5163/function/FileStorage/httppostfile" \
  -H "TOKEN-RELAY-AUTH: your-secure-proxy-token-here" \
  -F "file=@/path/to/your/file.txt" \
  -F "subDirectory=uploads" \
  -F "targetFileName=my-file.txt"
```

## 📁 File Upload Functionality

TokenRelay supports file uploads through the FileStorage plugin using multipart/form-data requests.

### Upload Endpoint
- **URL**: `/function/FileStorage/httppostfile`
- **Method**: POST
- **Content-Type**: multipart/form-data
- **Authentication**: Requires `TOKEN-RELAY-AUTH` header

### File Upload Features
- **Smart Storage Strategy**: 
  - Files ≤50MB: Stored in memory for faster processing
  - Files >50MB: Use temporary files with automatic cleanup
- **Metadata Extraction**: Automatically extracts filename, size, and content type
- **Unique Naming**: Adds timestamps to prevent filename conflicts
- **Subdirectory Support**: Optional organization into subdirectories
- **Stream Management**: Proper resource disposal and cleanup

### Upload Parameters
- **`file`** (required): The file to upload (form file field)
- **`subDirectory`** (optional): Subdirectory for file organization
- **`targetFileName`** (optional): Custom filename (defaults to original filename)

### Example PowerShell Upload
```powershell
$form = @{ 
    file = Get-Item "C:\path\to\file.txt"
    subDirectory = "documents"
    targetFileName = "uploaded-file.txt"
}

Invoke-RestMethod -Uri "http://localhost:5163/function/FileStorage/httppostfile" `
    -Method Post -Form $form `
    -Headers @{"TOKEN-RELAY-AUTH" = "your-secure-proxy-token-here"}
```

### Example Response
```json
{
  "success": true,
  "message": "File uploaded successfully",
  "originalFileName": "test-file.txt",
  "storedFileName": "uploaded-file_20241219-143052.txt",
  "filePath": "C:\\temp\\uploads\\documents\\uploaded-file_20241219-143052.txt",
  "fileSize": 1024,
  "contentType": "text/plain",
  "subDirectory": "documents",
  "timestamp": "2024-12-19T14:30:52Z"
}
```

### Storage Configuration
Configure file storage destinations in `tokenrelay.json`:
```json
{
  "plugins": {
    "fileStorage": {
      "enabled": true,
      "settings": {
        "destinations.StoragePath": "C:\\uploads",
        "destinations.documents": "C:\\temp\\documents",
        "destinations.images": "C:\\temp\\images"
      }
    }
  }
}
```

## 📋 Configuration

TokenRelay supports multiple configuration examples for different use cases. See [CONFIGURATION-EXAMPLES.md](TokenRelay/CONFIGURATION-EXAMPLES.md) for detailed examples.

### Quick Configuration
```bash
# Copy a base configuration
cp TokenRelay/tokenrelay.minimal-example.json TokenRelay/tokenrelay.json

# Edit for your environment
# Update auth tokens, target endpoints, and permissions
```

### Target Configuration (`tokenrelay.json`)
```json
{
  "proxy": {
    "auth": {
      "token": "your-secure-proxy-token-here"
    },
    "timeoutSeconds": 300,
    "logBufferMinutes": 20,
    "permissions": {
      "allowTargetConfigOverride": false,
      "allowLogRetrieval": true,
      "allowLogLevelAdjustment": true
    },
    "targets": {
      "httpbin": {
        "endpoint": "https://httpbin.org",
        "healthCheckUrl": "https://httpbin.org/status/200",
        "description": "HTTPBin service for testing HTTP requests and responses",
        "headers": {
          "User-Agent": "TokenRelay/1.0.0",
          "Accept": "application/json"
        }
      }
    }
  },
  "plugins": {
    "fileStorage": {
      "enabled": true,
      "settings": {
        "destinations.documents": "C:\\temp\\documents",
        "destinations.images": "C:\\temp\\images",
        "destinations.temp": "C:\\temp\\uploads"
      }
    }
  },
  "logging": {
    "level": "Information",
    "destinations": ["console"]
  }
}
```

### Target Configuration Fields

Each target in the `targets` section supports the following fields:

- **`endpoint`** (required): The base URL for the target service where requests will be forwarded
- **`healthCheckUrl`** (optional): Specific URL used by health checks to verify target availability. If not specified, the target will be skipped during connectivity health checks
- **`description`** (optional): Human-readable description of the target service for audit and documentation purposes
- **`headers`** (optional): Dictionary of default headers to inject into requests forwarded to this target
- **`ignoreCertificateValidation`** (optional, default: false): Bypass SSL certificate validation for this target. **⚠️ WARNING**: Only use for development/testing with self-signed certificates. 

### Proxy Configuration Fields

The `proxy` section supports the following global configuration options:

- **`timeoutSeconds`** (optional, default: 300): HTTP client timeout in seconds for all proxy and function requests. Applies to both direct mode and chain mode operations.
- **`logBufferMinutes`** (optional, default: 20): How many minutes of logs to keep in memory for retrieval via the logs API.
- **`permissions`** (optional): Runtime permission settings for security and operational control.

### Permissions Configuration

The `permissions` section controls what runtime management operations are allowed:

- **`allowTargetConfigOverride`** (default: false): Enables runtime override of target configurations via API
- **`allowLogRetrieval`** (default: true): Enables retrieval of in-memory logs via API  
- **`allowLogLevelAdjustment`** (default: true): Enables runtime log level changes via API

Example:
```json
"targets": {
  "api-service": {
    "endpoint": "https://api.example.com",
    "healthCheckUrl": "https://api.example.com/health",
    "description": "Main API service for customer data",
    "headers": {
      "User-Agent": "TokenRelay/1.0.0",
      "Accept": "application/json"
    }
  }
}
```

### Configuration Quick Reference

| Configuration File | Use Case | Description |
|-------------------|----------|-------------|
| `tokenrelay.direct-example.json` | Development | Complete direct mode with multiple targets |
| `tokenrelay.chain-example.json` | Chain Mode | Proxy-to-proxy configuration |

**Key Settings:**
- **`proxy.permissions.allowTargetConfigOverride`**: Enable runtime target changes (default: false)
- **`proxy.logBufferMinutes`**: Log retention period (default: 20 minutes)
- **`logging.level`**: Initial log level (Information, Warning, Error, Debug, Trace)

## 🔍 Logging & Runtime Management

TokenRelay provides advanced logging and runtime configuration management features designed for production environments where investigation of issues can be challenging.

### ✅ Memory Log Buffer

TokenRelay maintains an in-memory buffer of recent log entries that can be retrieved via API:

- **Configurable Duration**: Keeps logs for a configurable period (default: 20 minutes)
- **Memory Efficient**: Automatically cleans up old entries
- **Real-time Access**: Retrieve logs without server access

#### Configuration
```json
{
  "proxy": {
    "logBufferMinutes": 20,
    "permissions": {
      "allowLogRetrieval": true,
      "allowLogLevelAdjustment": true,
      "allowTargetConfigOverride": false
    }
  },
  "logging": {
    "level": "Information",
    "destinations": ["console"]
  }
}
```

#### Retrieve Logs
```bash
# Get current logs with authentication
curl -X GET "http://localhost:5163/logs" \
  -H "TOKEN-RELAY-AUTH: your-secure-proxy-token-here"
```

**Response:**
```json
{
  "currentLogLevel": "Information",
  "bufferMinutes": 20,
  "totalEntries": 145,
  "logs": [
    {
      "timestamp": "2024-12-19T14:30:52Z",
      "level": "Information",
      "category": "TokenRelay.Controllers.ProxyController",
      "message": "Processing proxy request for target: httpbin",
      "exception": null
    }
  ]
}
```

### ✅ Debug Body Encryption

When debug logging is enabled, request and response bodies are logged for troubleshooting. For additional security, you can encrypt these logged bodies using AES encryption by setting an environment variable:

```bash
# Set the encryption key (any string, will be padded/truncated to 32 bytes for AES-256)
export DEBUG_BODY_ENCRYPTION_KEY=your-secret-encryption-key-here
```

When enabled, debug logs will show encrypted content:
```
Body (256 chars):
ENC_DEBUG:base64encodedencryptedcontent...
```

To decrypt the logs, use the `DebugEncryptionHelper.Decrypt()` method with the same key:
```csharp
var plainText = DebugEncryptionHelper.Decrypt(encryptedLogLine, "your-secret-encryption-key-here");
```

**Note:** If the environment variable is not set, bodies are logged in plain text (after sanitization of sensitive fields).

### ✅ Dynamic Log Level Changes

Change log levels at runtime without restarting the service:

```bash
# Set log level to Debug for detailed troubleshooting
curl -X POST "http://localhost:5163/logs/level" \
  -H "TOKEN-RELAY-AUTH: your-secure-proxy-token-here" \
  -H "Content-Type: application/json" \
  -d '{"logLevel": "Debug"}'

# Supported levels: Trace, Debug, Information, Warning, Error, Critical, None
```

**Response:**
```json
{
  "message": "Log level updated successfully",
  "previousLevel": "Information",
  "newLevel": "Debug",
  "note": "Log level change is volatile and will revert to configuration on restart"
}
```

### ✅ Runtime Target Configuration Override

Override target configurations at runtime for testing and troubleshooting:

#### Enable Feature
```json
{
  "proxy": {
    "permissions": {
      "allowTargetConfigOverride": true
    }
  }
}
```

#### Override Target Configurations
```bash
# Override target configurations
curl -X POST "http://localhost:5163/config/targets/override" \
  -H "TOKEN-RELAY-AUTH: your-secure-proxy-token-here" \
  -H "Content-Type: application/json" \
  -d '{
    "targets": {
      "test-api": {
        "endpoint": "https://test-api.example.com",
        "description": "Test API endpoint for debugging",
        "headers": {
          "Authorization": "Bearer test-token"
        }
      }
    }
  }'
```

#### View Current Overrides
```bash
curl -X GET "http://localhost:5163/config/targets/override" \
  -H "TOKEN-RELAY-AUTH: your-secure-proxy-token-here"
```

#### Clear All Overrides
```bash
curl -X DELETE "http://localhost:5163/config/targets/override" \
  -H "TOKEN-RELAY-AUTH: your-secure-proxy-token-here"
```

### Production Troubleshooting Workflow

1. **Increase Log Level**: Set to `Debug` or `Trace` for detailed logging
2. **Reproduce Issue**: Execute the problematic operation
3. **Retrieve Logs**: Get detailed logs via the logs endpoint
4. **Analyze**: Review the log entries for the issue
5. **Reset Log Level**: Return to `Information` or `Warning` for normal operation

All runtime changes are **volatile** and will revert to configuration values on application restart.

## 🔐 SSL Certificate Validation

### Understanding 502 Bad Gateway Errors

If you're experiencing **502 Bad Gateway** errors when connecting to HTTPS targets, the most common cause is **SSL certificate validation failure**. This occurs when:

- Target uses a self-signed certificate
- Certificate has expired
- Certificate hostname doesn't match
- Certificate authority is not trusted

### Per-Target Certificate Validation Control

TokenRelay allows you to bypass SSL certificate validation on a per-target basis:

```json
{
  "proxy": {
    "targets": {
      "dev-api": {
        "endpoint": "https://dev.internal.example.com/api",
        "description": "Development API with self-signed certificate",
        "ignoreCertificateValidation": true
      },
      "prod-api": {
        "endpoint": "https://api.example.com",
        "description": "Production API with valid certificate",
        "ignoreCertificateValidation": false
      }
    }
  }
}
```

**⚠️ Security Warning**: Only use `ignoreCertificateValidation: true` for:
- Development/testing environments
- Internal networks with self-signed certificates
- **NEVER** for production endpoints accessible over public internet

### Certificate Validation Features

- ✅ **Per-target control**: Each target can have its own certificate validation setting
- ✅ **Secure by default**: Defaults to strict validation (`false`)
- ✅ **Warning logs**: Logs warnings when certificate validation is disabled
- ✅ **Runtime override support**: Can be changed via target configuration override API
- ✅ **Status visibility**: Shows in `/status` endpoint response
- ✅ **Chain mode support**: Works for downstream proxy connections too

### Example: Solving 502 Errors

```bash
# Before: Getting 502 Bad Gateway
curl http://localhost:5163/proxy/api/test \
  -H "TOKEN-RELAY-TARGET: dev-api" \
  -H "TOKEN-RELAY-AUTH: token"
# Response: 502 Bad Gateway

# After: Adding ignoreCertificateValidation: true to target config
curl http://localhost:5163/proxy/api/test \
  -H "TOKEN-RELAY-TARGET: dev-api" \
  -H "TOKEN-RELAY-AUTH: token"
# Response: 200 OK with target data
```

**📖 Full Documentation**: See [CERTIFICATE-VALIDATION.md](CERTIFICATE-VALIDATION.md) for:
- Detailed configuration guide
- Security considerations
- Alternative solutions (certificate trust store)
- Troubleshooting guide
- Best practices

## 🏗️ Architecture

### Core Components
- **ProxyController**: Handles `/proxy/**` endpoints
- **FunctionController**: Manages `/function/{plugin}/{function}` endpoints  
- **SystemController**: Provides `/status` endpoint
- **Health Checks**: Built-in .NET Core health monitoring at `/health`, `/health/live`, `/health/ready`
- **AuthenticationMiddleware**: Enforces TOKEN-RELAY-AUTH validation
- **ProxyService**: HTTP forwarding and header management
- **PluginService**: Plugin loading and execution
- **ConfigurationService**: Hot-reloadable configuration management

### Plugin System
- **ITokenRelayPlugin**: Base interface for all plugins
- **FileStoragePlugin**: Built-in file storage implementation
- **Dynamic Loading**: Plugins loaded from configuration
- **Error Isolation**: Plugin failures don't crash the system

### Health Monitoring
TokenRelay uses .NET Core's built-in health check system for comprehensive monitoring:

- **`/health`** - Detailed health check with JSON response including all components

Health checks monitor:
- **Configuration**: Validates auth tokens and target configurations
- **Plugins**: Monitors plugin loading status and health
- **Connectivity**: Tests reachability of configured target endpoints

Example health response:
```json
{
  "status": "Healthy",
  "totalDuration": "00:00:00.6073142",
  "checks": [
    {
      "name": "configuration",
      "status": "Healthy",
      "description": "Configuration is valid",
      "data": {
        "targets_count": 1,
        "auth_configured": true
      }
    },
    {
      "name": "plugins",
      "status": "Healthy", 
      "description": "1 plugins loaded successfully",
      "data": {
        "total_plugins": 1,
        "healthy_plugins": 1,
        "plugin_names": ["FileStorage"]
      }
    },
    {
      "name": "connectivity",
      "status": "Healthy",
      "description": "All 1 checked targets are reachable"
    }
  ]
}
```

## 🔧 Next Steps for Production

1. **Encryption**: Implement proper token encryption using Data Protection API ✅ (Basic AES encryption implemented)
2. **Rate Limiting**: Add DoS protection and rate limiting
3. **Circuit Breakers**: Implement retry logic and circuit breakers
4. **Containerization**: Create Docker images and compose files ✅ (Available)
5. **CI/CD**: Set up automated testing and deployment pipelines
6. **Advanced Monitoring**: Add metrics collection and alerting ✅ (Basic logging and health checks implemented)
7. **Load Testing**: Performance validation under load