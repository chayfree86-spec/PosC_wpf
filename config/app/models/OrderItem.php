<?php

namespace App\Models;

use App\Core\Database;

class OrderItem
{
    public static function createForOrder(int $orderId, array $item): int
    {
        $price = (float) ($item['price'] ?? 0);
        $quantity = (int) ($item['quantity'] ?? $item['qty'] ?? 1);
        $clientItemId = (string) ($item['client_item_id'] ?? $item['id'] ?? '');
        $itemId = $item['item_id'] ?? null;

        if ($itemId === null && preg_match('/^\d+/', $clientItemId, $matches)) {
            $itemId = (int) $matches[0];
        }

        $stmt = Database::connection()->prepare(
            'INSERT INTO order_items (uuid, order_id, item_id, client_item_id, item_name, price, quantity, is_parcel, total)
             VALUES (UUID(), ?, ?, ?, ?, ?, ?, ?, ?)'
        );
        $stmt->execute([
            $orderId,
            $itemId,
            $clientItemId ?: null,
            $item['item_name'] ?? $item['name'] ?? null,
            $price,
            $quantity,
            !empty($item['is_parcel']) || !empty($item['isParcel']) ? 1 : 0,
            $price * $quantity,
        ]);

        return (int) Database::connection()->lastInsertId();
    }
}
