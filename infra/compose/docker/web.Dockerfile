# Static web client: build with Node, serve with unprivileged nginx (SPA fallback + security headers).
FROM node:24-alpine AS build
WORKDIR /src
COPY src/frontend/package.json src/frontend/package-lock.json ./
RUN npm ci --no-fund --ignore-scripts
COPY src/frontend ./
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.31-alpine
COPY infra/compose/docker/web.nginx.conf /etc/nginx/conf.d/default.conf
COPY infra/compose/docker/web.security-headers.conf /etc/nginx/snippets/security-headers.conf
COPY --from=build /src/dist /usr/share/nginx/html
EXPOSE 8080
