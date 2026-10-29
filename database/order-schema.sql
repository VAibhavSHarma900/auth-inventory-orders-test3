CREATE TABLE orders (
    order_id       UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id        UUID NOT NULL,
    order_status   VARCHAR(30) NOT NULL,
    created_at     TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

ALTER TABLE orders
ADD CONSTRAINT chk_order_status
CHECK (order_status IN ('CREATED', 'CONFIRMED', 'CANCELLED'));

-- ============================
-- Table: order_items
-- ============================

CREATE TABLE order_items (
    order_item_id  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    order_id       UUID NOT NULL,
    product_id     UUID NOT NULL,
    quantity       INT NOT NULL CHECK (quantity > 0)
);

-- ============================
-- Foreign Key (Same Service Only)
-- ============================

ALTER TABLE order_items
ADD CONSTRAINT fk_order_items_order
FOREIGN KEY (order_id)
REFERENCES orders(order_id)
ON DELETE CASCADE;
