-- A durable workflow/outbox; business tables and status values remain unchanged.
CREATE TABLE order_operations (
    operation_id UUID PRIMARY KEY,
    order_id UUID NOT NULL,
    user_id UUID NOT NULL,
    operation_type VARCHAR(10) NOT NULL CHECK (operation_type IN ('CREATE', 'CANCEL')),
    request_key UUID,
    items JSONB NOT NULL,
    correlation_id VARCHAR(32) NOT NULL,
    state VARCHAR(10) NOT NULL DEFAULT 'PENDING' CHECK (state IN ('PENDING', 'COMPLETED', 'REJECTED')),
    error_status INT,
    error_detail TEXT,
    attempts INT NOT NULL DEFAULT 0,
    next_attempt_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE (order_id, operation_type)
);
CREATE UNIQUE INDEX idx_order_request_key ON order_operations(user_id, request_key) WHERE request_key IS NOT NULL;
CREATE UNIQUE INDEX idx_order_pending ON order_operations(order_id) WHERE state = 'PENDING';
CREATE INDEX idx_operation_retry ON order_operations(next_attempt_at) WHERE state = 'PENDING';
