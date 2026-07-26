-- Backend table for Len-Den customer udhari/jama entries.

CREATE TABLE IF NOT EXISTS customer_ledger_entries (
    id INT AUTO_INCREMENT PRIMARY KEY,
    uuid VARCHAR(36) UNIQUE,
    customer_id INT NOT NULL,
    entry_type ENUM('debit','credit') NOT NULL,
    amount DECIMAL(10,2) NOT NULL,
    note VARCHAR(255) DEFAULT NULL,
    created_by INT NULL,
    occurred_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    sync_version INT DEFAULT 1,
    CONSTRAINT fk_customer_ledger_customer
      FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE CASCADE,
    CONSTRAINT fk_customer_ledger_created_by
      FOREIGN KEY (created_by) REFERENCES users(id) ON DELETE SET NULL
);

DROP PROCEDURE IF EXISTS add_customer_ledger_index_if_missing;

DELIMITER $$

CREATE PROCEDURE add_customer_ledger_index_if_missing(
    IN p_index_name VARCHAR(64),
    IN p_index_columns VARCHAR(255)
)
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'customer_ledger_entries'
          AND INDEX_NAME = p_index_name
    ) THEN
        SET @sql = CONCAT('CREATE INDEX `', p_index_name, '` ON customer_ledger_entries(', p_index_columns, ')');
        PREPARE stmt FROM @sql;
        EXECUTE stmt;
        DEALLOCATE PREPARE stmt;
    END IF;
END$$

DELIMITER ;

CALL add_customer_ledger_index_if_missing('idx_customer_ledger_customer', 'customer_id');
CALL add_customer_ledger_index_if_missing('idx_customer_ledger_occurred', 'occurred_at');

DROP PROCEDURE IF EXISTS add_customer_ledger_index_if_missing;
