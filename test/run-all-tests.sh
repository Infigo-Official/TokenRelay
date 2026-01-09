#!/bin/bash

#######################################################################
# TokenRelay Test Runner
#
# Runs all unit and integration tests with automatic Docker management.
# Designed for CI/CD pipelines (GitHub Actions) and local development.
#
# Usage:
#   ./run-all-tests.sh           # Run all tests
#   ./run-all-tests.sh -v        # Run with verbose output
#   ./run-all-tests.sh --help    # Show help
#
# Exit Codes:
#   0 - All tests passed
#   1 - One or more tests failed
#   2 - Build failed
#   3 - Prerequisites missing
#
# Prerequisites:
#   - .NET 10.0 SDK
#   - Docker and docker-compose
#   - Ports 5193, 5194, 8191, 8192 available
#######################################################################

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
MAGENTA='\033[0;35m'
GRAY='\033[0;90m'
WHITE='\033[1;37m'
NC='\033[0m' # No Color

# Script configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
TEST_PROJECT="$REPO_ROOT/TokenRelay.Tests/TokenRelay.Tests.csproj"
OAUTH1_COMPOSE_FILE="$SCRIPT_DIR/docker/docker-compose.oauth1-integration.yml"
OAUTH2_COMPOSE_FILE="$SCRIPT_DIR/docker/docker-compose.oauth2-integration.yml"

# Test results storage
declare -a TEST_RESULTS
TOTAL_PASSED=0
TOTAL_FAILED=0
TOTAL_SKIPPED=0
OVERALL_START_TIME=0

# Verbose mode
VERBOSE=false

#######################################################################
# Helper Functions
#######################################################################

show_help() {
    cat << EOF
TokenRelay Test Runner

Usage: $(basename "$0") [OPTIONS]

Options:
    -v, --verbose    Enable verbose output
    -h, --help       Show this help message

Test Categories:
    - Unit Tests: All tests excluding Integration, OAuth1, OAuth2Integration
    - OAuth1 Integration: Tests with Category=OAuth1 (requires Docker)
    - OAuth2 Integration: Tests with Category=OAuth2Integration (requires Docker)

Examples:
    $(basename "$0")              # Run all tests
    $(basename "$0") -v           # Run with verbose output

Exit Codes:
    0 - All tests passed
    1 - One or more tests failed
    2 - Build failed
    3 - Prerequisites missing
EOF
}

write_header() {
    local title="$1"
    local symbol="${2:-[TEST]}"

    echo ""
    echo -e "${CYAN}${symbol} ${title}${NC}"
    printf "${CYAN}%s${NC}\n" "$(printf '=%.0s' $(seq 1 $((${#title} + ${#symbol} + 1))))"
}

write_info() {
    echo -e "  ${GRAY}[i] $1${NC}"
}

write_success() {
    echo -e "  ${GREEN}[+] $1${NC}"
}

write_warning() {
    echo -e "  ${YELLOW}[!] $1${NC}"
}

write_error() {
    echo -e "  ${RED}[-] $1${NC}"
}

write_verbose() {
    if [ "$VERBOSE" = true ]; then
        echo -e "  ${GRAY}[v] $1${NC}"
    fi
}

write_phase_result() {
    local phase="$1"
    local passed="$2"
    local failed="$3"
    local skipped="$4"
    local duration="$5"
    local success="$6"

    if [ "$success" = true ]; then
        local status_icon="[PASS]"
        local status_color="$GREEN"
    else
        local status_icon="[FAIL]"
        local status_color="$RED"
    fi

    local failed_color="$WHITE"
    if [ "$failed" -gt 0 ]; then
        failed_color="$RED"
    fi

    echo -e "${status_color}${status_icon} ${phase}:${NC} ${GREEN}Passed: ${passed}${NC} | ${failed_color}Failed: ${failed}${NC} | ${YELLOW}Skipped: ${skipped}${NC} | ${GRAY}Duration: ${duration}s${NC}"

    # Store result for summary
    TEST_RESULTS+=("$phase|$passed|$failed|$skipped|$duration|$success")

    TOTAL_PASSED=$((TOTAL_PASSED + passed))
    TOTAL_FAILED=$((TOTAL_FAILED + failed))
    TOTAL_SKIPPED=$((TOTAL_SKIPPED + skipped))
}

