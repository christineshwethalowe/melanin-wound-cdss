# 0005: Admin dashboard in `frontend/`, built with Flutter Web

- Status: accepted
- Date: 2026-09-30

## Context

The admin API from Phase 1b (`/v1/admin/*`) has no user interface; facility admins can only use it through
scripts. Senior developer review asked for an admin dashboard in its own frontend folder. The team's
frontend stack is Flutter and Dart (the mobile app).

## Decision

- New top-level folder `frontend/`, with the dashboard in `frontend/admin_dashboard` (Flutter Web).
  Structure mirrors `mobile/`: `lib/core` (API client, token handling, config) and
  `lib/features/{auth,clinicians,audit}`.
- First pages: login (with the MFA code prompt), clinicians (list, register, unlock, deactivate, reset MFA)
  and the facility's auth audit log. Everything is scoped to the admin's facility, as the API already is.
- The dashboard talks only to the API gateway (0004), never to a service directly.
- The browser is not a registered device. The dashboard logs in as client `admin-dashboard` and only
  users with role `admin` may do so. Its tokens carry no `device_id`, so they cannot push or pull.
  This needs a small additive migration on `clinical.clinician_session` (`client_id`, nullable
  `device_id` for web sessions).
- The access token is kept in memory; the refresh token is kept for the browser session only.
- Served as a static `flutter build web` output from an nginx container.
- Shared Dart code between `mobile/` and `frontend/` (for example an auth client) may move to a
  `packages/` folder later; not now.

## Consequences

- CI gains a Flutter job (`flutter analyze`, `flutter test`).
- Later additions fit here: a DLQ and provenance view (the §14.2 stretch "DLQ replay tool") and
  auth-health numbers from §13.
