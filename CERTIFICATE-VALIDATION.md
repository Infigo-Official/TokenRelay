# SSL Certificate Validation Configuration

## Overview

TokenRelay now supports per-target SSL certificate validation control. This feature allows you to bypass certificate validation for specific targets that use self-signed or invalid certificates, which is common in development and testing environments.

## ⚠️ Security Warning

**IMPORTANT**: Disabling certificate validation exposes you to man-in-the-middle attacks. Only use `IgnoreCertificateValidation: true` for:
- Development environments
- Testing environments
- Internal networks with self-signed certificates

**NEVER** disable certificate validation for production endpoints accessible over the public internet.

## Configuration

Add the `IgnoreCertificateValidation` property to any target configuration:

```json
{
  "Proxy": {
    "Targets": {
      "dev-api": {
        "Endpoint": "https://dev.internal.example.com/api",
        "Description": "Development API with self-signed certificate",
        "Enabled": true,
        "IgnoreCertificateValidation": true
      },
      "prod-api": {
        "Endpoint": "https://api.example.com",
        "Description": "Production API with valid certificate",
        "Enabled": true,
        "IgnoreCertificateValidation": false
      }
    }
  }
}
```

### Property Details

- **IgnoreCertificateValidation**: `boolean` (default: `false`)
  - `true`: Bypass SSL certificate validation for this target (accepts any certificate)
  - `false`: Enforce strict SSL certificate validation (default and recommended)

## Understanding 502 Bad Gateway Errors

### Common Causes

When connecting to HTTPS endpoints, a **502 Bad Gateway** error can occur due to:

1. **SSL Certificate Validation Failures** (most common):
   - Self-signed certificates
   - Expired certificates
   - Hostname mismatch
   - Untrusted certificate authority

2. **Target Server Issues**:
   - Target server is down or unreachable
   - Firewall blocking the connection
   - Network connectivity problems

### How Certificate Validation Causes 502 Errors

When TokenRelay attempts to connect to an HTTPS endpoint with an invalid certificate:
1. The SSL/TLS handshake begins
2. The certificate is checked against trusted certificate authorities
3. If validation fails, the connection is terminated
4. The proxy cannot establish a connection to the backend
5. This results in a **502 Bad Gateway** response to the client

### Solving Certificate Issues

**Option 1: Enable IgnoreCertificateValidation (Quick Fix for Dev/Test)**
```json
{
  "IgnoreCertificateValidation": true
}
```

**Option 2: Add Certificate to Trust Store (Recommended for Production)**

For Linux/Docker:
```bash
# Copy certificate to trusted location
sudo cp your-cert.crt /usr/local/share/ca-certificates/
sudo update-ca-certificates
```

For Windows:
```powershell
# Import certificate to Trusted Root Certification Authorities
Import-Certificate -FilePath "your-cert.crt" -CertStoreLocation Cert:\LocalMachine\Root
```

**Option 3: Use Valid Certificates**
- Use Let's Encrypt for free, valid SSL certificates
- Purchase certificates from a trusted Certificate Authority
- Use your organization's internal CA

## Logging

When `IgnoreCertificateValidation` is enabled for a target, TokenRelay will log a warning message:

```
[Warning] ProxyService: Certificate validation is DISABLED for target endpoint: https://dev.example.com/api. 
This should only be used in development/testing environments!
```

This helps ensure that disabled certificate validation doesn't go unnoticed in production environments.

## Status Endpoint

The `/status` endpoint will show the `IgnoreCertificateValidation` setting for each target:

```json
{
  "ConfiguredTargets": [
    {
      "Key": "dev-api",
      "Description": "Development API",
      "Enabled": true,
      "IgnoreCertificateValidation": true,
      "Endpoint": "https://dev.internal.example.com/api"
    },
    {
      "Key": "prod-api",
      "Description": "Production API",
      "Enabled": true,
      "IgnoreCertificateValidation": false,
      "Endpoint": "https://api.example.com"
    }
  ]
}
```

## Runtime Configuration Override

The `IgnoreCertificateValidation` setting can also be changed at runtime using the target configuration override endpoint:

```bash
# Enable certificate validation bypass at runtime
curl -X POST http://localhost:5000/config/targets/override \
  -H "TOKEN-RELAY-AUTH: your-token" \
  -H "Content-Type: application/json" \
  -d '{
    "Targets": {
      "dev-api": {
        "Endpoint": "https://dev.internal.example.com/api",
        "Description": "Development API",
        "Enabled": true,
        "IgnoreCertificateValidation": true
      }
    }
  }'
```

**Note**: This requires `AllowTargetConfigOverride: true` in the Permissions configuration.

## Chain Mode

The `IgnoreCertificateValidation` setting also works in chain mode for the target proxy configuration:

```json
{
  "Proxy": {
    "Mode": "chain",
    "Chain": {
      "TargetProxy": {
        "Endpoint": "https://downstream-proxy.internal.example.com",
        "Description": "Downstream proxy with self-signed cert",
        "Token": "downstream-token",
        "IgnoreCertificateValidation": true
      }
    }
  }
}
```

## Best Practices

1. **Default to Secure**: Always keep `IgnoreCertificateValidation: false` unless absolutely necessary

2. **Environment-Specific Configuration**: Use different configuration files for different environments:
   ```
   tokenrelay.development.json  (IgnoreCertificateValidation: true)
   tokenrelay.production.json   (IgnoreCertificateValidation: false)
   ```

3. **Monitoring**: Regularly check logs for certificate validation warnings

4. **Documentation**: Document why certificate validation is disabled for specific targets

5. **Temporary Use**: If you must disable validation, plan to enable it again once proper certificates are in place

6. **Code Review**: Ensure that any configuration with `IgnoreCertificateValidation: true` is reviewed before deployment

## Troubleshooting

### Still Getting 502 After Enabling IgnoreCertificateValidation?

If you still get 502 errors after setting `IgnoreCertificateValidation: true`, the issue is likely not certificate-related. Check:

1. **Target Availability**: Can you reach the target from the proxy server?
   ```bash
   curl -k https://target-endpoint/health
   ```

2. **Firewall Rules**: Are there firewall rules blocking the connection?

3. **DNS Resolution**: Can the proxy resolve the target hostname?
   ```bash
   nslookup target-hostname
   ```

4. **Network Connectivity**: Is there network connectivity to the target?
   ```bash
   ping target-hostname
   telnet target-hostname 443
   ```

5. **Target Server Logs**: Check the target server's logs for connection attempts

6. **TokenRelay Logs**: Enable Debug logging to see detailed error messages:
   ```json
   {
     "Logging": {
       "Level": "Debug"
     }
   }
   ```

## Example Configurations

See `tokenrelay.self-signed-cert-example.json` for a complete example configuration.
