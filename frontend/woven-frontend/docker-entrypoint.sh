#!/bin/sh
set -e

echo "Starting nginx with BACKEND_URL=${BACKEND_URL}"

# Extract hostname from BACKEND_URL (strip protocol and trailing path/slash)
# e.g. https://woven-prod-backend.internal.xxx.azurecontainerapps.io -> woven-prod-backend.internal.xxx.azurecontainerapps.io
# e.g. http://backend:8080 -> backend:8080
export BACKEND_HOST=$(echo "$BACKEND_URL" | sed -e 's|^https\?://||' -e 's|/.*$||')

echo "Resolved BACKEND_HOST=${BACKEND_HOST}"

# Generate nginx.conf from template (only substitute our variables, not nginx $ vars)
envsubst '${BACKEND_URL} ${BACKEND_HOST}' < /etc/nginx/nginx.conf.template > /etc/nginx/nginx.conf

echo "Generated nginx.conf with backend URL"

# Verify the configuration
nginx -t

# Start nginx
exec nginx -g 'daemon off;'