#######################################################################
# Docker Management Functions
#######################################################################

start_docker_containers() {
    local compose_file="$1"
    local service_name="$2"
    local health_url="$3"
    local timeout_seconds="${4:-120}"

    write_info "Starting Docker containers for ${service_name}..."
    write_verbose "Compose file: ${compose_file}"

    # Start containers
    if ! docker-compose -f "$compose_file" up -d --build 2>&1; then
        write_error "Failed to start Docker containers"
        return 1
    fi

    write_success "Containers started"

    # Wait for health check
    write_info "Waiting for services to be healthy..."
    local start_time=$(date +%s)
    local healthy=false

    while [ "$healthy" = false ]; do
        local current_time=$(date +%s)
        local elapsed=$((current_time - start_time))

        if [ $elapsed -ge $timeout_seconds ]; then
            write_error "Services did not become healthy within ${timeout_seconds} seconds"
            return 1
        fi

        if curl -s -f --max-time 5 "$health_url" > /dev/null 2>&1; then
            healthy=true
            write_success "Services are healthy"
        else
            write_verbose "Waiting for ${health_url}..."
            sleep 3
        fi
    done

    return 0
}

stop_docker_containers() {
    local compose_file="$1"
    local service_name="$2"

    write_info "Stopping Docker containers for ${service_name}..."

    if docker-compose -f "$compose_file" down -v --remove-orphans 2>&1; then
        write_success "Containers stopped and cleaned up"
    else
        write_warning "Warning: Failed to stop some containers"
    fi
}

#######################################################################
# Test Execution Functions
#######################################################################

run_dotnet_test() {
    local filter="$1"
    local phase_name="$2"

    local start_time=$(date +%s)

    write_verbose "Running: dotnet test --filter \"${filter}\" --no-build"

    # Run tests and capture output
    local output
    local exit_code=0
    output=$(dotnet test "$TEST_PROJECT" --filter "$filter" --no-build --verbosity normal 2>&1) || exit_code=$?

    local end_time=$(date +%s)
    local duration=$((end_time - start_time))

    # Parse test results from output
    local passed=0
    local failed=0
    local skipped=0

    # Try to extract counts from output (compatible with BSD and GNU grep)
    if echo "$output" | grep -q "Passed:"; then
        passed=$(echo "$output" | grep -E "Passed:\s*[0-9]+" | sed 's/.*Passed:[[:space:]]*//' | grep -oE "^[0-9]+" | tail -1 || echo "0")
        failed=$(echo "$output" | grep -E "Failed:\s*[0-9]+" | sed 's/.*Failed:[[:space:]]*//' | grep -oE "^[0-9]+" | tail -1 || echo "0")
        skipped=$(echo "$output" | grep -E "Skipped:\s*[0-9]+" | sed 's/.*Skipped:[[:space:]]*//' | grep -oE "^[0-9]+" | tail -1 || echo "0")
    fi

    # Fallback: try alternative format
    if [ "$passed" = "0" ] && [ "$failed" = "0" ]; then
        local summary_line=$(echo "$output" | grep -E "Total tests:|Passed!|Failed!" | tail -1)
        if [ -n "$summary_line" ]; then
            passed=$(echo "$summary_line" | grep -E "Passed:\s*[0-9]+" | sed 's/.*Passed:[[:space:]]*//' | grep -oE "^[0-9]+" || echo "0")
            failed=$(echo "$summary_line" | grep -E "Failed:\s*[0-9]+" | sed 's/.*Failed:[[:space:]]*//' | grep -oE "^[0-9]+" || echo "0")
            skipped=$(echo "$summary_line" | grep -E "Skipped:\s*[0-9]+" | sed 's/.*Skipped:[[:space:]]*//' | grep -oE "^[0-9]+" || echo "0")
        fi
    fi

    # Ensure numeric values
    passed=${passed:-0}
    failed=${failed:-0}
    skipped=${skipped:-0}

    # Show verbose output or failed test details
    if [ "$VERBOSE" = true ] || [ "$failed" -gt 0 ]; then
        echo "$output" | while read -r line; do
            if echo "$line" | grep -qE "Failed|Error|Exception" || [ "$VERBOSE" = true ]; then
                if echo "$line" | grep -qE "Failed|Error"; then
                    echo -e "    ${RED}${line}${NC}"
                else
                    echo -e "    ${GRAY}${line}${NC}"
                fi
            fi
        done
    fi

    local success=true
    if [ $exit_code -ne 0 ] || [ "$failed" -gt 0 ]; then
        success=false
    fi

    write_phase_result "$phase_name" "$passed" "$failed" "$skipped" "$duration" "$success"

    if [ "$success" = true ]; then
        return 0
    else
        return 1
    fi
}

