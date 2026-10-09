-- Archive for change-log rows every device has already pulled; pull never reads it.
CREATE TABLE sync.change_log_archive (
    server_seq    bigint PRIMARY KEY,
    facility_id   text NOT NULL,
    device_id     text,
    change_type   text NOT NULL,
    assessment_id uuid NOT NULL,
    revision      integer NOT NULL,
    created_at    timestamptz NOT NULL,
    archived_at   timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_change_log_archive_assessment ON sync.change_log_archive (assessment_id, revision);
