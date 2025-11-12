# AWS Deployment Guide - Custom Certificates

This guide explains how to deploy TokenRelay on various AWS services with custom SSL/TLS certificates.

## Overview

TokenRelay supports loading custom certificates from persistent storage. On AWS, this is typically achieved using **Amazon EFS (Elastic File System)**, which provides shared, persistent storage for containers.

## AWS Service Options

### 1. AWS ECS/Fargate (Recommended for Production)

**Best for:** Production workloads, auto-scaling, managed infrastructure

#### Prerequisites
- EFS file system in the same VPC as your ECS cluster
- Security groups configured to allow NFS traffic (port 2049)

#### Step 1: Create EFS File System

```bash
# Create EFS file system
aws efs create-file-system \
  --performance-mode generalPurpose \
  --throughput-mode bursting \
  --encrypted \
  --tags Key=Name,Value=tokenrelay-storage \
  --region us-east-1

# Note the FileSystemId (e.g., fs-12345678)
```

#### Step 2: Create Mount Targets

```bash
# Create mount target in each availability zone
aws efs create-mount-target \
  --file-system-id fs-12345678 \
  --subnet-id subnet-xxxxxxxx \
  --security-groups sg-xxxxxxxx
```

#### Step 3: Upload Certificates to EFS

Option A: Via EC2 Instance
```bash
# Launch EC2 instance in same VPC
# SSH into instance and mount EFS
sudo yum install -y amazon-efs-utils
sudo mkdir -p /mnt/efs
sudo mount -t efs fs-12345678:/ /mnt/efs

# Create directory and upload certificates
sudo mkdir -p /mnt/efs/tokenrelay/certs
sudo cp my-ca.crt /mnt/efs/tokenrelay/certs/
sudo chmod 644 /mnt/efs/tokenrelay/certs/*.crt
```

Option B: Via EFS File Browser (Console)
1. Navigate to EFS in AWS Console
2. Select your file system
3. Click "File Browser" and create directory structure
4. Upload certificate files

#### Step 4: Create ECS Task Definition

```json
{
  "family": "tokenrelay",
  "networkMode": "awsvpc",
  "requiresCompatibilities": ["FARGATE"],
  "cpu": "512",
  "memory": "1024",
  "containerDefinitions": [
    {
      "name": "tokenrelay",
      "image": "infigosoftware/tokenrelay:1.0.6",
      "portMappings": [
        {
          "containerPort": 80,
          "protocol": "tcp"
        }
      ],
      "environment": [
        {
          "name": "ASPNETCORE_ENVIRONMENT",
          "value": "Production"
        },
        {
          "name": "ConfigPath",
          "value": "/app/tokenrelay.json"
        }
      ],
      "mountPoints": [
        {
          "sourceVolume": "tokenrelay-config",
          "containerPath": "/app/tokenrelay.json",
          "readOnly": true
        },
        {
          "sourceVolume": "tokenrelay-certs",
          "containerPath": "/home/site/certs",
          "readOnly": true
        },
        {
          "sourceVolume": "tokenrelay-logs",
          "containerPath": "/app/logs",
          "readOnly": false
        }
      ],
      "logConfiguration": {
        "logDriver": "awslogs",
        "options": {
          "awslogs-group": "/ecs/tokenrelay",
          "awslogs-region": "us-east-1",
          "awslogs-stream-prefix": "ecs"
        }
      }
    }
  ],
  "volumes": [
    {
      "name": "tokenrelay-config",
      "efsVolumeConfiguration": {
        "fileSystemId": "fs-12345678",
        "rootDirectory": "/tokenrelay/config/tokenrelay.json",
        "transitEncryption": "ENABLED"
      }
    },
    {
      "name": "tokenrelay-certs",
      "efsVolumeConfiguration": {
        "fileSystemId": "fs-12345678",
        "rootDirectory": "/tokenrelay/certs",
        "transitEncryption": "ENABLED"
      }
    },
    {
      "name": "tokenrelay-logs",
      "efsVolumeConfiguration": {
        "fileSystemId": "fs-12345678",
        "rootDirectory": "/tokenrelay/logs",
        "transitEncryption": "ENABLED"
      }
    }
  ],
  "executionRoleArn": "arn:aws:iam::123456789012:role/ecsTaskExecutionRole",
  "taskRoleArn": "arn:aws:iam::123456789012:role/ecsTaskRole"
}
```

