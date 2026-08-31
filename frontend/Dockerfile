# syntax=docker/dockerfile:1.7
FROM node:22-bookworm-slim AS build

WORKDIR /app/SalesPlattform/SalesPlattform/frontend
COPY frontend/package*.json ./
RUN --mount=type=secret,id=github_packages_token \
    GITHUB_PACKAGES_TOKEN="$(cat /run/secrets/github_packages_token)" && \
    printf '@hammer2fall:registry=https://npm.pkg.github.com\n//npm.pkg.github.com/:_authToken=%s\n' "$GITHUB_PACKAGES_TOKEN" > .npmrc && \
    npm ci --no-audit --no-fund && \
    rm -f .npmrc
COPY frontend/ ./

ARG VITE_API_BASE_URL=http://localhost:3101/apps/sales-plattform
ARG VITE_PLATFORM_API_BASE_URL=http://localhost:3101/platform
ARG VITE_TENANT_PORTAL_URL=http://localhost:3001
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL
ENV VITE_PLATFORM_API_BASE_URL=$VITE_PLATFORM_API_BASE_URL
ENV VITE_TENANT_PORTAL_URL=$VITE_TENANT_PORTAL_URL
RUN npm run build

FROM nginx:1.27-alpine
COPY frontend/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/SalesPlattform/SalesPlattform/frontend/dist /usr/share/nginx/html
EXPOSE 80
