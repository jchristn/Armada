# Base images are pinned to exact patch versions; bump them deliberately (see .github/workflows/security.yml).
# --- Build stage ---
FROM node:22.23.3-alpine3.23 AS build
WORKDIR /app
COPY src/Armada.Dashboard/package.json src/Armada.Dashboard/package-lock.json* ./
RUN npm ci
COPY src/Armada.Dashboard/ .
RUN npm run build

# --- Runtime stage ---
# nginx-unprivileged runs as the non-root nginx user (UID 101) and listens on 8080.
FROM nginxinc/nginx-unprivileged:1.30.5-alpine3.24
# The build uses the base path /dashboard/, so it lives under /dashboard/ in the web root.
COPY --from=build /app/dist /usr/share/nginx/html/dashboard
# Rendered to /etc/nginx/conf.d/default.conf by the image entrypoint, substituting ARMADA_SERVER_URL.
COPY src/Armada.Dashboard/nginx.conf /etc/nginx/templates/default.conf.template
ENV ARMADA_SERVER_URL=http://armada-server:7890
EXPOSE 8080
CMD ["nginx", "-g", "daemon off;"]
