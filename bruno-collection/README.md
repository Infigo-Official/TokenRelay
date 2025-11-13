# TokenRelay Bruno API Collections

This directory contains [Bruno](https://www.usebruno.com/) API collections for testing TokenRelay.

## Available Collections

### 📦 TokenRelay-OAuth-Tests

Comprehensive test collection for TokenRelay's OAuth 2.0 integration.

**Features:**
- Health checks for TokenRelay and OAuth server
- Direct OAuth token requests (Password Grant, Client Credentials)
- TokenRelay OAuth proxy testing
- Admin endpoints (status, logs)
- Automatic token caching demonstration

**Quick Start:**
```bash
cd docker
docker-compose up -d
```

Then open the collection in Bruno and start testing!

## What is Bruno?

Bruno is a fast, Git-friendly open-source API client alternative to Postman.

**Key Features:**
- File-based collections (`.bru` files)
- Version control friendly
- No account required
- Lightweight and fast
- Environments and scripting support

**Download:** [usebruno.com](https://www.usebruno.com/)

## Getting Started

1. **Install Bruno**: Download from [usebruno.com](https://www.usebruno.com/)

2. **Open Collection**:
   - Launch Bruno
   - Click "Open Collection"
   - Navigate to `bruno-collection/TokenRelay-OAuth-Tests`

3. **Start Testing**:
   - Select the "Local" environment
   - Run the health checks first
   - Follow the documented workflow in each collection

## Collection Structure

```
bruno-collection/
├── README.md (this file)
└── TokenRelay-OAuth-Tests/
    ├── README.md                       # Detailed collection guide
    ├── bruno.json                      # Collection metadata
    ├── environments/
    │   └── Local.bru                   # Environment variables
    ├── 1. Health Checks/               # Service health verification
    ├── 2. OAuth Direct/                # Direct OAuth testing
    ├── 3. TokenRelay OAuth Proxy/      # OAuth proxy testing
    └── 4. TokenRelay Admin/            # Admin endpoints
```

## Contributing

To add a new collection:

1. Create a new directory: `bruno-collection/YourCollectionName/`
2. Add `bruno.json` and `README.md`
3. Create your `.bru` request files
4. Update this README

---

For detailed usage instructions, see the README in each collection directory.
