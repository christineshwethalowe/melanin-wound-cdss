# Admin dashboard

Flutter Web dashboard for facility admins ([ADR 0005](../../docs/decisions/0005-admin-dashboard-flutter-web.md)).
Only accounts with role `admin` can sign in. Everything shown is scoped to the admin's facility by the server.

- **Clinicians**: list, register (nurse, wound specialist or admin), unlock, deactivate, reset MFA
  (architecture §7). There is no self-signup: an admin creates every account.
- **Devices**: list, revoke a lost or stolen phone
- **Audit log**: sign-ins, lockouts and admin actions, with a filter and a failures-only view

It talks only to the API gateway (`/v1/auth/*`, `/v1/admin/*`). The access token is kept in memory; the refresh
token in `sessionStorage`, so it lasts for the browser tab only.

## Run

With Docker (part of `docker compose up -d --build` at the repository root): http://localhost:3001.
Demo admin: `admin.demo` / `Demo-Admin-2026!`.

For development, with the backend running:

```bash
flutter run -d chrome --web-port 3001
```

Use port 3001: it is the origin the gateway's CORS policy allows (`Cors:AllowedOrigins` in the api-gateway).
For a gateway somewhere else, add `--dart-define=API_BASE_URL=http://host:port` (Docker:
`DASHBOARD_API_BASE_URL`). The URL is the gateway as the browser sees it.

## Layout

```
lib/
├── core/        # config, AdminApi (HTTP), AuthSession (login/refresh/logout), token store, messages, shared UI
├── features/    # auth (login + MFA code), clinicians, devices, audit
├── app/         # signed-in shell: navigation, who is signed in, sign-out
└── main.dart
```

## Test

```bash
flutter analyze
flutter test
```
