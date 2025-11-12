# Custom Certificate Support - Docker Deployment

## Overview

TokenRelay Docker containers now support loading custom SSL/TLS certificates from a persistent directory. This feature is useful when your target APIs use self-signed certificates or certificates from private Certificate Authorities.

## Supported Persistent Storage Paths

The certificate loading mechanism supports multiple persistent storage locations, making it compatible with various hosting platforms:

### Azure App Service / Azure Container Apps
- **Path:** `/home/site/certs`
- **Persistent Storage:** Azure File Share mounted to `/home`
- **Documentation:** [Azure App Service persistent storage](https://learn.microsoft.com/en-us/azure/app-service/configure-custom-container#use-persistent-shared-storage)

### AWS App Runner
- **Path:** `/home/site/certs` (custom mount point)
- **Persistent Storage:** Amazon EFS file system
- **Documentation:** [AWS App Runner with EFS](https://docs.aws.amazon.com/apprunner/latest/dg/network-efs.html)

### AWS ECS/Fargate
- **Path:** `/home/site/certs` or `/mnt/efs/certs`
- **Persistent Storage:** Amazon EFS mounted via task definition
- **Documentation:** [ECS with EFS volumes](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/efs-volumes.html)

### AWS Elastic Beanstalk
- **Path:** `/home/site/certs`
- **Persistent Storage:** EFS file system configured in environment
- **Documentation:** [Elastic Beanstalk with EFS](https://docs.aws.amazon.com/elasticbeanstalk/latest/dg/services-efs.html)

### Docker / Docker Compose (Local/Self-hosted)
- **Path:** `/home/site/certs`
- **Persistent Storage:** Volume mount from host filesystem
- **Configuration:** See `docker-compose.yml`

### Kubernetes
- **Path:** `/home/site/certs`
- **Persistent Storage:** PersistentVolumeClaim, ConfigMap, or Secret
- **Documentation:** See Kubernetes deployment section below

## What Changed

### 1. Dockerfile Updates

**Location:** `docker/Dockerfile`

- Added `ca-certificates` package installation
- Created `/home/site/certs` directory for certificate storage
- Implemented a startup script (`/app/entrypoint.sh`) that:
  - Checks for `.crt` files in `/home/site/certs`
  - Copies certificates to `/usr/local/share/ca-certificates/`
  - Runs `update-ca-certificates` to trust them system-wide
  - Starts the .NET application as the `tokenrelay` user
- Changed the entrypoint to use the startup script instead of direct dotnet execution

**Important:** The script runs as root to update certificates, then drops privileges to the `tokenrelay` user before starting the application.

### 2. Docker Compose Updates

**Locations:** `docker/docker-compose.yml` and `docker/docker-compose-new.yml`

Added volume mount for certificates:
```yaml
- ${TOKENRELAY_CERTS_PATH:-./certs}:/home/site/certs:ro
```

This mounts the local `certs/` directory (read-only) to `/home/site/certs` in the container.

### 3. Environment Configuration

**Location:** `docker/env.template`

Added new environment variable:
```bash
TOKENRELAY_CERTS_PATH=./certs
```

This allows users to customize the certificate directory location.

### 4. Documentation

**Location:** `docker/README.md`

Added section explaining:
- How to add custom certificates
- When to use this feature
- Certificate loading process

**Location:** `docker/certs/README.md`

Created comprehensive guide covering:
- Use cases for custom certificates
- Certificate format requirements
- Loading process details
- Troubleshooting steps
- Security considerations

### 5. Git Configuration

**Location:** `.gitignore`

Added rules to:
- Ignore certificate files (`.crt`, `.pem`, `.cer`)
- Keep the directory structure tracked with `.gitkeep`

## Usage Examples

### Docker Compose (Local Development)

1. **Create the certs directory** (already done):
   ```bash
   mkdir -p docker/certs
   ```

2. **Add your certificate**:
   ```bash
   cp /path/to/my-ca.crt docker/certs/
   ```

3. **Start the container**:
   ```bash
   cd docker
   docker-compose up -d
   ```

4. **Verify loading** (check logs):
   ```bash
   docker-compose logs tokenrelay
   ```

   You should see:
   ```
   Loading custom certificates from /home/site/certs...
   Certificate loading completed.
   ```

### AWS ECS/Fargate with EFS

1. **Create an EFS file system** in the AWS Console or CLI

2. **Create the certificates directory on EFS**:
   ```bash
   # Mount EFS locally or via EC2 instance
   sudo mkdir -p /mnt/efs/tokenrelay/certs
   sudo cp my-ca.crt /mnt/efs/tokenrelay/certs/
   ```

3. **Update your ECS Task Definition** to mount EFS:
   ```json
   {
     "containerDefinitions": [{
       "name": "tokenrelay",
       "image": "infigosoftware/tokenrelay:1.0.6",
       "mountPoints": [{
         "sourceVolume": "tokenrelay-certs",
         "containerPath": "/home/site/certs",
         "readOnly": true
       }]
     }],
     "volumes": [{
       "name": "tokenrelay-certs",
       "efsVolumeConfiguration": {
         "fileSystemId": "fs-12345678",
         "rootDirectory": "/tokenrelay/certs",
         "transitEncryption": "ENABLED"
       }
     }]
   }
   ```

### AWS App Runner with EFS

1. **Create an EFS file system and access point**

2. **Configure App Runner service** with EFS:
   ```yaml
   # apprunner.yaml
   version: 1.0
   runtime: python3
   build:
     commands:
       build:
         - echo "No build needed for pre-built image"
   run:
     runtime-version: 8
     command: dotnet TokenRelay.dll
     network:
       vpc-connector:
         name: my-vpc-connector
         security-groups: [sg-12345678]
     storage:
       - name: certs
         mount-path: /home/site/certs
         efs:
           file-system-id: fs-12345678
           access-point-id: fsap-12345678
   ```

3. **Upload certificates to EFS** via EC2 instance or EFS File Browser

### AWS Elastic Beanstalk

1. **Create `.ebextensions/efs-mount.config`**:
   ```yaml
   Resources:
     MountTargetSecurityGroup:
       Type: AWS::EC2::SecurityGroup
       Properties:
         GroupDescription: EFS Mount Target Security Group
         VpcId: !Ref "VPCID"
         SecurityGroupIngress:
           - IpProtocol: tcp
             FromPort: 2049
             ToPort: 2049
             SourceSecurityGroupId: !Ref "AWSEBSecurityGroup"
   
   files:
     "/etc/efs-mount.sh":
       mode: "000755"
       owner: root
       group: root
       content: |
         #!/bin/bash
         EFS_ID=fs-12345678
         EFS_MOUNT=/home/site/certs
         mkdir -p ${EFS_MOUNT}
         mount -t efs ${EFS_ID}:/ ${EFS_MOUNT}
   
   commands:
     01_mount_efs:
       command: /etc/efs-mount.sh
   ```

2. **Add certificates to EFS** and deploy your application

### Azure App Service (Linux Container)

1. **Create an Azure File Share** in your Storage Account

2. **Upload certificates** to the file share:
   ```bash
   az storage file upload --account-name mystorageaccount \
     --share-name tokenrelay-certs \
     --source ./my-ca.crt \
     --path certs/my-ca.crt
   ```

3. **Mount the file share** in App Service:
   ```bash
   az webapp config storage-account add \
     --resource-group myResourceGroup \
     --name myAppServiceName \
     --custom-id certs \
     --storage-type AzureFiles \
     --share-name tokenrelay-certs \
     --account-name mystorageaccount \
     --mount-path /home/site/certs \
     --access-key "<storage-account-key>"
   ```

### Kubernetes

1. **Create a ConfigMap** with your certificate:
   ```bash
   kubectl create configmap tokenrelay-certs \
     --from-file=my-ca.crt=./my-ca.crt \
     --namespace=default
   ```

2. **Update your Deployment** to mount the ConfigMap:
   ```yaml
   apiVersion: apps/v1
   kind: Deployment
   metadata:
     name: tokenrelay
   spec:
     template:
       spec:
         containers:
         - name: tokenrelay
           image: infigosoftware/tokenrelay:1.0.6
           volumeMounts:
           - name: certs
             mountPath: /home/site/certs
             readOnly: true
         volumes:
         - name: certs
           configMap:
             name: tokenrelay-certs
   ```

   **Or use a Secret** for more sensitive certificates:
   ```bash
   kubectl create secret generic tokenrelay-certs \
     --from-file=my-ca.crt=./my-ca.crt \
     --namespace=default
   ```

   ```yaml
   volumes:
   - name: certs
     secret:
       secretName: tokenrelay-certs
   ```

## Technical Details

### Certificate Loading Flow

1. Container starts as root
2. Startup script (`/app/entrypoint.sh`) executes
3. Script checks `/home/site/certs` for `.crt` files
4. If found:
   - Copies to `/usr/local/share/ca-certificates/`
   - Runs `update-ca-certificates`
   - Logs success message
5. If not found:
   - Logs skip message
   - Continues normally
6. Switches to `tokenrelay` user via `runuser`
7. Starts TokenRelay application

### Security Considerations

- Certificates directory is mounted read-only (`:ro`)
- Certificate operations run as root (required for system trust store)
- Application runs as non-root `tokenrelay` user
- Invalid certificates are silently skipped
- Only `.crt` files are processed

### Backward Compatibility

This change is **fully backward compatible**:
- If no certificates are present, the script skips certificate loading
- Existing deployments work without modification
- The feature is entirely optional

## Files Modified

1. `docker/Dockerfile` - Added certificate loading logic
2. `docker/docker-compose.yml` - Added volume mount
3. `docker/docker-compose-new.yml` - Added volume mount
4. `docker/env.template` - Added TOKENRELAY_CERTS_PATH variable
5. `docker/README.md` - Added usage documentation
6. `.gitignore` - Added certificate file exclusions

## Files Created

1. `docker/certs/README.md` - Comprehensive certificate guide
2. `docker/certs/.gitkeep` - Ensures directory is tracked in git

## Testing Recommendations

1. **Test without certificates** (default behavior):
   ```bash
   docker-compose up -d
   docker-compose logs tokenrelay | grep certificate
   ```
   Expected: "No custom certificates found..."

2. **Test with certificates**:
   ```bash
   cp test-cert.crt docker/certs/
   docker-compose restart
   docker-compose logs tokenrelay | grep certificate
   ```
   Expected: "Loading custom certificates..." and "Certificate loading completed."

3. **Test certificate trust**:
   ```bash
   docker-compose exec tokenrelay curl -v https://your-api-with-custom-cert.com
   ```
   Expected: Successful connection without SSL errors

## Troubleshooting

### Certificates not loading

**Check logs:**
```bash
docker-compose logs tokenrelay | grep -i cert
```

**Verify file permissions:**
```bash
ls -la docker/certs/
```

**Check file extension:**
- Must be `.crt` (not `.pem`, `.cer`, etc.)

### Container won't start

**Check entrypoint script:**
```bash
docker-compose exec tokenrelay cat /app/entrypoint.sh
```

**Test manually:**
```bash
docker-compose exec tokenrelay /bin/sh /app/entrypoint.sh
```

### Certificate errors in application logs

**Verify certificate format:**
```bash
openssl x509 -in docker/certs/my-cert.crt -text -noout
```

**Check certificate is trusted:**
```bash
docker-compose exec tokenrelay cat /etc/ssl/certs/ca-certificates.crt | grep "your-ca-name"
```

## Future Enhancements

Potential improvements for future versions:
- Support additional certificate formats (`.pem`, `.cer`)
- Add certificate validation before loading
- Implement certificate hot-reload without restart
- Add health check for certificate validity
- Support for certificate bundles
