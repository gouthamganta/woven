#!/bin/sh
set -e

echo "Starting nginx with BACKEND_URL=${BACKEND_URL}"

# Generate nginx.conf from template
envsubst '${BACKEND_URL}' < /etc/nginx/nginx.conf.template > /etc/nginx/nginx.conf

echo "Generated nginx.conf with backend URL"

# Verify the configuration
nginx -t

# Start nginx
exec nginx -g 'daemon off;'
