# syntax=docker/dockerfile:1.7
# NEXUS C Console — Multi-stage static production build served by non-root Nginx

FROM node:24-alpine AS build
WORKDIR /app

COPY package.json package-lock.json* ./
RUN npm ci

COPY . .
ARG VITE_APP_NAME="NEXUS C Console"
ARG VITE_API_BASE_URL=""
ARG VITE_APP_ENV="production"
ENV VITE_APP_NAME=$VITE_APP_NAME \
    VITE_API_BASE_URL=$VITE_API_BASE_URL \
    VITE_APP_ENV=$VITE_APP_ENV

RUN npm run build

FROM nginxinc/nginx-unprivileged:1.27-alpine AS runtime
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=3 \
  CMD wget -qO- http://127.0.0.1:8080/healthz || exit 1
