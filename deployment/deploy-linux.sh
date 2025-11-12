#!/bin/bash

# TokenRelay Linux Deployment Script
# This script helps deploy TokenRelay on Linux systems

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
INSTALL_DIR="/opt/tokenrelay"
SERVICE_USER="tokenrelay"
SERVICE_GROUP="tokenrelay"
SERVICE_NAME="tokenrelay"

echo -e "${GREEN}🚀 TokenRelay Linux Deployment Script${NC}"
echo -e "${GREEN}====================================${NC}"
echo

# Check if running as root
if [[ $EUID -ne 0 ]]; then
    echo -e "${RED}❌ This script must be run as root (use sudo)${NC}"
    exit 1
fi

# Function to print step headers
print_step() {
    echo -e "${BLUE}📋 $1${NC}"
}

# Function to print success messages
print_success() {
    echo -e "${GREEN}✅ $1${NC}"
}

# Function to print warnings
print_warning() {
    echo -e "${YELLOW}⚠️ $1${NC}"
}

# Function to print errors
print_error() {
    echo -e "${RED}❌ $1${NC}"
}

# Detect Linux distribution
detect_distro() {
    if [ -f /etc/os-release ]; then
        . /etc/os-release
        DISTRO=$ID
        VERSION=$VERSION_ID
    else
        DISTRO="unknown"
    fi
    
    print_step "Detected OS: $DISTRO $VERSION"
}

# Install .NET runtime if not present
install_dotnet() {
    print_step "Checking .NET installation..."
    
    if command -v dotnet &> /dev/null; then
        DOTNET_VERSION=$(dotnet --version)
        echo "Found .NET version: $DOTNET_VERSION"
        
        # Check if version is 8.0 or higher
        MAJOR_VERSION=$(echo $DOTNET_VERSION | cut -d. -f1)
        if [ "$MAJOR_VERSION" -ge 8 ]; then
            print_success ".NET 8.0+ is already installed"
            return 0
        else
            print_warning "Found .NET $DOTNET_VERSION, but need 8.0+"
        fi
    fi
    
    print_step "Installing .NET 8.0 runtime..."
    
    case $DISTRO in
        ubuntu|debian)
            # Add Microsoft package repository
            wget https://packages.microsoft.com/config/$DISTRO/$VERSION/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
            dpkg -i packages-microsoft-prod.deb
            rm packages-microsoft-prod.deb
            
            # Update package list and install
            apt-get update
            apt-get install -y aspnetcore-runtime-8.0
            ;;
        centos|rhel|fedora)
            # Add Microsoft package repository
            rpm -Uvh https://packages.microsoft.com/config/$DISTRO/$VERSION/packages-microsoft-prod.rpm
            
            # Install .NET
            if command -v dnf &> /dev/null; then
                dnf install -y aspnetcore-runtime-8.0
            else
                yum install -y aspnetcore-runtime-8.0
            fi
            ;;
        *)
            print_error "Unsupported distribution: $DISTRO"
            print_warning "Please install .NET 8.0 runtime manually from:"
            print_warning "https://docs.microsoft.com/en-us/dotnet/core/install/linux"
            exit 1
            ;;
    esac
    
    print_success ".NET runtime installed successfully"
}

# Create service user
create_service_user() {
    print_step "Creating service user..."
    
    if id "$SERVICE_USER" &>/dev/null; then
        print_success "User $SERVICE_USER already exists"
    else
        useradd --system --home-dir $INSTALL_DIR --shell /usr/sbin/nologin $SERVICE_USER
        print_success "Created user $SERVICE_USER"
    fi
}

# Create installation directory
create_install_dir() {
    print_step "Creating installation directory..."
    
    mkdir -p $INSTALL_DIR
    mkdir -p $INSTALL_DIR/uploads
    mkdir -p $INSTALL_DIR/logs
    
    chown -R $SERVICE_USER:$SERVICE_GROUP $INSTALL_DIR
    chmod 755 $INSTALL_DIR
    chmod 755 $INSTALL_DIR/uploads
    chmod 755 $INSTALL_DIR/logs
    
    print_success "Installation directory created: $INSTALL_DIR"
}

