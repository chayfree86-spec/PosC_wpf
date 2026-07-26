<?php

namespace App\Models;

use App\Core\Database;
use PDOException;

class Customer
{
    private static bool $schemaChecked = false;

    public static function ensureTable(): void
    {
        if (self::$schemaChecked) {
            return;
        }

        $cacheFile = sys_get_temp_dir() . '/pos_schema_checked_' . md5((string) env('DB_DATABASE', 'pos_qr_system')) . '.cache';
        if (file_exists($cacheFile) && (time() - filemtime($cacheFile)) < 86400) {
            self::$schemaChecked = true;
            return;
        }

        $db = Database::connection();
        $db->exec(
            "CREATE TABLE IF NOT EXISTS customers (
                id INT AUTO_INCREMENT PRIMARY KEY,
                uuid VARCHAR(36) UNIQUE,
                client_id INT NOT NULL DEFAULT 1,
                name VARCHAR(150) DEFAULT NULL,
                mobile VARCHAR(20) DEFAULT NULL,
                normalized_mobile VARCHAR(20) DEFAULT NULL,
                email VARCHAR(150) DEFAULT NULL,
                address TEXT,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                sync_version INT DEFAULT 1,
                KEY idx_customers_client (client_id)
            )"
        );

        self::ensureClientColumn();

        self::$schemaChecked = true;
    }

    private static function ensureClientColumn(): void
    {
        $db = Database::connection();
        $exists = $db->prepare(
            "SELECT 1
             FROM INFORMATION_SCHEMA.COLUMNS
             WHERE TABLE_SCHEMA = DATABASE()
               AND TABLE_NAME = 'customers'
               AND COLUMN_NAME = 'client_id'
             LIMIT 1"
        );
        $exists->execute();
        if (!$exists->fetch()) {
            $db->exec('ALTER TABLE customers ADD COLUMN client_id INT NOT NULL DEFAULT 1 AFTER uuid');
        }

        $indexExists = $db->prepare(
            "SELECT 1
             FROM INFORMATION_SCHEMA.STATISTICS
             WHERE TABLE_SCHEMA = DATABASE()
               AND TABLE_NAME = 'customers'
               AND INDEX_NAME = 'idx_customers_client_mobile'
             LIMIT 1"
        );
        $indexExists->execute();
        if (!$indexExists->fetch()) {
            $db->exec('CREATE INDEX idx_customers_client_mobile ON customers(client_id, normalized_mobile)');
        }
    }

    public static function normalizeMobile(?string $mobile): ?string
    {
        $digits = preg_replace('/\D+/', '', (string) $mobile);
        return $digits !== '' ? substr($digits, -15) : null;
    }

    public static function findOrCreate(?string $name, ?string $mobile): ?int
    {
        self::ensureTable();

        $name = isset($name) && trim($name) !== '' ? trim($name) : null;
        $mobile = isset($mobile) && trim($mobile) !== '' ? trim($mobile) : null;
        $normalizedMobile = self::normalizeMobile($mobile);

        if ($name === null && $mobile === null) {
            return null;
        }

        $db = Database::connection();

        if ($normalizedMobile !== null) {
            $find = $db->prepare('SELECT id, name FROM customers WHERE normalized_mobile = ? AND client_id = ? LIMIT 1');
            $find->execute([$normalizedMobile, Client::currentId()]);
            $existing = $find->fetch();

            if ($existing) {
                if ($name !== null && trim((string) ($existing['name'] ?? '')) === '') {
                    $update = $db->prepare('UPDATE customers SET name = ?, mobile = COALESCE(mobile, ?), sync_version = sync_version + 1 WHERE id = ? AND client_id = ?');
                    $update->execute([$name, $mobile, $existing['id'], Client::currentId()]);
                }

                return (int) $existing['id'];
            }
        }

        try {
            $insert = $db->prepare(
                'INSERT INTO customers (uuid, client_id, name, mobile, normalized_mobile) VALUES (UUID(), ?, ?, ?, ?)'
            );
            $insert->execute([Client::currentId(), $name, $mobile, $normalizedMobile]);
            return (int) $db->lastInsertId();
        } catch (PDOException $error) {
            if ($normalizedMobile === null || $error->getCode() !== '23000') {
                throw $error;
            }

            $find = $db->prepare('SELECT id FROM customers WHERE normalized_mobile = ? AND client_id = ? LIMIT 1');
            $find->execute([$normalizedMobile, Client::currentId()]);
            $id = $find->fetchColumn();
            return $id ? (int) $id : null;
        }
    }

    public static function findByMobile(string $mobile): ?array
    {
        self::ensureTable();
        $normalizedMobile = self::normalizeMobile($mobile);
        if ($normalizedMobile === null) {
            return null;
        }

        $db = Database::connection();
        $stmt = $db->prepare('SELECT id, name, mobile FROM customers WHERE normalized_mobile = ? AND client_id = ? LIMIT 1');
        $stmt->execute([$normalizedMobile, Client::currentId()]);
        return $stmt->fetch() ?: null;
    }
}
