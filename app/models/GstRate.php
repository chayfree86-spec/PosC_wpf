<?php

namespace App\Models;

use App\Core\Database;

class GstRate
{
    private static function ensureTable(): void
    {
        Database::connection()->exec(
            "CREATE TABLE IF NOT EXISTS gst_rates (
                id INT AUTO_INCREMENT PRIMARY KEY,
                uuid VARCHAR(36) UNIQUE,
                name VARCHAR(100) NOT NULL,
                rate_percent DECIMAL(5,2) NOT NULL DEFAULT 0,
                is_active TINYINT(1) DEFAULT 1,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                sync_version INT DEFAULT 1
            )"
        );
    }

    public static function all(): array
    {
        self::ensureTable();

        return Database::connection()
            ->query('SELECT * FROM gst_rates ORDER BY id')
            ->fetchAll();
    }

    public static function find(int $id): ?array
    {
        self::ensureTable();
        $stmt = Database::connection()->prepare('SELECT * FROM gst_rates WHERE id = ?');
        $stmt->execute([$id]);
        $rate = $stmt->fetch();

        return $rate ?: null;
    }

    public static function create(array $data): int
    {
        self::ensureTable();
        $stmt = Database::connection()->prepare(
            'INSERT INTO gst_rates (uuid, name, rate_percent, is_active) VALUES (UUID(), ?, ?, ?)'
        );
        $stmt->execute([
            $data['name'],
            $data['rate_percent'] ?? $data['val'] ?? 0,
            $data['is_active'] ?? 1,
        ]);

        return (int) Database::connection()->lastInsertId();
    }

    public static function update(int $id, array $data): bool
    {
        self::ensureTable();
        $stmt = Database::connection()->prepare(
            'UPDATE gst_rates SET name = ?, rate_percent = ?, is_active = ?, sync_version = sync_version + 1 WHERE id = ?'
        );

        return $stmt->execute([
            $data['name'],
            $data['rate_percent'] ?? $data['val'] ?? 0,
            $data['is_active'] ?? 1,
            $id,
        ]);
    }

    public static function delete(int $id): bool
    {
        self::ensureTable();
        $stmt = Database::connection()->prepare('DELETE FROM gst_rates WHERE id = ?');
        return $stmt->execute([$id]);
    }
}
