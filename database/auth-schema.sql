-- ============================
-- Table: users
-- ============================

CREATE TABLE users (
    user_id        UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    username       VARCHAR(100) UNIQUE NOT NULL,
    password_hash  TEXT NOT NULL,
    role           VARCHAR(30) NOT NULL,
    is_active      BOOLEAN NOT NULL DEFAULT TRUE,
    created_at     TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE users
ADD CONSTRAINT chk_user_role
CHECK (role IN ('ADMIN', 'USER'));
