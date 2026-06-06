#!/bin/bash
# IdentityCenter.Api Publish Script (Linux/Mac)
# Produces a PUBLISH FOLDER deploy (no Docker required).
#
# DEFAULT: framework-dependent (smaller; needs ASP.NET Core 8 Runtime on server):
#     ./publish.sh
#     deploy: copy ./publish to server, run:  dotnet IdentityCenter.API.dll
#
# SELF-CONTAINED (runtime included, no .NET install needed on server):
#     ./publish.sh --self-contained            # default RID win-x64
#     ./publish.sh --self-contained linux-x64  # override RID
#     deploy: copy ./publish to server, run:  ./IdentityCenter.API
set -e

API_PROJECT="IdentityCenter.API/IdentityCenter.API.csproj"
OUTPUT="./publish"
SELF_CONTAINED=false
RUNTIME="win-x64"

shift_done=false
for arg in "$@"; do
    if [ "$shift_done" = true ]; then RUNTIME="$arg"; shift_done=false; continue; fi
    case $arg in
        --self-contained) SELF_CONTAINED=true; shift_done=true ;;
    esac
done
# if --self-contained had no RID following, keep default win-x64
[ "$shift_done" = true ] && RUNTIME="win-x64"

echo "Publishing IdentityCenter.Api -> $OUTPUT"
rm -rf "$OUTPUT"

if [ "$SELF_CONTAINED" = true ]; then
    echo "Mode: SELF-CONTAINED ($RUNTIME) — runtime included."
    dotnet publish "$API_PROJECT" -c Release -o "$OUTPUT" --self-contained true -r "$RUNTIME"
else
    echo "Mode: FRAMEWORK-DEPENDENT — requires ASP.NET Core 8 Runtime on server."
    dotnet publish "$API_PROJECT" -c Release -o "$OUTPUT" --self-contained false
fi

echo ""
echo "Publish complete: $OUTPUT"
echo "Deploy: copy the folder to the server and run:"
if [ "$SELF_CONTAINED" = true ]; then echo "  ./IdentityCenter.API"; else echo "  dotnet IdentityCenter.API.dll"; fi
echo "Listens on http://localhost:5062 (Swagger at /swagger)"
echo "NOTE: set ConnectionStrings:DefaultConnection on the server (env/user-secrets)."
echo "      'enc:' connection strings need the DataProtection keyring present."
