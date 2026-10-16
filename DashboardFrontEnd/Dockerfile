# Stage 1: compile the React app to static files
FROM node:24-alpine AS build
WORKDIR /app

# package*.json first, on its own layer: `npm ci` only re-runs when dependencies change.
COPY DashboardFrontEnd/package*.json ./
RUN npm ci

COPY DashboardFrontEnd/ ./
RUN npm run build

# Stage 2: serve them with nginx, which also proxies /api/ to the server container
FROM nginx:1.27-alpine
COPY DashboardFrontEnd/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
