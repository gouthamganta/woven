# ================================
# Woven Frontend - Multi-stage Dockerfile
# ================================

# Stage 1: Build
FROM node:22-alpine AS build
WORKDIR /app

# Copy package files
COPY package*.json ./

# Install dependencies
RUN npm ci

# Copy source code
COPY . .

# Build for production
RUN npm run build -- --configuration=production

# Stage 2: Serve with nginx
FROM nginx:alpine AS runtime

# Copy nginx template (envsubst replaces BACKEND_URL/BACKEND_HOST at startup)
COPY nginx.conf.template /etc/nginx/nginx.conf.template

# Copy entrypoint script that runs envsubst then starts nginx
COPY docker-entrypoint.sh /docker-entrypoint.sh
RUN chmod +x /docker-entrypoint.sh

# Copy built app from build stage
COPY --from=build /app/dist/woven-frontend/browser /usr/share/nginx/html

# Create non-root user
RUN adduser -D -g '' nginxuser && \
    chown -R nginxuser:nginxuser /var/cache/nginx && \
    chown -R nginxuser:nginxuser /var/log/nginx && \
    touch /var/run/nginx.pid && \
    chown -R nginxuser:nginxuser /var/run/nginx.pid && \
    chown -R nginxuser:nginxuser /etc/nginx

EXPOSE 80

# Default BACKEND_URL for Docker Compose. Container Apps overrides this via Terraform.
ENV BACKEND_URL=http://backend:8080

CMD ["/docker-entrypoint.sh"]
