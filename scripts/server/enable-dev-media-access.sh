#!/usr/bin/env bash
# Run as the server owner (sudo bash enable-dev-media-access.sh).
# Grants the existing developer SSH account access only to product media.
set -euo pipefail
developer="${1:-treba-sasha}"
media_root=/opt/treba-backend/uploads/products
[[ "$developer" =~ ^[a-z][a-z0-9_-]*$ ]] || { echo 'Invalid Linux username'; exit 1; }
test -d "$media_root"
id "$developer" >/dev/null
command -v setfacl >/dev/null || { echo 'Install the acl package before running this script.'; exit 1; }
setfacl -R -m "u:${developer}:rwX" "$media_root"
find "$media_root" -type d -exec setfacl -m "d:u:${developer}:rwx" {} +
echo "Product media access configured for ${developer}"
