\set ON_ERROR_STOP on
CREATE DATABASE auth_db;
\connect auth_db
\ir auth-schema.sql
CREATE DATABASE inventory_db;
\connect inventory_db
\ir inventory-schema.sql
CREATE DATABASE order_db;
\connect order_db
\ir order-schema.sql