#### Step 5: Create ECS Service

```bash
aws ecs create-service \
  --cluster my-cluster \
  --service-name tokenrelay \
  --task-definition tokenrelay:1 \
  --desired-count 2 \
  --launch-type FARGATE \
  --network-configuration "awsvpcConfiguration={subnets=[subnet-xxx,subnet-yyy],securityGroups=[sg-xxx],assignPublicIp=ENABLED}"
```

---

### 2. AWS App Runner

**Best for:** Simple container deployments, auto-scaling web apps

#### Step 1: Create EFS File System
(Same as ECS Step 1-3 above)

#### Step 2: Create VPC Connector

```bash
aws apprunner create-vpc-connector \
  --vpc-connector-name tokenrelay-vpc-connector \
  --subnets subnet-xxx subnet-yyy \
  --security-groups sg-xxx
```

#### Step 3: Create App Runner Service

```bash
aws apprunner create-service \
  --service-name tokenrelay \
  --source-configuration '{
    "ImageRepository": {
      "ImageIdentifier": "infigosoftware/tokenrelay:1.0.6",
      "ImageRepositoryType": "ECR_PUBLIC",
      "ImageConfiguration": {
        "Port": "80",
        "RuntimeEnvironmentVariables": {
          "ASPNETCORE_ENVIRONMENT": "Production"
        }
      }
    },
    "AutoDeploymentsEnabled": false
  }' \
  --instance-configuration '{
    "Cpu": "1 vCPU",
    "Memory": "2 GB"
  }' \
  --network-configuration '{
    "EgressConfiguration": {
      "EgressType": "VPC",
      "VpcConnectorArn": "arn:aws:apprunner:us-east-1:123456789012:vpcconnector/tokenrelay-vpc-connector"
    }
  }' \
  --observability-configuration '{
    "ObservabilityEnabled": true
  }'
```

**Note:** App Runner EFS integration is configured via console or CloudFormation (not yet available in CLI).

---

### 3. AWS Elastic Beanstalk

**Best for:** Traditional application deployment, easy management

#### Step 1: Create `.ebextensions/01-efs-mount.config`

```yaml
option_settings:
  aws:elasticbeanstalk:application:environment:
    EFS_ID: fs-12345678
    EFS_MOUNT_DIR: /home/site/certs

files:
  "/opt/elasticbeanstalk/hooks/appdeploy/pre/01_mount_efs.sh":
    mode: "000755"
    owner: root
    group: root
    content: |
      #!/bin/bash
      # Install amazon-efs-utils if not present
      if ! rpm -qa | grep -q amazon-efs-utils; then
        yum install -y amazon-efs-utils
      fi
      
      # Create mount directory
      mkdir -p /home/site/certs
      
      # Mount EFS
      if ! mount | grep -q /home/site/certs; then
        mount -t efs -o tls ${EFS_ID}:/ /home/site/certs
      fi
      
      # Ensure it's mounted on reboot
      if ! grep -q ${EFS_ID} /etc/fstab; then
        echo "${EFS_ID}:/ /home/site/certs efs _netdev,tls 0 0" >> /etc/fstab
      fi

Resources:
  # Security group for EFS mount target
  MountTargetSecurityGroup:
    Type: AWS::EC2::SecurityGroup
    Properties:
      GroupDescription: Security group for EFS mount target
      VpcId: !Ref "VPC"
      SecurityGroupIngress:
        - IpProtocol: tcp
          FromPort: 2049
          ToPort: 2049
          SourceSecurityGroupId: !Ref "AWSEBSecurityGroup"
```

#### Step 2: Create `Dockerrun.aws.json`

```json
{
  "AWSEBDockerrunVersion": "1",
  "Image": {
    "Name": "infigosoftware/tokenrelay:1.0.6",
    "Update": "true"
  },
  "Ports": [
    {
      "ContainerPort": 80
    }
  ],
  "Volumes": [
    {
      "HostDirectory": "/home/site/certs",
      "ContainerDirectory": "/home/site/certs"
    }
  ],
  "Logging": "/app/logs"
}
```

#### Step 3: Deploy

