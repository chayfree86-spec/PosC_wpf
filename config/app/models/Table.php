<?php

namespace App\Models;

use App\Core\Database;

class Table
{
    public static function ensureStateTable(): void
    {
        Database::connection()->exec(
            "CREATE TABLE IF NOT EXISTS table_client_states (
                id INT AUTO_INCREMENT PRIMARY KEY,
                client_id INT NOT NULL,
                table_id INT NOT NULL,
                table_status VARCHAR(30) DEFAULT 'available',
                current_amount DECIMAL(10,2) DEFAULT 0,
                order_timestamp BIGINT NULL,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                UNIQUE KEY uq_table_client_state (client_id, table_id),
                KEY idx_table_client_states_table (table_id)
            )"
        );
    }

    public static function all(): array
    {
        self::ensureStateTable();
        $stmt = Database::connection()->prepare(
            'SELECT rt.*,
                    COALESCE(ts.table_status, "available") AS status,
                    COALESCE(ts.current_amount, 0) AS amount,
                    ts.order_timestamp AS orderTimestamp,
                    da.name AS area_name
             FROM restaurant_tables rt
             LEFT JOIN table_client_states ts ON ts.table_id = rt.id AND ts.client_id = ?
             LEFT JOIN dining_areas da ON da.id = rt.area_id
             ORDER BY rt.id'
        );
        $stmt->execute([Client::currentId()]);

        return $stmt->fetchAll();
    }

    public static function find(int $id): ?array
    {
        self::ensureStateTable();
        $stmt = Database::connection()->prepare(
            'SELECT rt.*,
                    COALESCE(ts.table_status, "available") AS status,
                    COALESCE(ts.current_amount, 0) AS amount,
                    ts.order_timestamp AS orderTimestamp,
                    da.name AS area_name
             FROM restaurant_tables rt
             LEFT JOIN table_client_states ts ON ts.table_id = rt.id AND ts.client_id = ?
             LEFT JOIN dining_areas da ON da.id = rt.area_id
             WHERE rt.id = ?'
        );
        $stmt->execute([Client::currentId(), $id]);
        $table = $stmt->fetch();

        return $table ?: null;
    }

    public static function updateState(int $id, string $status, float $amount = 0, ?int $timestamp = null): bool
    {
        self::ensureStateTable();
        $stmt = Database::connection()->prepare(
            'INSERT INTO table_client_states (client_id, table_id, table_status, current_amount, order_timestamp)
             VALUES (?, ?, ?, ?, ?)
             ON DUPLICATE KEY UPDATE
                table_status = VALUES(table_status),
                current_amount = VALUES(current_amount),
                order_timestamp = VALUES(order_timestamp),
                updated_at = CURRENT_TIMESTAMP'
        );
        return $stmt->execute([
            Client::currentId(),
            $id,
            $status,
            $status === 'available' ? 0 : $amount,
            $status === 'available' ? null : $timestamp,
        ]);
    }

    public static function create(array $data): int
    {
        $stmt = Database::connection()->prepare(
            'INSERT INTO restaurant_tables (uuid, table_number, area_id, qr_code, qr_token, table_status, current_amount, order_timestamp, is_active)
             VALUES (UUID(), ?, ?, ?, ?, ?, ?, ?, ?)'
        );
        $stmt->execute([
            $data['table_number'],
            $data['area_id'] ?? null,
            $data['qr_code'] ?? null,
            $data['qr_token'] ?? bin2hex(random_bytes(16)),
            $data['table_status'] ?? 'available',
            $data['current_amount'] ?? 0,
            $data['order_timestamp'] ?? null,
            $data['is_active'] ?? 1,
        ]);

        return (int) Database::connection()->lastInsertId();
    }

    public static function update(int $id, array $data): bool
    {
        $stmt = Database::connection()->prepare(
            'UPDATE restaurant_tables
             SET table_number = ?, area_id = ?, qr_code = ?, qr_token = COALESCE(?, qr_token), is_active = ?, sync_version = sync_version + 1
             WHERE id = ?'
        );

        return $stmt->execute([
            $data['table_number'],
            $data['area_id'] ?? null,
            $data['qr_code'] ?? null,
            $data['qr_token'] ?? null,
            $data['is_active'] ?? 1,
            $id,
        ]);
    }

    public static function updateStatus(int $id, string $status, float $amount = 0, ?int $timestamp = null): bool
    {
        $db = Database::connection();
        $db->beginTransaction();

        try {
            $ok = self::updateState($id, $status, $amount, $timestamp);

            if ($status === 'available') {
                self::clearActiveOrders($id);
            }

            $db->commit();
            return $ok;
        } catch (\Throwable $error) {
            $db->rollBack();
            throw $error;
        }
    }

    private static function clearActiveOrders(int $tableId): void
    {
        $db = Database::connection();
        $find = $db->prepare(
            "SELECT id
             FROM orders
             WHERE table_id = ?
               AND client_id = ?
               AND order_status NOT IN ('completed', 'cancelled')"
        );
        $find->execute([$tableId, Client::currentId()]);
        $ids = array_map('intval', array_column($find->fetchAll(), 'id'));

        if (!$ids) {
            return;
        }

        $placeholders = implode(',', array_fill(0, count($ids), '?'));

        $deleteItems = $db->prepare("DELETE FROM order_items WHERE order_id IN ($placeholders)");
        $deleteItems->execute($ids);

        $completeOrders = $db->prepare(
            "UPDATE orders
             SET order_status = 'completed', total_amount = 0, sync_version = sync_version + 1
             WHERE id IN ($placeholders)"
        );
        $completeOrders->execute($ids);
    }

    public static function delete(int $id): bool
    {
        $stmt = Database::connection()->prepare('DELETE FROM restaurant_tables WHERE id = ?');
        return $stmt->execute([$id]);
    }
}
