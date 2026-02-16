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

# TEMPORARY: Copy static nginx.conf with hard-coded backend URL for testing
COPY nginx.conf.static /etc/nginx/nginx.conf

# Copy built app from build stage
COPY --from=build /app/dist/woven-frontend/browser /usr/share/nginx/html

# Create non-root user
RUN adduser -D -g '' nginxuser && \
    chown -R nginxuser:nginxuser /var/cache/nginx && \
    chown -R nginxuser:nginxuser /var/log/nginx && \
    touch /var/run/nginx.pid && \
    chown -R nginxuser:nginxuser /var/run/nginx.pid

EXPOSE 80

# Default BACKEND_URL for Docker Compose. Container Apps overrides this.
ENV BACKEND_URL=http://backend:8080

# TEMPORARY: Use default nginx CMD (no template substitution)
# This is a test to see if the proxy works with hard-coded backend URL
CMD ["nginx", "-g", "daemon off;"]
