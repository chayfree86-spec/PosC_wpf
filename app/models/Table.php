<?php

namespace App\Models;

use App\Core\Database;

class Table
{
    private static bool $stateTableChecked = false;

    public static function ensureStateTable(): void
    {
        if (self::$stateTableChecked) {
            return;
        }

        $cacheFile = sys_get_temp_dir() . '/pos_schema_checked_' . md5((string) env('DB_DATABASE', 'pos_qr_system')) . '.cache';
        if (file_exists($cacheFile) && (time() - filemtime($cacheFile)) < 86400) {
            self::$stateTableChecked = true;
            return;
        }

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
        self::$stateTableChecked = true;
    }

    public static function all(): array
    {
        self::ensureStateTable();
        $statusSql = self::effectiveStatusSql('ts');
        $stmt = Database::connection()->prepare(
            'SELECT rt.*,
                    ' . $statusSql . ' AS status,
                    CASE WHEN ' . $statusSql . ' = \'available\' THEN 0 ELSE COALESCE(ts.current_amount, 0) END AS amount,
                    CASE WHEN ' . $statusSql . ' = \'available\' THEN NULL ELSE ts.order_timestamp END AS orderTimestamp,
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
        $statusSql = self::effectiveStatusSql('ts');
        $stmt = Database::connection()->prepare(
            'SELECT rt.*,
                    ' . $statusSql . ' AS status,
                    CASE WHEN ' . $statusSql . ' = \'available\' THEN 0 ELSE COALESCE(ts.current_amount, 0) END AS amount,
                    CASE WHEN ' . $statusSql . ' = \'available\' THEN NULL ELSE ts.order_timestamp END AS orderTimestamp,
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

    private static function effectiveStatusSql(string $alias): string
    {
        // Show 'available' if table_client_states says ordered/occupied
        // but no actual active/unsettled order exists for this table
        return "CASE WHEN COALESCE({$alias}.table_status, 'available') IN ('ordered', 'occupied')
            AND NOT EXISTS (
                SELECT 1 FROM orders o
                WHERE o.table_id = {$alias}.table_id
                  AND o.client_id = {$alias}.client_id
                  AND o.order_status != 'cancelled'
                  AND (o.report_visible = 0 OR o.report_visible IS NULL)
            ) THEN 'available'
            ELSE COALESCE({$alias}.table_status, 'available')
        END";
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
        self::ensureStateTable();
        Client::currentId();

        $db = Database::connection();
        $db->beginTransaction();

        try {
            $ok = self::updateState($id, $status, $amount, $timestamp);

            // clearActiveOrders removed — status change should NOT wipe active orders
            // Orders are now managed by saveTableOrder / final bill flow only

            if ($db->inTransaction()) {
                $db->commit();
            }
            return $ok;
        } catch (\Throwable $error) {
            if ($db->inTransaction()) {
                $db->rollBack();
            }
            throw $error;
        }
    }

    public static function findByToken(string $token): ?array
    {
        self::ensureStateTable();
        $stmt = Database::connection()->prepare(
            'SELECT rt.*
             FROM restaurant_tables rt
             WHERE (rt.qr_token = ? OR rt.qr_code = ?) AND rt.is_active = 1 LIMIT 1'
        );
        $stmt->execute([$token, $token]);
        $table = $stmt->fetch();
        return $table ?: null;
    }

    public static function delete(int $id): bool
    {
        $stmt = Database::connection()->prepare('DELETE FROM restaurant_tables WHERE id = ?');
        return $stmt->execute([$id]);
    }
}
