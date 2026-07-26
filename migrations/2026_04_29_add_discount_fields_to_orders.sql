-- Adds discount metadata to orders without changing existing totals or rows.
-- Safe to run more than once: each column is added only when missing.

DROP PROCEDURE IF EXISTS add_order_discount_column_if_missing;

DELIMITER $$

CREATE PROCEDURE add_order_discount_column_if_missing(
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

CALL add_order_discount_column_if_missing('discount_amount', 'DECIMAL(10,2) DEFAULT 0 AFTER `total_amount`');
CALL add_order_discount_column_if_missing('discount_type', 'VARCHAR(20) DEFAULT NULL AFTER `discount_amount`');
CALL add_order_discount_column_if_missing('discount_value', 'DECIMAL(10,2) DEFAULT 0 AFTER `discount_type`');
CALL add_order_discount_column_if_missing('discount_label', 'VARCHAR(150) DEFAULT NULL AFTER `discount_value`');
CALL add_order_discount_column_if_missing('discount_date', 'DATE DEFAULT NULL AFTER `discount_label`');
CALL add_order_discount_column_if_missing('discount_start_time', 'VARCHAR(10) DEFAULT NULL AFTER `discount_date`');
CALL add_order_discount_column_if_missing('discount_end_time', 'VARCHAR(10) DEFAULT NULL AFTER `discount_start_time`');
CALL add_order_discount_column_if_missing('discount_is_paused', 'TINYINT(1) DEFAULT 0 AFTER `discount_end_time`');

DROP PROCEDURE IF EXISTS add_order_discount_column_if_missing;
