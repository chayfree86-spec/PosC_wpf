CREATE TABLE IF NOT EXISTS clients (
    id INT AUTO_INCREMENT PRIMARY KEY,
    uuid VARCHAR(36) UNIQUE,
    name VARCHAR(150) NOT NULL,
    slug VARCHAR(80) NOT NULL UNIQUE,
    is_active TINYINT(1) DEFAULT 1,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
);

INSERT INTO clients (uuid, name, slug)
VALUES
    (UUID(), 'Dal Roti', 'daalroti'),
    (UUID(), 'Chay Chaupal', 'chaychaupal')
ON DUPLICATE KEY UPDATE
    name = VALUES(name),
    is_active = 1;

SET @default_client_id := COALESCE(
    (SELECT id FROM clients WHERE slug = 'daalroti' LIMIT 1),
    (SELECT id FROM clients ORDER BY id LIMIT 1)
);

DELIMITER $$

DROP PROCEDURE IF EXISTS add_column_if_missing $$
CREATE PROCEDURE add_column_if_missing(
    IN table_name_value VARCHAR(64),
    IN column_name_value VARCHAR(64),
    IN column_definition TEXT
)
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = table_name_value
          AND COLUMN_NAME = column_name_value
    ) THEN
        SET @ddl = CONCAT('ALTER TABLE `', table_name_value, '` ADD COLUMN `', column_name_value, '` ', column_definition);
        PREPARE stmt FROM @ddl;
        EXECUTE stmt;
        DEALLOCATE PREPARE stmt;
    END IF;
END $$

DROP PROCEDURE IF EXISTS add_index_if_missing $$
CREATE PROCEDURE add_index_if_missing(
    IN table_name_value VARCHAR(64),
    IN index_name_value VARCHAR(64),
    IN index_definition TEXT
)
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = table_name_value
          AND INDEX_NAME = index_name_value
    ) THEN
        SET @ddl = index_definition;
        PREPARE stmt FROM @ddl;
        EXECUTE stmt;
        DEALLOCATE PREPARE stmt;
    END IF;
END $$

DELIMITER ;

CALL add_column_if_missing('users', 'client_id', 'INT NOT NULL DEFAULT 1 AFTER uuid');
CALL add_column_if_missing('orders', 'client_id', 'INT NOT NULL DEFAULT 1 AFTER uuid');
CALL add_column_if_missing('customers', 'client_id', 'INT NOT NULL DEFAULT 1 AFTER uuid');
CALL add_column_if_missing('customer_ledger_entries', 'client_id', 'INT NOT NULL DEFAULT 1 AFTER uuid');

UPDATE users SET client_id = @default_client_id WHERE client_id = 1 OR client_id IS NULL;
UPDATE orders SET client_id = @default_client_id WHERE client_id = 1 OR client_id IS NULL;
UPDATE customers SET client_id = @default_client_id WHERE client_id = 1 OR client_id IS NULL;
UPDATE customer_ledger_entries SET client_id = @default_client_id WHERE client_id = 1 OR client_id IS NULL;

CALL add_index_if_missing('users', 'idx_users_client_email', 'CREATE INDEX idx_users_client_email ON users(client_id, email)');
CALL add_index_if_missing('users', 'idx_users_client_phone', 'CREATE INDEX idx_users_client_phone ON users(client_id, phone)');
CALL add_index_if_missing('orders', 'idx_orders_client_created_at', 'CREATE INDEX idx_orders_client_created_at ON orders(client_id, created_at)');
CALL add_index_if_missing('orders', 'idx_orders_client_table_status', 'CREATE INDEX idx_orders_client_table_status ON orders(client_id, table_id, order_status)');
CALL add_index_if_missing('customers', 'idx_customers_client_mobile', 'CREATE INDEX idx_customers_client_mobile ON customers(client_id, normalized_mobile)');
CALL add_index_if_missing('customer_ledger_entries', 'idx_customer_ledger_client_customer', 'CREATE INDEX idx_customer_ledger_client_customer ON customer_ledger_entries(client_id, customer_id)');

SET @customer_mobile_unique := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'customers'
      AND INDEX_NAME = 'uq_customers_normalized_mobile'
);
SET @drop_customer_unique := IF(@customer_mobile_unique > 0, 'ALTER TABLE customers DROP INDEX uq_customers_normalized_mobile', 'SELECT 1');
PREPARE stmt FROM @drop_customer_unique;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

CALL add_index_if_missing('customers', 'uq_customers_client_normalized_mobile', 'CREATE UNIQUE INDEX uq_customers_client_normalized_mobile ON customers(client_id, normalized_mobile)');

CREATE TABLE IF NOT EXISTS table_client_states (
    id INT AUTO_INCREMENT PRIMARY KEY,
    client_id INT NOT NULL,
    table_id INT NOT NULL,
    table_status VARCHAR(30) DEFAULT 'available',
    current_amount DECIMAL(10,2) DEFAULT 0,
    order_timestamp BIGINT NULL,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY uq_table_client_state (client_id, table_id),
    KEY idx_table_client_states_table (table_id)
);

INSERT INTO table_client_states (client_id, table_id, table_status, current_amount, order_timestamp)
SELECT @default_client_id, id, table_status, current_amount, order_timestamp
FROM restaurant_tables
WHERE COALESCE(table_status, 'available') <> 'available'
ON DUPLICATE KEY UPDATE
    table_status = VALUES(table_status),
    current_amount = VALUES(current_amount),
    order_timestamp = VALUES(order_timestamp);

CREATE TABLE IF NOT EXISTS app_settings (
    `key` VARCHAR(100) PRIMARY KEY,
    `value` JSON NULL,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS app_settings_new (
    client_id INT NOT NULL DEFAULT 1,
    `key` VARCHAR(100) NOT NULL,
    `value` JSON NULL,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (client_id, `key`)
);

INSERT INTO app_settings_new (client_id, `key`, `value`, updated_at)
SELECT @default_client_id, `key`, `value`, updated_at
FROM app_settings
ON DUPLICATE KEY UPDATE
    `value` = VALUES(`value`),
    updated_at = VALUES(updated_at);

RENAME TABLE app_settings TO app_settings_old_client_migration, app_settings_new TO app_settings;
DROP TABLE app_settings_old_client_migration;

DROP PROCEDURE IF EXISTS add_column_if_missing;
DROP PROCEDURE IF EXISTS add_index_if_missing;
