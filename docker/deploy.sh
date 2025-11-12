#!/bin/bash

# TokenRelay Docker Deployment Script

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

# Default values
VERSION="${TOKENRELAY_VERSION:-1.0.0-docker}"
BUILD_DATE="${BUILD_DATE:-$(date '+%Y-%m-%d %H:%M:%S')}"

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --version)
            VERSION="$2"
            shift 2
            ;;
        --build-date)
            BUILD_DATE="$2"
            shift 2
            ;;
        *)
            COMMAND="$1"
            shift
            ;;
    esac
done

# Export for docker-compose
export TOKENRELAY_VERSION="$VERSION"
export BUILD_DATE="$BUILD_DATE"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Functions
log_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

log_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

log_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Check prerequisites
check_prerequisites() {
    log_info "Checking prerequisites..."
    log_info "Version: $VERSION"
    log_info "Build Date: $BUILD_DATE"
    
    if ! command -v docker &> /dev/null; then
        log_error "Docker is not installed or not in PATH"
        exit 1
    fi
    
    if ! command -v docker-compose &> /dev/null; then
        log_error "Docker Compose is not installed or not in PATH"
        exit 1
    fi
    
    log_success "Prerequisites check passed"
}

# Setup environment
setup_environment() {
    log_info "Setting up environment..."
    
    cd "$SCRIPT_DIR"
    
    if [ ! -f ".env" ]; then
        if [ -f ".env.template" ]; then
            cp .env.template .env
            log_success "Created .env file from template"
            log_warning "Please edit .env file with your configuration before proceeding"
            return 1
        else
            log_error ".env.template not found"
            exit 1
        fi
    fi
    
    # Create necessary directories
    mkdir -p uploads logs
    
    # Check for configuration file
    local config_path="./config/tokenrelay.json"
    if [[ ! -f "$config_path" ]]; then
        log_warning "Configuration file not found: $config_path"
        
        if [[ -f "./config/tokenrelay.template.json" ]]; then
            log_info "Template configuration file found."
            echo
            log_error "SECURITY NOTICE:"
            echo "  The Docker image does NOT include tokenrelay.json for security reasons."
            echo "  You must create your own configuration file with proper credentials."
            echo
            echo -e "${BLUE}Options:${NC}"
            echo "  1. Copy template: cp ./config/tokenrelay.template.json $config_path"
            echo "  2. Use environment config: Set TOKENRELAY_CONFIG_MODE=env in .env"
            echo
            
            read -p "Copy template configuration? (y/N): " choice
            if [[ "$choice" == "y" || "$choice" == "Y" ]]; then
                cp "./config/tokenrelay.template.json" "$config_path"
                log_success "Copied template configuration to $config_path"
                log_warning "IMPORTANT: Edit $config_path with your actual credentials and endpoints!"
                return 1  # Don't proceed automatically, let user edit first
            else
                log_warning "Please create $config_path or use environment-based configuration"
                return 1
            fi
        else
            log_error "No configuration template found"
            return 1
        fi
    fi
    
    log_success "Environment setup completed"
    return 0
}

# Build and start services
start_services() {
    local with_nginx=$1
    
    log_info "Building and starting TokenRelay services..."
    
    cd "$SCRIPT_DIR"
    
    if [ "$with_nginx" = "true" ]; then
        docker-compose --profile with-nginx up -d --build
        log_success "TokenRelay started with Nginx reverse proxy"
        log_info "Access URLs:"
        log_info "  - TokenRelay: http://localhost:5163"
        log_info "  - Nginx Proxy: http://localhost:8080"
    else
        docker-compose up -d --build
        log_success "TokenRelay started"
        log_info "Access URL: http://localhost:5163"
    fi
    
    log_info "Health check: http://localhost:5163/health"
}

# Stop services
stop_services() {
    log_info "Stopping TokenRelay services..."
    
    cd "$SCRIPT_DIR"
    docker-compose down
    
    log_success "TokenRelay services stopped"
}

# Show logs
show_logs() {
    cd "$SCRIPT_DIR"
    docker-compose logs -f tokenrelay
}

# Show status
show_status() {
    cd "$SCRIPT_DIR"
    docker-compose ps
    
    log_info "Testing health endpoint..."
    if curl -s http://localhost:5163/health > /dev/null; then
        log_success "TokenRelay is healthy"
    else
        log_warning "TokenRelay health check failed"
    fi
}

# Clean up
cleanup() {
    log_info "Cleaning up TokenRelay deployment..."
    
    cd "$SCRIPT_DIR"
    docker-compose down -v --rmi local
    docker system prune -f
    
    log_success "Cleanup completed"
}

# Main script
show_usage() {
    echo "Usage: $0 [COMMAND]"
    echo ""
    echo "Commands:"
    echo "  start         Start TokenRelay (without Nginx)"
    echo "  start-nginx   Start TokenRelay with Nginx reverse proxy"
    echo "  stop          Stop TokenRelay services"
    echo "  restart       Restart TokenRelay services"
    echo "  logs          Show TokenRelay logs (follow mode)"
    echo "  status        Show service status and health"
    echo "  cleanup       Remove containers, volumes, and images"
    echo "  setup         Initial setup (create .env file)"
    echo ""
}

case "${1:-}" in
    "start")
        check_prerequisites
        if setup_environment; then
            start_services false
        fi
        ;;
    "start-nginx")
        check_prerequisites
        if setup_environment; then
            start_services true
        fi
        ;;
    "stop")
        stop_services
        ;;
    "restart")
        stop_services
        check_prerequisites
        if setup_environment; then
            start_services false
        fi
        ;;
    "logs")
        show_logs
        ;;
    "status")
        show_status
        ;;
    "cleanup")
        cleanup
        ;;
    "setup")
        setup_environment
        ;;
    *)
        show_usage
        exit 1
        ;;
esac
