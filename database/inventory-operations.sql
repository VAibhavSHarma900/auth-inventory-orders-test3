-- Supporting recovery storage; the supplied products table is unchanged.
CREATE TABLE stock_operations (
    operation_id UUID PRIMARY KEY,
    order_id UUID NOT NULL,
    user_id UUID NOT NULL,
    operation_type VARCHAR(10) NOT NULL CHECK (operation_type IN ('DEDUCT', 'RESTORE')),
    request_hash TEXT NOT NULL,
    outcome VARCHAR(10) NOT NULL CHECK (outcome IN ('APPLIED', 'REJECTED')),
    error_status INT,
    error_detail TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT uq_stock_order_operation UNIQUE (order_id, operation_type)
);
