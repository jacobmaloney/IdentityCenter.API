#!/bin/bash
# IdentityCenter.Api Run Script (Linux/Mac)
# Runs the REST API locally on http://localhost:5062 (Swagger at /swagger).
set -e

RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; CYAN='\033[0;36m'; NC='\033[0m'
COMMAND=${1:-run}
RELEASE=false
WATCH=false
API_PROJECT="IdentityCenter.API/IdentityCenter.API.csproj"

for arg in "$@"; do
    case $arg in
        --release) RELEASE=true ;;
        --watch)   WATCH=true ;;
    esac
done

write_header() {
    echo -e "\n${CYAN}$1${NC}"
    echo -e "${CYAN}$(printf '%*s' ${#1} | tr ' ' '-')${NC}"
}

build_solution() {
    write_header "Building IdentityCenter.Api"
    if [ "$RELEASE" = true ]; then dotnet build IdentityCenter.Api.sln -c Release; else dotnet build IdentityCenter.Api.sln -c Debug; fi
    if [ $? -ne 0 ]; then echo -e "${RED}Build failed!${NC}"; exit 1; fi
    echo -e "${GREEN}Build completed successfully!${NC}"
}

run_application() {
    write_header "Starting IdentityCenter.Api on http://localhost:5062"
    if [ "$WATCH" = true ]; then
        echo -e "${YELLOW}Starting in watch mode (hot reload enabled)...${NC}"
        dotnet watch --project "$API_PROJECT" run
    else
        dotnet run --project "$API_PROJECT"
    fi
}

clean_solution() {
    write_header "Cleaning Solution"
    find . -type d \( -name bin -o -name obj \) | xargs rm -rf
    echo -e "${GREEN}Clean completed!${NC}"
}

show_usage() {
    echo "Usage: ./run.sh [command] [options]"
    echo ""
    echo "Commands:"
    echo "  build   Build the solution"
    echo "  run     Build and run the API (default)"
    echo "  clean   Clean build artifacts"
    echo ""
    echo "Options:"
    echo "  --release   Build in Release mode"
    echo "  --watch     Run with hot reload (watch mode)"
}

case $COMMAND in
    build) build_solution ;;
    run)   build_solution; run_application ;;
    clean) clean_solution ;;
    help|--help|-h) show_usage ;;
    *) echo -e "${RED}Unknown command: $COMMAND${NC}"; show_usage; exit 1 ;;
esac

echo -e "\n${GREEN}Done!${NC}"
