-- 0001: initial schema for the Transactions Dispute Portal.

CREATE TABLE users (
    id            uuid        PRIMARY KEY,
    email         text        NOT NULL,
    display_name  text        NOT NULL,
    password_hash text        NOT NULL,
    role          text        NOT NULL CHECK (role IN ('Customer', 'Agent')),
    created_at    timestamptz NOT NULL
);

CREATE UNIQUE INDEX ux_users_email ON users (email);

CREATE TABLE transactions (
    id          uuid          PRIMARY KEY,
    customer_id uuid          NOT NULL REFERENCES users (id),
    reference   text          NOT NULL,
    merchant    text          NOT NULL,
    category    text          NOT NULL,
    amount      numeric(18,2) NOT NULL,
    currency    char(3)       NOT NULL,
    occurred_at timestamptz   NOT NULL
);

CREATE UNIQUE INDEX ux_transactions_reference ON transactions (reference);
CREATE INDEX ix_transactions_customer_occurred ON transactions (customer_id, occurred_at DESC);

CREATE TABLE disputes (
    id              uuid        PRIMARY KEY,
    transaction_id  uuid        NOT NULL REFERENCES transactions (id),
    customer_id     uuid        NOT NULL REFERENCES users (id),
    reason          text        NOT NULL,
    description     text        NOT NULL,
    status          text        NOT NULL
        CHECK (status IN ('Submitted', 'UnderReview', 'Upheld', 'Rejected', 'Withdrawn')),
    resolution_note text        NULL,
    created_at      timestamptz NOT NULL,
    updated_at      timestamptz NOT NULL
);

-- At most one open dispute per transaction, enforced by the database even under concurrent requests.
CREATE UNIQUE INDEX ux_disputes_one_active_per_transaction
    ON disputes (transaction_id)
    WHERE status IN ('Submitted', 'UnderReview');

CREATE INDEX ix_disputes_customer_created ON disputes (customer_id, created_at DESC);
CREATE INDEX ix_disputes_status_created ON disputes (status, created_at DESC);
CREATE INDEX ix_disputes_transaction_created ON disputes (transaction_id, created_at DESC);

-- Dispute history: one row per status change.
CREATE TABLE dispute_events (
    id          uuid        PRIMARY KEY,
    dispute_id  uuid        NOT NULL REFERENCES disputes (id),
    from_status text        NULL,
    to_status   text        NOT NULL,
    actor_id    uuid        NOT NULL REFERENCES users (id),
    actor_role  text        NOT NULL,
    note        text        NULL,
    occurred_at timestamptz NOT NULL
);

CREATE INDEX ix_dispute_events_dispute ON dispute_events (dispute_id, occurred_at);

-- History is an audit trail: rows can be added, never changed or removed.
CREATE FUNCTION reject_dispute_event_mutation() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'dispute_events is append-only (% rejected)', TG_OP
        USING ERRCODE = 'insufficient_privilege';
END;
$$;

CREATE TRIGGER trg_dispute_events_append_only
    BEFORE UPDATE OR DELETE ON dispute_events
    FOR EACH ROW EXECUTE FUNCTION reject_dispute_event_mutation();
