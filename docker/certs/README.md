# Custom Certificates Directory

This directory is for custom SSL/TLS certificates that should be trusted by TokenRelay when making outbound HTTPS connections.

## When to Use

Add certificates to this directory if:
- Your target APIs use self-signed certificates
- Your target APIs use certificates signed by a private Certificate Authority (CA)
- You need to trust specific intermediate CA certificates
- You're working in a corporate environment with custom root CAs

## How to Use

1. **Place your certificate files here** with the `.crt` extension
   - Example: `my-custom-ca.crt`
   - Example: `corporate-root-ca.crt`

2. **Certificate format:** PEM-encoded X.509 certificates
   ```
   -----BEGIN CERTIFICATE-----
   MIIBkTCB+wIJAKHHCgVZU2XtMA0GCSqGSIb3DQEBCwUAMA0xCzAJBgNVBAYTAkFV
   ...
   -----END CERTIFICATE-----
   ```

3. **Start or restart the container:**
   ```bash
   docker-compose restart
   ```

## Certificate Loading Process

When the container starts:
1. Checks for `.crt` files in `/home/site/certs` (this directory when mounted)
2. Copies any found certificates to `/usr/local/share/ca-certificates/`
3. Runs `update-ca-certificates` to add them to the system trust store
4. Starts the TokenRelay application

This happens automatically before the .NET application starts, ensuring all certificates are trusted.

## Troubleshooting

### Check if certificates are loaded
View container logs to see certificate loading messages:
```bash
docker-compose logs tokenrelay | grep -i certificate
```

You should see:
```
Loading custom certificates from /home/site/certs...
Certificate loading completed.
```

### Verify certificate is trusted
```bash
# Enter the container
docker-compose exec tokenrelay /bin/bash

# Check installed certificates
ls -la /usr/local/share/ca-certificates/

# Test connection to your API
curl -v https://your-api-endpoint.com
```

## Important Notes

- Only `.crt` files are processed
- Files must be readable by the container
- Changes require container restart to take effect
- This directory is mounted as read-only for security
- Invalid certificates will be silently skipped with a log message

## Security Considerations

- Only add certificates from trusted sources
- Keep this directory secure with appropriate file permissions
- Regularly review and update certificates as needed
- Remove certificates when no longer needed
