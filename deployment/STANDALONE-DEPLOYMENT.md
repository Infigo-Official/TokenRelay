# TokenRelay - Standalone Deployment Guide

> **Production-Ready HTTP Proxy Service with Credential Management**

TokenRelay is a secure HTTP proxy service that provides credential management and request forwarding capabilities. This guide covers deployment without Docker for maximum flexibility and control.

## 📋 Table of Contents

- [Quick Start](#quick-start)
- [Prerequisites](#prerequisites)
- [Deployment Options](#deployment-options)
- [Running with Kestrel](#running-with-kestrel)
- [Running with Nginx](#running-with-nginx)
- [Running with IIS](#running-with-iis)
- [Service Installation](#service-installation)
- [Configuration](#configuration)
- [Security Considerations](#security-considerations)
- [Monitoring & Troubleshooting](#monitoring--troubleshooting)

## 🚀 Quick Start

### 1. Prerequisites Check

**Windows:**
```powershell
# Check .NET installation
dotnet --version
# Should show 8.0.x or later

# Check PowerShell version
$PSVersionTable.PSVersion
# Should be 5.1 or later
```

**Linux/macOS:**
```bash
# Check .NET installation
dotnet --version
# Should show 8.0.x or later

# Check if systemd is available (Linux)
systemctl --version
```

### 2. Build and Publish

```powershell
# Self-contained Windows deployment (includes .NET runtime)
.\publish-standalone.ps1 -Runtime win-x64 -SelfContained $true

# Framework-dependent Windows deployment (requires .NET runtime)
.\publish-standalone.ps1 -Runtime win-x64 -SelfContained $false

# Self-contained Linux deployment
.\publish-standalone.ps1 -Runtime linux-x64 -SelfContained $true

# Portable deployment (works on any platform with .NET runtime)
.\publish-standalone.ps1 -Runtime portable -SelfContained $false
```

### 3. Deploy and Configure

1. Copy the published files to your target server
2. Copy `tokenrelay.template.json` to `tokenrelay.json`
3. Edit `tokenrelay.json` with your configuration
4. Run the application using one of the methods below

## 📦 Prerequisites

### .NET Runtime Requirements

| Deployment Type | Requirements |
|----------------|--------------|
| **Self-Contained** | ✅ No additional requirements |
| **Framework-Dependent** | .NET 8.0 Runtime or later |

**Download .NET:**
- **Windows**: https://dotnet.microsoft.com/download/dotnet/8.0
- **Linux**: Use your distribution's package manager or download from Microsoft
- **macOS**: https://dotnet.microsoft.com/download/dotnet/8.0

### System Requirements

- **Memory**: Minimum 512MB RAM, Recommended 1GB+
- **Storage**: 100MB+ free space (plus space for uploads and logs)
- **Network**: Access to target APIs and client networks
- **Ports**: Default HTTP (5163) and HTTPS (7102), or custom ports

## 🏗️ Deployment Options

### Option 1: Self-Contained Deployment ✅ Recommended for Production

**Advantages:**
- ✅ No .NET runtime dependencies
- ✅ Version isolation
- ✅ Easier deployment
- ✅ Better security (no shared runtime)

**Disadvantages:**
- ❌ Larger file size (~100MB)
- ❌ Separate updates needed for runtime security patches

### Option 2: Framework-Dependent Deployment

**Advantages:**
- ✅ Smaller deployment size (~10MB)
- ✅ Shared runtime updates
- ✅ Better for multiple .NET applications

**Disadvantages:**
- ❌ Requires .NET runtime installation
- ❌ Runtime version compatibility

## 🔧 Running with Kestrel (Built-in Web Server)

Kestrel is ASP.NET Core's built-in, cross-platform web server. It's production-ready and high-performance.

### Development/Testing

```bash
# Windows
start.bat

# Linux/macOS
chmod +x start.sh
./start.sh
```

### Production Configuration

Create `appsettings.Production.json`:

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:5163"
      },
      "Https": {
        "Url": "https://0.0.0.0:7102",
        "Certificate": {
          "Path": "/path/to/certificate.pfx",
          "Password": "certificate-password"
        }
      }
    },
    "Limits": {
      "MaxConcurrentConnections": 100,
      "MaxConcurrentUpgradedConnections": 100,
      "MaxRequestBodySize": 104857600,
      "KeepAliveTimeout": "00:02:00",
      "RequestHeadersTimeout": "00:00:30"
    }
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

### SSL Certificate Setup

**Using Let's Encrypt (Linux):**
```bash
# Install certbot
sudo apt install certbot

# Generate certificate
sudo certbot certonly --standalone -d yourdomain.com

# Certificate will be at: /etc/letsencrypt/live/yourdomain.com/
```

**Using pfx certificate:**
```bash
# Set environment variables
export ASPNETCORE_Kestrel__Certificates__Default__Path="/path/to/cert.pfx"
export ASPNETCORE_Kestrel__Certificates__Default__Password="your-password"
```

### Firewall Configuration

**Windows:**
```powershell
# Allow HTTP traffic
New-NetFirewallRule -DisplayName "TokenRelay HTTP" -Direction Inbound -Protocol TCP -LocalPort 5163 -Action Allow

# Allow HTTPS traffic
New-NetFirewallRule -DisplayName "TokenRelay HTTPS" -Direction Inbound -Protocol TCP -LocalPort 7102 -Action Allow
```

**Linux (UFW):**
```bash
sudo ufw allow 5163/tcp
sudo ufw allow 7102/tcp
```

## 🌐 Running with Nginx (Reverse Proxy)

Using Nginx as a reverse proxy provides additional features like load balancing, SSL termination, and static file serving.

### Install Nginx

**Ubuntu/Debian:**
```bash
sudo apt update
sudo apt install nginx
```

**CentOS/RHEL:**
```bash
sudo yum install nginx
# or for newer versions:
sudo dnf install nginx
```

**Windows:**
Download from https://nginx.org/en/download.html

### Nginx Configuration

Create `/etc/nginx/sites-available/tokenrelay`:

```nginx
# Upstream definition
upstream tokenrelay_backend {
    server 127.0.0.1:5163;
    keepalive 32;
}

# Rate limiting zones
limit_req_zone $binary_remote_addr zone=api:10m rate=10r/s;
limit_req_zone $binary_remote_addr zone=upload:10m rate=2r/s;

# HTTP to HTTPS redirect
server {
    listen 80;
    server_name yourdomain.com;
    return 301 https://$server_name$request_uri;
}

# Main HTTPS server
server {
    listen 443 ssl http2;
    server_name yourdomain.com;

    # SSL Configuration
    ssl_certificate /path/to/your/certificate.crt;
    ssl_certificate_key /path/to/your/private.key;
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers ECDHE-RSA-AES256-GCM-SHA512:DHE-RSA-AES256-GCM-SHA512:ECDHE-RSA-AES256-GCM-SHA384;
    ssl_prefer_server_ciphers off;
    ssl_session_cache shared:SSL:10m;
    ssl_session_timeout 10m;

    # Security headers
    add_header X-Frame-Options DENY always;
    add_header X-Content-Type-Options nosniff always;
    add_header X-XSS-Protection "1; mode=block" always;
    add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;
    add_header Referrer-Policy "strict-origin-when-cross-origin" always;

    # File upload size limit
    client_max_body_size 100M;
    client_body_timeout 60s;
    client_header_timeout 60s;

    # Proxy settings
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection $connection_upgrade;
    proxy_set_header Host $host;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_set_header X-Forwarded-Host $server_name;
    proxy_cache_bypass $http_upgrade;
    proxy_connect_timeout 30s;
    proxy_send_timeout 30s;
    proxy_read_timeout 30s;

    # API endpoints with rate limiting
    location ~ ^/(proxy|function)/ {
        limit_req zone=api burst=20 nodelay;
        proxy_pass http://tokenrelay_backend;
    }

    # File upload endpoints with stricter rate limiting
    location ~ ^/function/.*/upload {
        limit_req zone=upload burst=5 nodelay;
        proxy_pass http://tokenrelay_backend;
    }

    # Health checks (no rate limiting)
    location ~ ^/(health|status) {
        proxy_pass http://tokenrelay_backend;
        access_log off;
    }

    # Swagger UI
    location / {
        proxy_pass http://tokenrelay_backend;
    }

    # Logging
    access_log /var/log/nginx/tokenrelay_access.log;
    error_log /var/log/nginx/tokenrelay_error.log;
}

# Connection upgrade map
map $http_upgrade $connection_upgrade {
    default upgrade;
    '' close;
}
```

### Enable and Start

```bash
# Enable the site
sudo ln -s /etc/nginx/sites-available/tokenrelay /etc/nginx/sites-enabled/

# Test configuration
sudo nginx -t

# Restart Nginx
sudo systemctl restart nginx

# Enable auto-start
sudo systemctl enable nginx
```

### Configure TokenRelay for Nginx

Update `appsettings.Production.json`:

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://127.0.0.1:5163"
      }
    }
  },
  "ForwardedHeaders": {
    "ForwardedHeaders": "XForwardedFor,XForwardedProto,XForwardedHost",
    "KnownProxies": ["127.0.0.1"]
  }
}
```

## 🪟 Running with IIS (Windows)

### Prerequisites

1. Install IIS with ASP.NET Core Hosting Bundle
2. Install .NET 8.0 Hosting Bundle

### IIS Configuration

1. **Create Application Pool:**
   ```powershell
   # Create new application pool
   New-WebAppPool -Name "TokenRelay" -ProcessModel @{identityType="ApplicationPoolIdentity"}
   
   # Configure .NET version
   Set-ItemProperty -Path "IIS:\AppPools\TokenRelay" -Name processModel.identityType -Value ApplicationPoolIdentity
   Set-ItemProperty -Path "IIS:\AppPools\TokenRelay" -Name managedRuntimeVersion -Value ""
   ```

2. **Create Website:**
   ```powershell
   # Create website
   New-Website -Name "TokenRelay" -ApplicationPool "TokenRelay" -PhysicalPath "C:\inetpub\tokenrelay" -Port 80
   ```

3. **Configure web.config:**

Create `web.config` in the application root:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath="dotnet" 
                  arguments=".\TokenRelay.dll" 
                  stdoutLogEnabled="false" 
                  stdoutLogFile=".\logs\stdout" 
                  hostingModel="inprocess">
        <environmentVariables>
          <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
          <environmentVariable name="ConfigPath" value="tokenrelay.json" />
        </environmentVariables>
      </aspNetCore>
      <security>
        <requestFiltering>
          <requestLimits maxAllowedContentLength="104857600" />
        </requestFiltering>
      </security>
    </system.webServer>
  </location>
</configuration>
```

### SSL Configuration for IIS

1. **Generate/Import Certificate:**
   - Use IIS Manager to generate self-signed certificate (development)
   - Import purchased/Let's Encrypt certificate (production)

2. **Bind HTTPS:**
   ```powershell
   New-WebBinding -Name "TokenRelay" -Protocol https -Port 443 -SslFlags 1
   ```

## 🔧 Service Installation

### Windows Service

```powershell
# Navigate to application directory
cd C:\path\to\tokenrelay

# Install service
.\service.bat install

# Start service
.\service.bat start

# Check status
sc query TokenRelay
```

### Linux systemd Service

```bash
# Copy service file
sudo cp tokenrelay.service /etc/systemd/system/

# Reload systemd
sudo systemctl daemon-reload

# Enable service
sudo systemctl enable tokenrelay

# Start service
sudo systemctl start tokenrelay

# Check status
sudo systemctl status tokenrelay

# View logs
sudo journalctl -u tokenrelay -f
```

### macOS launchd Service

Create `/Library/LaunchDaemons/com.yourcompany.tokenrelay.plist`:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>com.yourcompany.tokenrelay</string>
    <key>ProgramArguments</key>
    <array>
        <string>/usr/local/bin/dotnet</string>
        <string>/opt/tokenrelay/TokenRelay.dll</string>
    </array>
    <key>WorkingDirectory</key>
    <string>/opt/tokenrelay</string>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <true/>
    <key>EnvironmentVariables</key>
    <dict>
        <key>ASPNETCORE_ENVIRONMENT</key>
        <string>Production</string>
        <key>ConfigPath</key>
        <string>/opt/tokenrelay/tokenrelay.json</string>
    </dict>
</dict>
</plist>
```

Load the service:
```bash
sudo launchctl load /Library/LaunchDaemons/com.yourcompany.tokenrelay.plist
sudo launchctl start com.yourcompany.tokenrelay
```

## ⚙️ Configuration

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `ASPNETCORE_ENVIRONMENT` | Environment (Development/Production) | `Development` |
| `ASPNETCORE_URLS` | Binding URLs | `http://localhost:5163;https://localhost:7102` |
| `ConfigPath` | Path to configuration file | `tokenrelay.json` |

### Configuration File Structure

```json
{
  "proxy": {
    "auth": {
      "tokens": ["ENC:your-encrypted-token"],
      "encryptionKey": "your-32-character-key"
    },
    "targets": {
      "target1": {
        "endpoint": "https://api.example.com",
        "healthCheckUrl": "https://api.example.com/health",
        "description": "Example API",
        "headers": {
          "Authorization": "Bearer token123"
        }
      }
    }
  },
  "plugins": {},
  "logging": {
    "level": "Information",
    "destinations": ["console", "file"],
    "file": {
      "path": "logs/tokenrelay.log",
      "rollingInterval": "Day",
      "retainedFileCount": 30
    }
  }
}
```

### Token Encryption

Use the provided encryption tool:

```powershell
# Encrypt a token
.\tools\Encrypt-Token.ps1 -Token "your-secret-token" -Key "your-32-character-encryption-key"
```

## 🔒 Security Considerations

### Network Security

1. **Firewall Rules:**
   - Only allow necessary ports (80, 443, or custom)
   - Restrict access to management ports
   - Consider IP whitelisting for admin access

2. **TLS Configuration:**
   - Use TLS 1.2 or higher
   - Strong cipher suites
   - Regular certificate renewal

### Application Security

1. **Token Management:**
   - Use strong, unique tokens
   - Regular token rotation
   - Secure token storage (encrypted)

2. **Configuration Security:**
   - Protect configuration files (600 permissions on Linux)
   - Use environment variables for sensitive data
   - Regular security updates

3. **Logging Security:**
   - Avoid logging sensitive data
   - Secure log file access
   - Regular log rotation

### File System Security

```bash
# Linux permissions
sudo chown -R tokenrelay:tokenrelay /opt/tokenrelay
sudo chmod 755 /opt/tokenrelay
sudo chmod 600 /opt/tokenrelay/tokenrelay.json
sudo chmod 755 /opt/tokenrelay/uploads
sudo chmod 755 /opt/tokenrelay/logs
```

## 📊 Monitoring & Troubleshooting

### Health Checks

```bash
# Basic health check
curl http://localhost:5163/health

# Detailed status
curl http://localhost:5163/status
```

### Performance Monitoring

1. **Built-in Metrics:**
   - Health check endpoints
   - Application logs
   - Request/response times

2. **External Monitoring:**
   - Application Performance Monitoring (APM) tools
   - Log aggregation (ELK stack, Splunk)
   - Uptime monitoring

### Common Issues

| Issue | Symptom | Solution |
|-------|---------|----------|
| **Port Conflicts** | Startup fails | Change port in configuration |
| **Permission Denied** | File access errors | Check file permissions |
| **SSL Certificate** | HTTPS not working | Verify certificate installation |
| **Memory Issues** | High memory usage | Check for memory leaks, restart service |
| **Network Connectivity** | Target unreachable | Check firewall and DNS |

### Log Analysis

```bash
# View recent logs (systemd)
sudo journalctl -u tokenrelay -n 100

# Follow logs in real-time
sudo journalctl -u tokenrelay -f

# Filter by log level
sudo journalctl -u tokenrelay -p err
```

### Performance Tuning

1. **Kestrel Settings:**
   ```json
   {
     "Kestrel": {
       "Limits": {
         "MaxConcurrentConnections": 100,
         "MaxRequestBodySize": 104857600,
         "KeepAliveTimeout": "00:02:00"
       }
     }
   }
   ```

2. **System Resources:**
   - Monitor CPU and memory usage
   - Adjust connection limits based on capacity
   - Consider load balancing for high traffic

## 🆘 Support

- **Documentation**: Check README.md in the application directory
- **Health Checks**: Use `/health` and `/status` endpoints
- **Logs**: Check application and system logs
- **Configuration**: Verify `tokenrelay.json` syntax and values

## 📚 Additional Resources

- [ASP.NET Core Deployment Guide](https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/)
- [Nginx Documentation](https://nginx.org/en/docs/)
- [IIS Documentation](https://docs.microsoft.com/en-us/iis/)
- [systemd Service Management](https://www.freedesktop.org/software/systemd/man/systemd.service.html)

---

**🎉 Congratulations!** You now have a comprehensive guide for deploying TokenRelay without Docker. Choose the deployment method that best fits your infrastructure and security requirements.
