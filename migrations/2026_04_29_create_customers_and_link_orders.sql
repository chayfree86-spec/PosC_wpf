-- Creates a customer master table and links orders to customers.
-- Existing order customer_name/customer_mobile values are migrated into customers.

CREATE TABLE IF NOT EXISTS customers (
    id INT AUTO_INCREMENT PRIMARY KEY,
    uuid VARCHAR(36) UNIQUE,
    name VARCHAR(150) DEFAULT NULL,
    mobile VARCHAR(20) DEFAULT NULL,
    normalized_mobile VARCHAR(20) DEFAULT NULL,
    email VARCHAR(150) DEFAULT NULL,
    address TEXT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    sync_version INT DEFAULT 1,
    UNIQUE KEY uq_customers_normalized_mobile (normalized_mobile)
);

DROP PROCEDURE IF EXISTS add_order_customer_id_if_missing;

DELIMITER $$

CREATE PROCEDURE add_order_customer_id_if_missing()
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'orders'
          AND COLUMN_NAME = 'customer_id'
    ) THEN
        ALTER TABLE orders ADD COLUMN customer_id INT NULL AFTER table_id;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'orders'
          AND INDEX_NAME = 'idx_orders_customer'
    ) THEN
        CREATE INDEX idx_orders_customer ON orders(customer_id);
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'orders'
          AND CONSTRAINT_NAME = 'fk_orders_customer'
    ) THEN
        ALTER TABLE orders
          ADD CONSTRAINT fk_orders_customer
          FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE SET NULL;
    END IF;
END$$

DELIMITER ;

CALL add_order_customer_id_if_missing();

INSERT INTO customers (uuid, name, mobile, normalized_mobile)
SELECT UUID(),
       MAX(source.customer_name),
       MAX(source.customer_mobile),
       source.normalized_mobile
FROM (
    SELECT NULLIF(TRIM(customer_name), '') AS customer_name,
           NULLIF(TRIM(customer_mobile), '') AS customer_mobile,
           NULLIF(REGEXP_REPLACE(customer_mobile, '[^0-9]', ''), '') AS normalized_mobile
    FROM orders
    WHERE customer_id IS NULL
) source
LEFT JOIN customers c
  ON c.normalized_mobile = source.normalized_mobile
WHERE (source.customer_name IS NOT NULL OR source.customer_mobile IS NOT NULL)
  AND source.normalized_mobile IS NOT NULL
  AND c.id IS NULL
GROUP BY source.normalized_mobile;

INSERT INTO customers (uuid, name, mobile, normalized_mobile)
SELECT UUID(),
       source.customer_name,
       NULL,
       NULL
FROM (
    SELECT DISTINCT NULLIF(TRIM(customer_name), '') AS customer_name
    FROM orders
    WHERE customer_id IS NULL
      AND NULLIF(TRIM(customer_name), '') IS NOT NULL
      AND NULLIF(TRIM(customer_mobile), '') IS NULL
) source;

UPDATE orders o
JOIN customers c
  ON c.normalized_mobile = NULLIF(REGEXP_REPLACE(o.customer_mobile, '[^0-9]', ''), '')
SET o.customer_id = c.id
WHERE o.customer_id IS NULL
  AND NULLIF(REGEXP_REPLACE(o.customer_mobile, '[^0-9]', ''), '') IS NOT NULL;

UPDATE orders o
JOIN customers c
  ON c.normalized_mobile IS NULL
 AND c.name = NULLIF(TRIM(o.customer_name), '')
SET o.customer_id = c.id
WHERE o.customer_id IS NULL
  AND NULLIF(TRIM(o.customer_name), '') IS NOT NULL
  AND NULLIF(TRIM(o.customer_mobile), '') IS NULL;

DROP PROCEDURE IF EXISTS add_order_customer_id_if_missing;
