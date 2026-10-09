-- REST baseline tables for the evaluation; deliberately no dedup, kept in their own schema.
CREATE SCHEMA IF NOT EXISTS baseline;

CREATE TABLE baseline.assessment (
    row_id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    event_id            uuid NOT NULL,                   -- not unique, on purpose
    assessment_id       uuid NOT NULL,
    revision            integer NOT NULL,
    wound_id            uuid NOT NULL,
    device_id           text NOT NULL,
    facility_id         text NOT NULL,
    captured_at         timestamptz NOT NULL,
    received_at         timestamptz NOT NULL DEFAULT now(),
    analytics           jsonb NOT NULL,
    clinical_assessment jsonb NOT NULL
);

CREATE INDEX ix_baseline_assessment_wound ON baseline.assessment (wound_id, captured_at);
CREATE INDEX ix_baseline_assessment_event ON baseline.assessment (event_id);

CREATE TABLE baseline.recommendation (
    row_id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    assessment_row_id bigint NOT NULL REFERENCES baseline.assessment (row_id),
    mode              text NOT NULL,
    corpus_version    text NOT NULL,
    payload           jsonb NOT NULL,
    created_at        timestamptz NOT NULL DEFAULT now()
);
