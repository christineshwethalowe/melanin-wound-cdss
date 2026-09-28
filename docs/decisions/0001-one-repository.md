# 0001: One repository for the whole project

- Status: accepted
- Date: 2026-09-28
- Deciders: Member 4 (to be confirmed by the team)

## Context

The Flutter app, the backend services and the Recommendation Service all depend on the same
contracts (`contracts/`). With separate repositories these copies can drift.

## Decision

Use one repository (`melanin-wound-cdss`) with `mobile/`, `backend/`, `research/`, `contracts/`,
`db/` and `infra/`. Ownership is enforced with `.github/CODEOWNERS`.

## Consequences

A contract change is one pull request reviewed by everyone it affects. CI runs only the jobs
relevant to the changed paths as the repo grows.
