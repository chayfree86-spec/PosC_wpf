<?php

namespace App\Models;

use App\Core\Database;

class OrderItem
{
    private static bool $schemaChecked = false;

    public static function ensureSchema(): void
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
        $columns = [
            'discount_amount' => 'DECIMAL(10,2) DEFAULT 0',
            'discount_type' => 'VARCHAR(20) DEFAULT NULL',
            'discount_value' => 'DECIMAL(10,2) DEFAULT 0',
            'discount_label' => 'VARCHAR(150) DEFAULT NULL',
        ];

        foreach ($columns as $column => $definition) {
            $exists = $db->prepare(
                "SELECT 1
                 FROM INFORMATION_SCHEMA.COLUMNS
                 WHERE TABLE_SCHEMA = DATABASE()
                   AND TABLE_NAME = 'order_items'
                   AND COLUMN_NAME = ?
                 LIMIT 1"
            );
            $exists->execute([$column]);
            if (!$exists->fetch()) {
                $db->exec("ALTER TABLE order_items ADD COLUMN {$column} {$definition}");
            }
        }

        self::$schemaChecked = true;
    }

    public static function createForOrder(int $orderId, array $item): int
    {
        self::ensureSchema();
        $price = (float) ($item['price'] ?? 0);
        $quantity = (int) ($item['quantity'] ?? $item['qty'] ?? 1);
        $clientItemId = (string) ($item['client_item_id'] ?? $item['id'] ?? '');
        $itemId = $item['item_id'] ?? null;

        if ($itemId === null && preg_match('/^\d+/', $clientItemId, $matches)) {
            $itemId = (int) $matches[0];
        }

        // Extract item level discount fields
        $discAmount = (float) ($item['discount_amount'] ?? $item['discountAmount'] ?? 0);
        $discType = $item['discount_type'] ?? $item['discountType'] ?? null;
        $discValue = (float) ($item['discount_value'] ?? $item['discountValue'] ?? 0);
        $discLabel = $item['discount_label'] ?? $item['discountLabel'] ?? null;

        $subtotal = $price * $quantity;
        $finalTotal = max(0, $subtotal - $discAmount);

        $stmt = Database::connection()->prepare(
            'INSERT INTO order_items (uuid, order_id, item_id, client_item_id, item_name, price, quantity, is_parcel, total, discount_amount, discount_type, discount_value, discount_label)
             VALUES (UUID(), ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)'
        );
        $stmt->execute([
            $orderId,
            $itemId,
            $clientItemId ?: null,
            $item['item_name'] ?? $item['name'] ?? null,
            $price,
            $quantity,
            !empty($item['is_parcel']) || !empty($item['isParcel']) ? 1 : 0,
            $finalTotal,
            $discAmount,
            $discType ?: null,
            $discValue,
            $discLabel ?: null
        ]);

        return (int) Database::connection()->lastInsertId();
    }
}