# Copy application files
copy_application() {
    print_step "Copying application files..."
    
    PUBLISH_DIR="./publish"
    
    if [ ! -d "$PUBLISH_DIR" ]; then
        print_error "Publish directory not found: $PUBLISH_DIR"
        print_warning "Please run the publish script first:"
        print_warning "  ./publish-standalone.ps1 -Runtime linux-x64 -SelfContained \$true"
        exit 1
    fi
    
    # Determine which subdirectory to use
    if [ -d "$PUBLISH_DIR/linux-x64" ]; then
        SOURCE_DIR="$PUBLISH_DIR/linux-x64"
    elif [ -d "$PUBLISH_DIR/portable" ]; then
        SOURCE_DIR="$PUBLISH_DIR/portable"
    else
        print_error "No suitable publish output found in $PUBLISH_DIR"
        exit 1
    fi
    
    print_step "Copying from $SOURCE_DIR to $INSTALL_DIR"
    
    # Copy files
    cp -r $SOURCE_DIR/* $INSTALL_DIR/
    
    # Set permissions
    chown -R $SERVICE_USER:$SERVICE_GROUP $INSTALL_DIR
    chmod +x $INSTALL_DIR/start.sh
    
    # Make executable if self-contained
    if [ -f "$INSTALL_DIR/TokenRelay" ]; then
        chmod +x $INSTALL_DIR/TokenRelay
        print_success "Set executable permissions for self-contained deployment"
    fi
    
    print_success "Application files copied successfully"
}

# Setup configuration
setup_configuration() {
    print_step "Setting up configuration..."
    
    CONFIG_FILE="$INSTALL_DIR/tokenrelay.json"
    TEMPLATE_FILE="$INSTALL_DIR/tokenrelay.template.json"
    
    if [ -f "$CONFIG_FILE" ]; then
        print_warning "Configuration file already exists: $CONFIG_FILE"
        read -p "Do you want to backup and replace it? [y/N]: " -n 1 -r
        echo
        if [[ $REPLY =~ ^[Yy]$ ]]; then
            cp "$CONFIG_FILE" "$CONFIG_FILE.backup.$(date +%Y%m%d_%H%M%S)"
            print_success "Backed up existing configuration"
        else
            print_success "Keeping existing configuration"
            return 0
        fi
    fi
    
    if [ -f "$TEMPLATE_FILE" ]; then
        cp "$TEMPLATE_FILE" "$CONFIG_FILE"
        chown $SERVICE_USER:$SERVICE_GROUP "$CONFIG_FILE"
        chmod 600 "$CONFIG_FILE"
        print_success "Created configuration file from template"
        print_warning "Please edit $CONFIG_FILE before starting the service"
    else
        print_error "Configuration template not found: $TEMPLATE_FILE"
        print_warning "Please create $CONFIG_FILE manually"
    fi
}

# Install systemd service
install_service() {
    print_step "Installing systemd service..."
    
    SERVICE_FILE="/etc/systemd/system/$SERVICE_NAME.service"
    TEMPLATE_SERVICE="$INSTALL_DIR/tokenrelay.service"
    
    if [ -f "$TEMPLATE_SERVICE" ]; then
        cp "$TEMPLATE_SERVICE" "$SERVICE_FILE"
    else
        print_warning "Service template not found, creating basic service file..."
        
        cat > "$SERVICE_FILE" << EOF
[Unit]
Description=TokenRelay Proxy Service
After=network.target

[Service]
Type=notify
User=$SERVICE_USER
Group=$SERVICE_GROUP
WorkingDirectory=$INSTALL_DIR
ExecStart=$INSTALL_DIR/TokenRelay
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=$SERVICE_NAME
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://localhost:5163
Environment=ConfigPath=$INSTALL_DIR/tokenrelay.json

[Install]
WantedBy=multi-user.target
EOF
    fi
    
    # Reload systemd and enable service
    systemctl daemon-reload
    systemctl enable $SERVICE_NAME
    
    print_success "Service installed and enabled"
}

# Configure firewall
configure_firewall() {
    print_step "Configuring firewall..."
    
    if command -v ufw &> /dev/null; then
        # UFW (Ubuntu/Debian)
        ufw allow 5163/tcp comment "TokenRelay HTTP"
        ufw allow 7102/tcp comment "TokenRelay HTTPS"
        print_success "UFW firewall rules added"
    elif command -v firewall-cmd &> /dev/null; then
        # firewalld (CentOS/RHEL/Fedora)
        firewall-cmd --permanent --add-port=5163/tcp
        firewall-cmd --permanent --add-port=7102/tcp
        firewall-cmd --reload
        print_success "firewalld rules added"
    else
        print_warning "No supported firewall found. Please manually open ports 5163 and 7102"
    fi
}

# Main deployment function
main() {
    detect_distro
    
    echo
    echo "This script will:"
    echo "1. Install .NET 8.0 runtime (if needed)"
    echo "2. Create service user ($SERVICE_USER)"
    echo "3. Create installation directory ($INSTALL_DIR)"
    echo "4. Copy application files"
    echo "5. Setup configuration"
    echo "6. Install systemd service"
    echo "7. Configure firewall"
    echo
    
    read -p "Continue with deployment? [y/N]: " -n 1 -r
    echo
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        echo "Deployment cancelled."
        exit 0
    fi
    
    echo
    install_dotnet
    echo
    create_service_user
    echo
    create_install_dir
    echo
    copy_application
    echo
    setup_configuration
    echo
    install_service
    echo
    configure_firewall
    
    echo
    print_success "🎉 TokenRelay deployment completed successfully!"
    echo
    echo -e "${YELLOW}Next steps:${NC}"
    echo "1. Edit the configuration file: $INSTALL_DIR/tokenrelay.json"
    echo "2. Start the service: sudo systemctl start $SERVICE_NAME"
    echo "3. Check status: sudo systemctl status $SERVICE_NAME"
    echo "4. View logs: sudo journalctl -u $SERVICE_NAME -f"
    echo
    echo -e "${YELLOW}Service URLs:${NC}"
    echo "- HTTP: http://localhost:5163"
    echo "- HTTPS: https://localhost:7102"
    echo "- Health: http://localhost:5163/health"
    echo
}

# Run main function
main "$@"
