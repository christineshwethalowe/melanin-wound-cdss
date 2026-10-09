-- Ablation shadow tables with no unique constraints, to show duplication without touching the real tables.
CREATE SCHEMA IF NOT EXISTS ablation;

CREATE TABLE ablation.wound_assessment (
    row_id        bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    event_id      uuid NOT NULL,           -- deliberately not unique
    assessment_id uuid NOT NULL,
    revision      integer NOT NULL,
    device_id     text NOT NULL,
    kafka_ref     text NOT NULL,
    received_at   timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_ablation_assessment_device ON ablation.wound_assessment (device_id);

CREATE TABLE ablation.recommendation (
    row_id        bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    event_id      uuid NOT NULL,           -- deliberately not unique
    assessment_id uuid NOT NULL,
    revision      integer NOT NULL,
    created_at    timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_ablation_recommendation_event ON ablation.recommendation (event_id);
