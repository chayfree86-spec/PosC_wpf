<?php

namespace App\Models;

use App\Core\Database;
use App\Models\Client;

class Discount
{
    private static bool $tableChecked = false;

    public static function ensureTable(): void
    {
        if (self::$tableChecked) {
            return;
        }

        Database::connection()->exec(
            "CREATE TABLE IF NOT EXISTS discounts (
                id INT AUTO_INCREMENT PRIMARY KEY,
                client_id INT NOT NULL,
                name VARCHAR(150) NOT NULL,
                discount_type VARCHAR(20) NOT NULL,
                discount_value DECIMAL(10,2) NOT NULL DEFAULT 0,
                min_order_amount DECIMAL(10,2) DEFAULT 0,
                max_discount DECIMAL(10,2) NULL,
                is_paused TINYINT(1) DEFAULT 0,
                start_time VARCHAR(10) NULL,
                end_time VARCHAR(10) NULL,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                KEY idx_discounts_client (client_id)
            )"
        );
        self::$tableChecked = true;
    }

    public static function all(): array
    {
        self::ensureTable();
        $stmt = Database::connection()->prepare(
            'SELECT * FROM discounts WHERE client_id = ? ORDER BY id DESC'
        );
        $stmt->execute([Client::currentId()]);
        return $stmt->fetchAll();
    }

    public static function find(int $id): ?array
    {
        self::ensureTable();
        $stmt = Database::connection()->prepare(
            'SELECT * FROM discounts WHERE id = ? AND client_id = ?'
        );
        $stmt->execute([$id, Client::currentId()]);
        $item = $stmt->fetch();
        return $item ?: null;
    }

    public static function create(array $data): int
    {
        self::ensureTable();
        $stmt = Database::connection()->prepare(
            'INSERT INTO discounts (client_id, name, discount_type, discount_value, min_order_amount, max_discount, is_paused, start_time, end_time)
             VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)'
        );
        $stmt->execute([
            Client::currentId(),
            $data['name'] ?? '',
            $data['discount_type'] ?? 'percent',
            $data['discount_value'] ?? 0,
            $data['min_order_amount'] ?? 0,
            $data['max_discount'] ?? null,
            !empty($data['is_paused']) ? 1 : 0,
            $data['start_time'] ?? null,
            $data['end_time'] ?? null
        ]);

        return (int) Database::connection()->lastInsertId();
    }

    public static function update(int $id, array $data): bool
    {
        self::ensureTable();
        $stmt = Database::connection()->prepare(
            'UPDATE discounts 
             SET name = ?, discount_type = ?, discount_value = ?, min_order_amount = ?, max_discount = ?, is_paused = ?, start_time = ?, end_time = ?
             WHERE id = ? AND client_id = ?'
        );
        return $stmt->execute([
            $data['name'] ?? '',
            $data['discount_type'] ?? 'percent',
            $data['discount_value'] ?? 0,
            $data['min_order_amount'] ?? 0,
            $data['max_discount'] ?? null,
            !empty($data['is_paused']) ? 1 : 0,
            $data['start_time'] ?? null,
            $data['end_time'] ?? null,
            $id,
            Client::currentId()
        ]);
    }

    public static function delete(int $id): bool
    {
        self::ensureTable();
        $stmt = Database::connection()->prepare(
            'DELETE FROM discounts WHERE id = ? AND client_id = ?'
        );
        return $stmt->execute([$id, Client::currentId()]);
    }
}
