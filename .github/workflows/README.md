# GitHub Actions - Docker Hub Publishing

This workflow automatically builds and publishes the TokenRelay Docker image to Docker Hub when you create a new version tag.

## Setup Instructions

### 1. Configure Docker Hub Secrets

Add the following secrets to your GitHub repository:

**Settings → Secrets and variables → Actions → New repository secret**

| Secret Name | Description | Example |
|-------------|-------------|---------|
| `DOCKER_HUB_USERNAME` | Your Docker Hub username | `yourname` |
| `DOCKER_HUB_ACCESS_TOKEN` | Docker Hub access token (not password) | `dckr_pat_xxxxx...` |

#### Creating a Docker Hub Access Token:

1. Log in to [Docker Hub](https://hub.docker.com/)
2. Go to **Account Settings → Security → Access Tokens**
3. Click **New Access Token**
4. Name: `github-actions-tokenrelay`
5. Permissions: **Read & Write**
6. Click **Generate** and copy the token
7. Add it to GitHub secrets as `DOCKER_HUB_ACCESS_TOKEN`

### 2. Create and Push a Version Tag

The workflow triggers automatically when you push a tag matching the pattern `v*.*.*` (e.g., `v1.0.0`, `v2.1.3`).

#### Create a new release:

```bash
# Ensure you're on the main branch with all changes committed
git checkout main
git pull

# Create and push a version tag
git tag v1.0.0
git push origin v1.0.0
```

#### Or create a tag with a message:

```bash
git tag -a v1.0.0 -m "Release version 1.0.0 - Initial production release"
git push origin v1.0.0
```

### 3. Monitor the Workflow

1. Go to your repository on GitHub
2. Click the **Actions** tab
3. You should see the workflow running: **"Build and Push to Docker Hub"**
4. Click on the workflow run to see detailed logs

## What Gets Published

When you push a tag like `v1.2.3`, the workflow creates the following Docker tags:

- `yourname/tokenrelay:1.2.3` - Full version
- `yourname/tokenrelay:1.2` - Major.minor version
- `yourname/tokenrelay:1` - Major version only
- `yourname/tokenrelay:latest` - Latest release

## Features

### Multi-Architecture Support
- **linux/amd64** - Standard x86_64 systems
- **linux/arm64** - ARM-based systems (Apple Silicon, Raspberry Pi, AWS Graviton)

### Security Scanning
- **Trivy vulnerability scanner** runs automatically
- Results uploaded to GitHub Security tab
- Scans for CRITICAL and HIGH severity vulnerabilities

### Build Optimization
- **Layer caching** via GitHub Actions cache
- **Parallel multi-arch builds** using Docker Buildx
- **SBOM generation** for supply chain security
- **Provenance attestation** for build verification

### Documentation Sync
- Automatically updates Docker Hub description with README.md
- Keeps documentation in sync between GitHub and Docker Hub

## Manual Triggering

You can also trigger the workflow manually without creating a tag:

1. Go to **Actions** tab in your repository
2. Select **"Build and Push to Docker Hub"** workflow
3. Click **"Run workflow"**
4. Select the branch to build from
5. Click **"Run workflow"**

This is useful for:
- Testing the workflow
- Publishing development builds
- Republishing after Docker Hub issues

## Using the Published Image

After the workflow completes, pull and run your image:

```bash
# Pull the latest version
docker pull yourname/tokenrelay:latest

# Or pull a specific version
docker pull yourname/tokenrelay:1.0.0

# Run the container
docker run -d \
  --name tokenrelay \
  -p 80:80 \
  -v ./tokenrelay.json:/app/tokenrelay.json:ro \
  -v ./uploads:/app/uploads \
  yourname/tokenrelay:latest
```

## Troubleshooting

### Workflow fails at "Log in to Docker Hub"
- **Error**: `unauthorized: incorrect username or password`
- **Solution**: Verify `DOCKER_HUB_USERNAME` and `DOCKER_HUB_ACCESS_TOKEN` secrets are correct
- **Note**: Use an access token, not your Docker Hub password

### Workflow fails at "Build and push Docker image"
- **Error**: Build context issues
- **Solution**: Ensure `docker/Dockerfile` exists and is valid
- **Check**: Dockerfile path is correct in workflow (`DOCKERFILE_PATH` variable)

### Trivy scan fails
- **Error**: Cannot pull image
- **Solution**: This step is marked `continue-on-error: true`, so it won't fail the workflow
- **Note**: Check if the image was successfully pushed first

### Docker Hub description not updating
- **Error**: Permission denied
- **Solution**: Ensure access token has **Read & Write** permissions
- **Note**: This step is marked `continue-on-error: true`, so it won't fail the workflow

## Versioning Best Practices

Follow semantic versioning (SemVer):

- **Major** (v2.0.0): Breaking changes
- **Minor** (v1.1.0): New features, backward compatible
- **Patch** (v1.0.1): Bug fixes, backward compatible

Examples:
```bash
# Patch release (bug fixes)
git tag v1.0.1 -m "Fix authentication bug"

# Minor release (new features)
git tag v1.1.0 -m "Add OAuth 2.0 support"

# Major release (breaking changes)
git tag v2.0.0 -m "Complete API redesign"
```

## Workflow Output

Each successful run generates a summary with:

- Published Docker tags
- Image digest (SHA256)
- Build date and commit SHA
- Pull command for the image

View the summary in the workflow run page under **"Generate build summary"** step.

## Next Steps

1. Set up the GitHub secrets
2. Create your first version tag
3. Monitor the workflow run
4. Test the published Docker image
5. Update your deployment pipelines to use the new image

For more information about TokenRelay deployment, see [DOCKER-DEPLOYMENT.md](../../DOCKER-DEPLOYMENT.md).
