# Certificate Storage Paths - Quick Reference

This document provides a quick reference for certificate storage paths across different deployment platforms.

## Container Path (All Platforms)

**Certificate Directory:** `/home/site/certs`

This is the path inside the container where TokenRelay looks for `.crt` certificate files.

---

## Platform-Specific Persistent Storage

### Docker / Docker Compose

**Host Path:** `./docker/certs/`  
**Container Mount:** `/home/site/certs`  
**Configuration:**
```yaml
volumes:
  - ./certs:/home/site/certs:ro
```

**Usage:**
```bash
cp my-ca.crt docker/certs/
docker-compose restart
```

---

### Azure App Service (Linux Containers)

**Storage Type:** Azure Files (SMB)  
**Mount Path:** `/home/site/certs`  
**Setup:**
```bash
# Create file share
az storage share create --name tokenrelay-certs

# Upload certificate
az storage file upload \
  --account-name mystorageaccount \
  --share-name tokenrelay-certs \
  --source ./my-ca.crt \
  --path certs/my-ca.crt

# Mount in App Service
az webapp config storage-account add \
  --resource-group myRG \
  --name myApp \
  --custom-id certs \
  --storage-type AzureFiles \
  --share-name tokenrelay-certs \
  --mount-path /home/site/certs
```

**Documentation:** [Azure App Service Storage](https://learn.microsoft.com/en-us/azure/app-service/configure-custom-container#use-persistent-shared-storage)

---

### Azure Container Apps

**Storage Type:** Azure Files  
**Mount Path:** `/home/site/certs`  
**Setup:**
```bash
# Create storage
az containerapp env storage set \
  --name my-environment \
  --resource-group myRG \
  --storage-name certs \
  --azure-file-account-name mystorageaccount \
  --azure-file-account-key $KEY \
  --azure-file-share-name tokenrelay-certs \
  --access-mode ReadOnly

# Add volume mount to container app
az containerapp update \
  --name tokenrelay \
  --resource-group myRG \
  --set-env-vars "ASPNETCORE_ENVIRONMENT=Production" \
  --add-volume certs storage certs \
  --add-volume-mount certs /home/site/certs
```

---

### AWS ECS / Fargate

**Storage Type:** Amazon EFS  
**Mount Path:** `/home/site/certs`  
**EFS Path:** `/tokenrelay/certs/` (recommended)  
**Task Definition:**
```json
{
  "volumes": [{
    "name": "tokenrelay-certs",
    "efsVolumeConfiguration": {
      "fileSystemId": "fs-12345678",
      "rootDirectory": "/tokenrelay/certs",
      "transitEncryption": "ENABLED"
    }
  }],
  "containerDefinitions": [{
    "mountPoints": [{
      "sourceVolume": "tokenrelay-certs",
      "containerPath": "/home/site/certs",
      "readOnly": true
    }]
  }]
}
```

**Upload to EFS:**
```bash
# Mount EFS on EC2
sudo mount -t efs fs-12345678:/ /mnt/efs
sudo mkdir -p /mnt/efs/tokenrelay/certs
sudo cp my-ca.crt /mnt/efs/tokenrelay/certs/
```

**Documentation:** See [AWS-DEPLOYMENT-CERTIFICATES.md](AWS-DEPLOYMENT-CERTIFICATES.md)

---

### AWS App Runner

**Storage Type:** Amazon EFS  
**Mount Path:** `/home/site/certs`  
**Setup:** Configure via Console or CloudFormation (EFS support limited in CLI)

**apprunner.yaml:**
```yaml
storage:
  - name: certs
    mount-path: /home/site/certs
    efs:
      file-system-id: fs-12345678
      access-point-id: fsap-12345678
```

---

### AWS Elastic Beanstalk

**Storage Type:** Amazon EFS  
**Host Mount:** `/home/site/certs`  
**Container Mount:** `/home/site/certs`  
**Setup:** Configure via `.ebextensions/efs-mount.config`

```yaml
files:
  "/opt/elasticbeanstalk/hooks/appdeploy/pre/mount_efs.sh":
    content: |
      #!/bin/bash
      mkdir -p /home/site/certs
      mount -t efs fs-12345678:/ /home/site/certs
```

**Dockerrun.aws.json:**
```json
{
  "Volumes": [{
    "HostDirectory": "/home/site/certs",
    "ContainerDirectory": "/home/site/certs"
  }]
}
```

---

### Google Cloud Run

**Storage Type:** Cloud Storage FUSE (beta) or Secret Manager  
**Mount Path:** `/home/site/certs`  

**Option 1: Using Secrets:**
```bash
# Create secret with certificate
gcloud secrets create tokenrelay-ca-cert \
  --data-file=my-ca.crt

# Deploy with secret mounted
gcloud run deploy tokenrelay \
  --image infigosoftware/tokenrelay:1.0.6 \
  --set-secrets=/home/site/certs/my-ca.crt=tokenrelay-ca-cert:latest
```

**Option 2: Using Cloud Storage FUSE (beta):**
```bash
gcloud run deploy tokenrelay \
  --image infigosoftware/tokenrelay:1.0.6 \
  --add-volume name=certs,type=cloud-storage,bucket=my-certs-bucket \
  --add-volume-mount volume=certs,mount-path=/home/site/certs
```

---

### Kubernetes

**Storage Type:** ConfigMap, Secret, or PersistentVolume  
**Mount Path:** `/home/site/certs`

**Option 1: ConfigMap (for non-sensitive certs):**
```bash
kubectl create configmap tokenrelay-certs \
  --from-file=my-ca.crt=./my-ca.crt
```

```yaml
volumes:
  - name: certs
    configMap:
      name: tokenrelay-certs
volumeMounts:
  - name: certs
    mountPath: /home/site/certs
    readOnly: true
```

**Option 2: Secret (for sensitive certs):**
```bash
kubectl create secret generic tokenrelay-certs \
  --from-file=my-ca.crt=./my-ca.crt
```

```yaml
volumes:
  - name: certs
    secret:
      secretName: tokenrelay-certs
```

**Option 3: PersistentVolume:**
```yaml
volumes:
  - name: certs
    persistentVolumeClaim:
      claimName: tokenrelay-certs-pvc
```

---

### OpenShift

**Storage Type:** ConfigMap, Secret, or PersistentVolumeClaim  
**Mount Path:** `/home/site/certs`

**Same as Kubernetes**, but use `oc` commands:
```bash
oc create configmap tokenrelay-certs --from-file=my-ca.crt
oc set volume deployment/tokenrelay \
  --add --type=configmap \
  --configmap-name=tokenrelay-certs \
  --mount-path=/home/site/certs
```

---

### Docker Swarm

**Storage Type:** Docker Config or Volume  
**Mount Path:** `/home/site/certs`

**Using Docker Config:**
```bash
docker config create my-ca-cert my-ca.crt

docker service create \
  --name tokenrelay \
  --config source=my-ca-cert,target=/home/site/certs/my-ca.crt \
  infigosoftware/tokenrelay:1.0.6
```

**Using Volume:**
```bash
docker service create \
  --name tokenrelay \
  --mount type=bind,source=/path/to/certs,target=/home/site/certs,readonly \
  infigosoftware/tokenrelay:1.0.6
```

---

### Nomad

**Storage Type:** Host Volume or CSI Volume  
**Mount Path:** `/home/site/certs`

```hcl
job "tokenrelay" {
  group "app" {
    volume "certs" {
      type      = "host"
      source    = "tokenrelay-certs"
      read_only = true
    }
    
    task "tokenrelay" {
      driver = "docker"
      
      config {
        image = "infigosoftware/tokenrelay:1.0.6"
      }
      
      volume_mount {
        volume      = "certs"
        destination = "/home/site/certs"
        read_only   = true
      }
    }
  }
}
```

---

## Certificate File Requirements

**Across All Platforms:**

1. **File Extension:** `.crt` (PEM-encoded X.509)
2. **Format:** PEM (Base64-encoded with BEGIN/END markers)
3. **Permissions:** Readable by container user (644 recommended)
4. **Location:** Files must be in the `/home/site/certs` directory
5. **Multiple Files:** Multiple `.crt` files are supported

**Example Certificate Format:**
```
-----BEGIN CERTIFICATE-----
MIIDXTCCAkWgAwIBAgIJAKHHCgVZU2XtMA0GCSqGSIb3DQEBCwUAMEUxCzAJ...
...
-----END CERTIFICATE-----
```

---

## Verification Commands

**Check if certificates are loaded (all platforms):**

```bash
# View container logs
docker logs <container-id> 2>&1 | grep -i certificate

# Expected output:
# "Loading custom certificates from /home/site/certs..."
# "Certificate loading completed."
```

**Test certificate trust:**

```bash
# Exec into container
docker exec -it <container-id> /bin/bash

# Check certificates are present
ls -la /home/site/certs/

# Check certificates are installed
ls -la /usr/local/share/ca-certificates/

# Test HTTPS connection
curl -v https://your-api-with-custom-cert.com
```

---

## Troubleshooting Matrix

| Issue | Docker | Azure | AWS ECS | Kubernetes |
|-------|--------|-------|---------|------------|
| **Certs not loading** | Check volume mount in compose | Check storage mount | Check EFS mount | Check ConfigMap/Secret |
| **Permission denied** | Check file permissions (644) | Check storage account key | Check security groups | Check pod security context |
| **Path not found** | Check host path exists | Check file share exists | Check EFS file system | Check volume definition |
| **Still getting SSL errors** | Restart container | Restart app | Restart task | Delete pod |

---

## Quick Commands by Platform

### Local Docker
```bash
# Add certificate
cp my-ca.crt docker/certs/
docker-compose restart tokenrelay

# Verify
docker-compose logs tokenrelay | grep certificate
```

### Azure
```bash
# Upload certificate
az storage file upload --share-name tokenrelay-certs --source my-ca.crt --path certs/my-ca.crt

# Restart app
az webapp restart --name myapp --resource-group myRG
```

### AWS ECS
```bash
# Upload to EFS (via EC2)
sudo cp my-ca.crt /mnt/efs/tokenrelay/certs/

# Force new deployment
aws ecs update-service --cluster my-cluster --service tokenrelay --force-new-deployment
```

### Kubernetes
```bash
# Update ConfigMap
kubectl create configmap tokenrelay-certs --from-file=my-ca.crt --dry-run=client -o yaml | kubectl apply -f -

# Restart deployment
kubectl rollout restart deployment/tokenrelay
```

---

## Summary

**Key Points:**
- ✅ Container always looks in `/home/site/certs`
- ✅ Only `.crt` files are processed
- ✅ Certificates loaded before app starts
- ✅ Works with all major cloud platforms
- ✅ Supports multiple certificates
- ✅ Read-only mount recommended
- ✅ Fully optional feature

**Common Path:** `/home/site/certs` (consistent across all platforms)