#######################################################################
# Main Test Functions
#######################################################################

test_prerequisites() {
    write_header "Prerequisites Check" "[CHECK]"

    local all_passed=true

    # Check dotnet
    if command -v dotnet &> /dev/null; then
        local dotnet_version=$(dotnet --version 2>&1)
        write_success "dotnet SDK found: ${dotnet_version}"
    else
        write_error "dotnet SDK not found"
        all_passed=false
    fi

    # Check docker
    if command -v docker &> /dev/null; then
        local docker_version=$(docker --version 2>&1)
        write_success "Docker found: ${docker_version}"
    else
        write_error "Docker not found"
        all_passed=false
    fi

    # Check docker-compose
    if command -v docker-compose &> /dev/null; then
        local compose_version=$(docker-compose --version 2>&1)
        write_success "docker-compose found: ${compose_version}"
    else
        write_error "docker-compose not found"
        all_passed=false
    fi

    # Check test project exists
    if [ -f "$TEST_PROJECT" ]; then
        write_success "Test project found: ${TEST_PROJECT}"
    else
        write_error "Test project not found: ${TEST_PROJECT}"
        all_passed=false
    fi

    # Check docker compose files
    if [ -f "$OAUTH1_COMPOSE_FILE" ]; then
        write_success "OAuth1 compose file found"
    else
        write_error "OAuth1 compose file not found: ${OAUTH1_COMPOSE_FILE}"
        all_passed=false
    fi

    if [ -f "$OAUTH2_COMPOSE_FILE" ]; then
        write_success "OAuth2 compose file found"
    else
        write_error "OAuth2 compose file not found: ${OAUTH2_COMPOSE_FILE}"
        all_passed=false
    fi

    if [ "$all_passed" = true ]; then
        return 0
    else
        return 1
    fi
}

run_build() {
    write_header "Building Test Project" "[BUILD]"

    write_info "Building TokenRelay.Tests..."

    local output
    local exit_code=0
    output=$(dotnet build "$TEST_PROJECT" --configuration Release 2>&1) || exit_code=$?

    if [ $exit_code -eq 0 ]; then
        write_success "Build successful"
        return 0
    else
        write_error "Build failed"
        echo "$output" | grep -i "error" | while read -r line; do
            echo -e "    ${RED}${line}${NC}"
        done
        return 1
    fi
}

run_unit_tests() {
    write_header "Unit Tests" "[UNIT]"

    local filter="Category!=Integration&Category!=OAuth1&Category!=OAuth2Integration"
    run_dotnet_test "$filter" "Unit Tests"
    return $?
}

run_oauth1_integration_tests() {
    write_header "OAuth1 Integration Tests" "[OAuth1]"

    local result=1

    # Start containers
    if start_docker_containers "$OAUTH1_COMPOSE_FILE" "OAuth1" "http://localhost:5193/health" 120; then
        # Run tests
        run_dotnet_test "Category=OAuth1" "OAuth1 Integration" && result=0 || result=1
    else
        write_phase_result "OAuth1 Integration" 0 1 0 0 false
    fi

    # Always stop containers
    stop_docker_containers "$OAUTH1_COMPOSE_FILE" "OAuth1"

    return $result
}

run_oauth2_integration_tests() {
    write_header "OAuth2 Integration Tests" "[OAuth2]"

    local result=1

    # Start containers
    if start_docker_containers "$OAUTH2_COMPOSE_FILE" "OAuth2" "http://localhost:5194/health" 120; then
        # Run tests
        run_dotnet_test "Category=OAuth2Integration" "OAuth2 Integration" && result=0 || result=1
    else
        write_phase_result "OAuth2 Integration" 0 1 0 0 false
    fi

    # Always stop containers
    stop_docker_containers "$OAUTH2_COMPOSE_FILE" "OAuth2"

    return $result
}

