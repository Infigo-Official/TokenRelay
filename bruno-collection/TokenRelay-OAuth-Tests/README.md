# TokenRelay OAuth Tests - Bruno Collection

This Bruno API collection provides comprehensive tests for TokenRelay's OAuth 2.0 integration.

## 📋 What's Included

This collection includes requests for:

### 1. Health Checks
- **TokenRelay Health** - Verify TokenRelay service is running
- **OAuth Server Health** - Verify OAuth2 server is running

### 2. OAuth Direct (Test OAuth Server)
- **Get Token - Password Grant** - Request token with username/password
- **Get Token - Client Credentials** - Request token with client credentials

### 3. TokenRelay OAuth Proxy (Main Feature)
- **Proxy to OAuth API - Password Grant** - Test automatic OAuth token acquisition
- **Proxy to OAuth API - Client Credentials** - Test client credentials flow

### 4. TokenRelay Admin
- **Get Status** - View TokenRelay status and target health
- **Get Logs** - View recent logs (including OAuth operations)

## 🚀 Quick Start

### Prerequisites

1. **Install Bruno**: Download from [usebruno.com](https://www.usebruno.com/)

2. **Start Docker Services**:
   ```bash
   cd docker
   cp .env.template .env
   cp config/tokenrelay.template.json config/tokenrelay.json
   docker-compose up -d
   ```

3. **Wait for Services**: Give the OAuth server 30-40 seconds to initialize

### Using the Collection

1. **Open Bruno** and click "Open Collection"

2. **Navigate to** `bruno-collection/TokenRelay-OAuth-Tests`

3. **Select Environment**: Click "Local" environment in the top-right dropdown

4. **Update Variables** (if needed):
   - Open Environments → Local
   - Set `tokenrelay_auth_token` to your actual token (from tokenrelay.json)
   - Other variables are pre-configured for Docker setup

5. **Run Requests**:
   - Start with "Health Checks" to verify services are running
   - Test OAuth directly with "OAuth Direct" requests
   - Test TokenRelay proxy with "TokenRelay OAuth Proxy" requests

## 📝 Environment Variables

The `Local` environment includes:

```
tokenrelay_url: http://localhost:5163
oauth_server_url: http://localhost:8080
tokenrelay_auth_token: your-tokenrelay-auth-token-here
oauth_client_id: test_client_1
oauth_client_secret: test_secret_1
oauth_username: test_user
oauth_password: test_password
oauth_access_token: (automatically set by OAuth requests)
```

## 🔍 Testing Workflow

### Step 1: Verify Services Are Running

Run these in order:
1. **TokenRelay Health** - Should return "Healthy"
2. **OAuth Server Health** - Should return health status

### Step 2: Test OAuth Server Directly

Test the OAuth server independently:
1. **Get Token - Password Grant** - Acquires a token using username/password
2. **Get Token - Client Credentials** - Acquires a token using client credentials

Both requests automatically save the `access_token` to the `oauth_access_token` variable.

### Step 3: Test TokenRelay OAuth Proxy

Now test TokenRelay's automatic OAuth handling:
1. **Proxy to OAuth API - Password Grant**
   - TokenRelay will automatically acquire an OAuth token
   - Cache it for subsequent requests
   - Forward your request with the token

2. **Proxy to OAuth API - Client Credentials**
   - Same automatic handling with client credentials grant

### Step 4: Monitor and Debug

Use admin endpoints to monitor:
1. **Get Status** - View target health and OAuth cache stats
2. **Get Logs** - View OAuth operations (filter by "OAuth")

## 🔧 Advanced Usage

### Testing Token Caching

To observe TokenRelay's token caching:

1. Run **Proxy to OAuth API - Password Grant** twice
2. Check TokenRelay logs (Get Logs or `docker-compose logs tokenrelay`)
3. First request: You'll see OAuth token acquisition
4. Second request: Token retrieved from cache (faster!)

### Testing Token Expiration

Configure a short-lived token in OAuth server, then:
1. Request through TokenRelay
2. Wait for token to expire
3. Request again
4. TokenRelay automatically refreshes the token

### Custom Targets

To test with your own OAuth-protected APIs:

1. Add target configuration to `docker/config/tokenrelay.json`:
   ```json
   {
     "my-api": {
       "endpoint": "https://api.example.com",
       "authType": "oauth",
       "authData": {
         "token_endpoint": "https://auth.example.com/oauth/token",
         "grant_type": "password",
         "username": "myuser",
         "password": "mypass",
         "client_id": "myclient",
         "client_secret": "mysecret"
       }
     }
   }
   ```

2. Restart TokenRelay:
   ```bash
   docker-compose restart tokenrelay
   ```

3. Create a new request in Bruno:
   - URL: `{{tokenrelay_url}}/proxy/my-api/endpoint`
   - Header: `TOKEN-RELAY-TARGET: my-api`
   - Auth: Bearer `{{tokenrelay_auth_token}}`

## 📊 Understanding the Responses

### Successful OAuth Token Response

```json
{
  "access_token": "eyJhbGciOiJIUzI1NiIs...",
  "token_type": "Bearer",
  "expires_in": 3600,
  "refresh_token": "eyJhbGciOiJIUzI1NiIs...",
  "scope": "read write"
}
```

### TokenRelay Proxy Success

When TokenRelay successfully proxies a request:
- Status: 200 OK (or whatever the target returns)
- Headers: Original target response headers
- Body: Original target response body

TokenRelay adds these headers:
- `X-TokenRelay-Target`: Target name
- `X-TokenRelay-Duration`: Request duration in ms

### TokenRelay OAuth Errors

**Authentication Failed:**
```json
{
  "error": "Failed to acquire OAuth token for target 'oauth-test-api': unauthorized"
}
```

**Target Not Found:**
```json
{
  "error": "Target 'unknown' not found"
}
```

**Invalid Configuration:**
```json
{
  "error": "Target 'oauth-test-api' has invalid OAuth configuration"
}
```

## 🐛 Troubleshooting

### "Connection refused" errors

**Problem**: Can't connect to localhost:5163 or localhost:8080

**Solution**:
```bash
# Check if services are running
docker-compose ps

# Check logs
docker-compose logs tokenrelay
docker-compose logs oauth-server
```

### "Unauthorized" from OAuth server

**Problem**: OAuth credentials are incorrect

**Solution**:
1. Verify credentials match in:
   - `docker/config/tokenrelay.json` (TokenRelay config)
   - `bruno-collection/.../environments/Local.bru` (Bruno variables)
2. Check OAuth server logs: `docker-compose logs oauth-server`

### TokenRelay returns 502 Bad Gateway

**Problem**: OAuth token acquisition failed

**Solution**:
1. Test OAuth server directly (Run "Get Token - Password Grant")
2. Check TokenRelay logs: `docker-compose logs -f tokenrelay | grep -i oauth`
3. Verify OAuth server is accessible from TokenRelay container:
   ```bash
   docker-compose exec tokenrelay curl http://oauth-server:8080/health
   ```

### Requests are slow

**Problem**: Token not being cached

**Solution**:
1. Check logs for "Cache hit" vs "Cache miss"
2. Verify OAuthService is registered as Singleton (it is by default)
3. Check if token is expiring too quickly

## 📚 Additional Resources

- **TokenRelay Documentation**: See `/docker/README.md`
- **OAuth 2.0 Specification**: [RFC 6749](https://tools.ietf.org/html/rfc6749)
- **Bruno Documentation**: [docs.usebruno.com](https://docs.usebruno.com)

## 🎯 Collection Structure

```
TokenRelay-OAuth-Tests/
├── bruno.json                          # Collection metadata
├── README.md                           # This file
├── environments/
│   └── Local.bru                       # Local environment variables
├── 1. Health Checks/
│   ├── TokenRelay Health.bru
│   └── OAuth Server Health.bru
├── 2. OAuth Direct/
│   ├── Get Token - Password Grant.bru
│   └── Get Token - Client Credentials.bru
├── 3. TokenRelay OAuth Proxy/
│   ├── Proxy to OAuth API - Password Grant.bru
│   └── Proxy to OAuth API - Client Credentials.bru
└── 4. TokenRelay Admin/
    ├── Get Status.bru
    └── Get Logs.bru
```

## 🤝 Contributing

To add new requests to this collection:

1. Create a `.bru` file in the appropriate folder
2. Use the existing requests as templates
3. Include comprehensive documentation in the `docs` section
4. Test thoroughly with Docker setup

## 📄 License

This collection is part of the TokenRelay project and follows the same license.

---

**Happy Testing! 🚀**

For issues or questions, check the TokenRelay documentation or logs.
