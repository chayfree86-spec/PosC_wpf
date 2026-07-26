-- Adds optional customer details to orders.
-- Safe to run more than once: each column is added only when missing.

DROP PROCEDURE IF EXISTS add_order_customer_column_if_missing;

DELIMITER $$

CREATE PROCEDURE add_order_customer_column_if_missing(
    IN p_column_name VARCHAR(64),
    IN p_column_definition TEXT
)
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'orders'
          AND COLUMN_NAME = p_column_name
    ) THEN
        SET @sql = CONCAT('ALTER TABLE `orders` ADD COLUMN `', p_column_name, '` ', p_column_definition);
        PREPARE stmt FROM @sql;
        EXECUTE stmt;
        DEALLOCATE PREPARE stmt;
    END IF;
END$$

DELIMITER ;

CALL add_order_customer_column_if_missing('customer_name', 'VARCHAR(150) DEFAULT NULL AFTER `discount_is_paused`');
CALL add_order_customer_column_if_missing('customer_mobile', 'VARCHAR(20) DEFAULT NULL AFTER `customer_name`');

DROP PROCEDURE IF EXISTS add_order_customer_column_if_missing;