write_summary() {
    local end_time=$(date +%s)
    local total_duration=$((end_time - OVERALL_START_TIME))

    echo ""
    echo -e "${CYAN}+======================================================================+${NC}"
    echo -e "${CYAN}|                       TEST EXECUTION SUMMARY                         |${NC}"
    echo -e "${CYAN}+======================================================================+${NC}"
    echo -e "${CYAN}| Phase                | Passed | Failed | Skipped | Duration          |${NC}"
    echo -e "${CYAN}+----------------------+--------+--------+---------+-------------------+${NC}"

    for result in "${TEST_RESULTS[@]}"; do
        IFS='|' read -r phase passed failed skipped duration success <<< "$result"

        local failed_color="$WHITE"
        if [ "$failed" -gt 0 ]; then
            failed_color="$RED"
        fi

        printf "${CYAN}|${NC} ${WHITE}%-20s${NC} ${CYAN}|${NC} ${GREEN}%6s${NC} ${CYAN}|${NC} ${failed_color}%6s${NC} ${CYAN}|${NC} ${YELLOW}%7s${NC} ${CYAN}|${NC} ${GRAY}%17ss${NC} ${CYAN}|${NC}\n" \
            "$phase" "$passed" "$failed" "$skipped" "$duration"
    done

    echo -e "${CYAN}+----------------------+--------+--------+---------+-------------------+${NC}"

    local total_failed_color="$WHITE"
    if [ "$TOTAL_FAILED" -gt 0 ]; then
        total_failed_color="$RED"
    fi

    printf "${CYAN}|${NC} ${WHITE}%-20s${NC} ${CYAN}|${NC} ${GREEN}%6s${NC} ${CYAN}|${NC} ${total_failed_color}%6s${NC} ${CYAN}|${NC} ${YELLOW}%7s${NC} ${CYAN}|${NC} ${GRAY}%17ss${NC} ${CYAN}|${NC}\n" \
        "TOTAL" "$TOTAL_PASSED" "$TOTAL_FAILED" "$TOTAL_SKIPPED" "$total_duration"

    echo -e "${CYAN}+======================================================================+${NC}"

    echo ""

    if [ "$TOTAL_FAILED" -eq 0 ]; then
        echo -e "${GREEN}[PASS] All tests passed!${NC}"
    else
        echo -e "${RED}[FAIL] ${TOTAL_FAILED} test(s) failed!${NC}"
    fi

    echo ""
}

#######################################################################
# Main Execution
#######################################################################

main() {
    # Parse arguments
    while [[ $# -gt 0 ]]; do
        case $1 in
            -v|--verbose)
                VERBOSE=true
                shift
                ;;
            -h|--help)
                show_help
                exit 0
                ;;
            *)
                echo "Unknown option: $1"
                show_help
                exit 1
                ;;
        esac
    done

    echo ""
    echo -e "${MAGENTA}========================================${NC}"
    echo -e "${MAGENTA}    TokenRelay Test Runner${NC}"
    echo -e "${MAGENTA}========================================${NC}"
    echo ""

    OVERALL_START_TIME=$(date +%s)

    # Phase 1: Prerequisites
    if ! test_prerequisites; then
        echo ""
        echo -e "${RED}[FAIL] Prerequisites check failed. Please install missing dependencies.${NC}"
        exit 3
    fi

    # Phase 2: Build
    if ! run_build; then
        echo ""
        echo -e "${RED}[FAIL] Build failed. Please fix build errors before running tests.${NC}"
        exit 2
    fi

    # Phase 3: Unit Tests
    run_unit_tests || true

    # Phase 4: OAuth1 Integration Tests
    run_oauth1_integration_tests || true

    # Phase 5: OAuth2 Integration Tests
    run_oauth2_integration_tests || true

    # Summary
    write_summary

    # Exit code
    if [ "$TOTAL_FAILED" -gt 0 ]; then
        exit 1
    else
        exit 0
    fi
}

# Run main
main "$@"
