# Admin dashboard (ADR 0005): `flutter build web` served as static files by nginx.
# Built from this folder: docker build -t melanin-wound-cdss/admin-dashboard frontend/admin_dashboard

ARG FLUTTER_IMAGE=ghcr.io/cirruslabs/flutter:stable

FROM ${FLUTTER_IMAGE} AS build
# The browser calls the API gateway directly, so this is the gateway as the browser sees it, not a Docker hostname.
ARG API_BASE_URL=http://localhost:8080
WORKDIR /src
COPY pubspec.yaml pubspec.lock ./
RUN flutter pub get
COPY . .
RUN flutter build web --release --dart-define=API_BASE_URL=${API_BASE_URL}

FROM nginx:1.27-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/build/web /usr/share/nginx/html
EXPOSE 80