```bash
eb init -p docker tokenrelay
eb create tokenrelay-env
eb deploy
```

---

### 4. AWS Lambda (Alternative - Custom Runtime)

**Note:** Lambda doesn't support EFS mounting the same way, but you can:

#### Option A: Package Certificates in Deployment
```bash
# Include certificates in deployment package
mkdir -p certs
cp my-ca.crt certs/
zip -r tokenrelay.zip . -x "*.git*"
```

#### Option B: Use Lambda Layers
```bash
# Create layer with certificates
mkdir -p layer/certs
cp my-ca.crt layer/certs/
cd layer && zip -r ../certs-layer.zip .
aws lambda publish-layer-version \
  --layer-name tokenrelay-certs \
  --zip-file fileb://../certs-layer.zip
```

---

## Security Best Practices

### IAM Policies

#### ECS Task Execution Role
```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "elasticfilesystem:ClientMount",
        "elasticfilesystem:ClientWrite",
        "elasticfilesystem:ClientRootAccess"
      ],
      "Resource": "arn:aws:elasticfilesystem:us-east-1:123456789012:file-system/fs-12345678"
    }
  ]
}
```

### EFS Security

1. **Enable encryption at rest and in transit**
2. **Use security groups** to restrict NFS access (port 2049)
3. **Use IAM authentication** for EFS access points
4. **Set proper file permissions** (644 for certificates)

### Network Security

```bash
# Security group for ECS tasks
aws ec2 authorize-security-group-ingress \
  --group-id sg-ecs-tasks \
  --protocol tcp \
  --port 2049 \
  --source-group sg-efs-mount

# Security group for EFS mount targets
aws ec2 authorize-security-group-ingress \
  --group-id sg-efs-mount \
  --protocol tcp \
  --port 2049 \
  --source-group sg-ecs-tasks
```

---

## Verification and Troubleshooting

### Check Certificate Loading in ECS

```bash
# View logs
aws logs tail /ecs/tokenrelay --follow --format short

# Look for:
# "Loading custom certificates from /home/site/certs..."
# "Certificate loading completed."
```

### Test from Container

```bash
# Execute shell in running container
aws ecs execute-command \
  --cluster my-cluster \
  --task task-id \
  --container tokenrelay \
  --interactive \
  --command "/bin/bash"

# Inside container:
ls -la /home/site/certs/
ls -la /usr/local/share/ca-certificates/
curl -v https://your-api-with-custom-cert.com
```

### Common Issues

#### EFS Mount Fails
- **Check security groups:** Ensure NFS traffic (2049) is allowed
- **Check VPC/subnets:** EFS mount targets must be in same VPC
- **Check IAM permissions:** Task role needs EFS permissions

#### Certificates Not Loading
- **Check file permissions:** Must be readable (644)
- **Check file extension:** Must be `.crt`
- **Check EFS mount:** Verify files exist in EFS

#### Connection Issues
- **Check VPC configuration:** Ensure tasks can reach target APIs
- **Check NAT Gateway:** Required for outbound internet access in private subnets
- **Check route tables:** Verify routing configuration

---

## Cost Optimization

### EFS Pricing
- **Storage:** ~$0.30/GB/month (Standard)
- **Storage:** ~$0.025/GB/month (Infrequent Access)
- **Data transfer:** Free within same AZ

### Recommendations
1. Use **EFS Infrequent Access** for static certificate storage
2. Use **EFS lifecycle policies** to move old logs to IA storage
3. Monitor **CloudWatch metrics** for EFS usage
4. Consider **EFS Provisioned Throughput** only if needed

---

## Example: Complete ECS Deployment with CloudFormation

See `aws-ecs-tokenrelay.yaml` (to be created) for a complete CloudFormation template that includes:
- VPC and networking
- EFS file system
- ECS cluster and service
- Application Load Balancer
- Security groups
- IAM roles

---

## Additional Resources

- [AWS ECS with EFS](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/efs-volumes.html)
- [AWS App Runner VPC Support](https://docs.aws.amazon.com/apprunner/latest/dg/network-vpc.html)
- [EFS Security Best Practices](https://docs.aws.amazon.com/efs/latest/ug/security-considerations.html)
- [ECS Task Execution IAM Role](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/task_execution_IAM_role.html)
