<?php

namespace App\Models;

use App\Core\Database;
use PDO;

/**
 * QR orders placed by customers from the mobile-menu PWA. This is a SEPARATE
 * table from `orders` -- it is just the pending request queue between a
 * customer's phone and the POS operator. When the operator accepts a QR order
 * the POS places it on the table through the normal table-order flow (the
 * regular `orders` table); this model never touches that flow.
 *
 * Status lifecycle: pending -> accepted | rejected.
 */
class QrOrder
{
    private static bool $schemaChecked = false;

    public static function ensureTable(): void
    {
        if (self::$schemaChecked) {
            return;
        }

        Database::connection()->exec(
            "CREATE TABLE IF NOT EXISTS qr_orders (
                id INT AUTO_INCREMENT PRIMARY KEY,
                client_id INT NOT NULL DEFAULT 1,
                table_id INT NULL,
                table_token VARCHAR(64) NULL,
                table_number VARCHAR(50) NULL,
                items LONGTEXT NOT NULL,
                total_amount DECIMAL(10,2) NOT NULL DEFAULT 0,
                customer_name VARCHAR(150) NULL,
                customer_mobile VARCHAR(20) NULL,
                status VARCHAR(20) NOT NULL DEFAULT 'pending',
                created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at DATETIME NULL,
                KEY idx_qr_client_status (client_id, status),
                KEY idx_qr_created (created_at)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4"
        );

        self::$schemaChecked = true;
    }

    /** Insert a new pending QR order. Returns the new id. */
    public static function create(array $data): int
    {
        self::ensureTable();
        $db = Database::connection();

        $stmt = $db->prepare(
            "INSERT INTO qr_orders
                (client_id, table_id, table_token, table_number, items, total_amount,
                 customer_name, customer_mobile, status, created_at)
             VALUES (?, ?, ?, ?, ?, ?, ?, ?, 'pending', NOW())"
        );
        $stmt->execute([
            (int) $data['client_id'],
            isset($data['table_id']) && $data['table_id'] !== null ? (int) $data['table_id'] : null,
            $data['table_token'] ?? null,
            $data['table_number'] ?? null,
            json_encode($data['items'] ?? [], JSON_UNESCAPED_UNICODE),
            (float) ($data['total_amount'] ?? 0),
            $data['customer_name'] ?? null,
            $data['customer_mobile'] ?? null,
        ]);

        return (int) $db->lastInsertId();
    }

    /** Pending QR orders for one client (newest first), for the POS board. */
    public static function pendingForClient(int $clientId): array
    {
        self::ensureTable();
        $stmt = Database::connection()->prepare(
            "SELECT * FROM qr_orders
             WHERE client_id = ? AND status = 'pending'
             ORDER BY created_at DESC"
        );
        $stmt->execute([$clientId]);
        return array_map([self::class, 'hydrate'], $stmt->fetchAll());
    }

    /**
     * Orders for the full POS board: still-open ones = pending + accepted (an
     * accepted order stays visible until its table bill is settled). Pending
     * first, then accepted, each newest first.
     */
    public static function forBoard(int $clientId): array
    {
        self::ensureTable();
        $stmt = Database::connection()->prepare(
            "SELECT * FROM qr_orders
             WHERE client_id = ? AND status IN ('pending', 'accepted')
             ORDER BY FIELD(status, 'pending', 'accepted'), created_at DESC"
        );
        $stmt->execute([$clientId]);
        return array_map([self::class, 'hydrate'], $stmt->fetchAll());
    }

    /** A single QR order scoped to its client (used by the customer status poll). */
    public static function find(int $id, int $clientId): ?array
    {
        self::ensureTable();
        $stmt = Database::connection()->prepare(
            "SELECT * FROM qr_orders WHERE id = ? AND client_id = ? LIMIT 1"
        );
        $stmt->execute([$id, $clientId]);
        $row = $stmt->fetch();
        return $row ? self::hydrate($row) : null;
    }

    /** Accept/reject (or otherwise transition) a pending order, client-scoped. */
    public static function updateStatus(int $id, int $clientId, string $status): bool
    {
        self::ensureTable();
        $stmt = Database::connection()->prepare(
            "UPDATE qr_orders SET status = ?, updated_at = NOW()
             WHERE id = ? AND client_id = ?"
        );
        $stmt->execute([$status, $id, $clientId]);
        return $stmt->rowCount() > 0;
    }

    /**
     * Mark every ACCEPTED QR order for a table as settled. Called when the POS
     * settles that table's bill -> the customer's PWA then shows "completed".
     * Returns the number of orders updated.
     */
    public static function settleForTable(int $tableId, int $clientId): int
    {
        self::ensureTable();
        $stmt = Database::connection()->prepare(
            "UPDATE qr_orders SET status = 'settled', updated_at = NOW()
             WHERE table_id = ? AND client_id = ? AND status = 'accepted'"
        );
        $stmt->execute([$tableId, $clientId]);
        return $stmt->rowCount();
    }

    /** Decode the JSON items column so the API returns a proper array. */
    private static function hydrate(array $row): array
    {
        $items = json_decode($row['items'] ?? '[]', true);
        $row['items'] = is_array($items) ? $items : [];
        $row['total_amount'] = (float) ($row['total_amount'] ?? 0);
        return $row;
    }
}
