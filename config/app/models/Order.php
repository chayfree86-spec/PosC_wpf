<?php

namespace App\Models;

use App\Core\Database;
use PDO;

class Order
{
    private static bool $orderColumnsChecked = false;

    public static function ensureSchema(): void
    {
        self::ensureOrderColumns();
    }

    private static function ensureOrderColumns(): void
    {
        if (self::$orderColumnsChecked) {
            return;
        }

        $db = Database::connection();
        Customer::ensureTable();
        Table::ensureStateTable();

        $columns = [
            'client_id' => 'INT NOT NULL DEFAULT 1',
            'customer_id' => 'INT NULL',
            'discount_amount' => 'DECIMAL(10,2) DEFAULT 0',
            'discount_type' => 'VARCHAR(20) DEFAULT NULL',
            'discount_value' => 'DECIMAL(10,2) DEFAULT 0',
            'discount_label' => 'VARCHAR(150) DEFAULT NULL',
            'discount_date' => 'DATE DEFAULT NULL',
            'discount_start_time' => 'VARCHAR(10) DEFAULT NULL',
            'discount_end_time' => 'VARCHAR(10) DEFAULT NULL',
            'discount_is_paused' => 'TINYINT(1) DEFAULT 0',
            'customer_name' => 'VARCHAR(150) DEFAULT NULL',
            'customer_mobile' => 'VARCHAR(20) DEFAULT NULL',
            'is_kot_only' => 'TINYINT(1) NOT NULL DEFAULT 1',
            'report_visible' => 'TINYINT(1) NOT NULL DEFAULT 0',
            'billed_at' => 'DATETIME NULL',
            'bill_number' => 'INT NULL',
        ];

        foreach ($columns as $column => $definition) {
            $exists = $db->prepare(
                "SELECT 1
                 FROM INFORMATION_SCHEMA.COLUMNS
                 WHERE TABLE_SCHEMA = DATABASE()
                   AND TABLE_NAME = 'orders'
                   AND COLUMN_NAME = ?
                 LIMIT 1"
            );
            $exists->execute([$column]);
            if (!$exists->fetch()) {
                $db->exec("ALTER TABLE orders ADD COLUMN {$column} {$definition}");
            }
        }

        self::backfillReportMarkers();
        self::backfillBillNumbers();
        self::ensureReportIndexes();
        self::ensureCustomerForeignKey();
        self::backfillCustomerIds();
        self::$orderColumnsChecked = true;
    }

    private static function backfillReportMarkers(): void
    {
        $timezone = new \DateTimeZone((string) env('APP_TIMEZONE', 'Asia/Kolkata'));
        $databaseTimezone = new \DateTimeZone('UTC');
        $todayStart = (new \DateTimeImmutable('today', $timezone))
            ->setTimezone($databaseTimezone)
            ->format('Y-m-d H:i:s');

        Database::connection()
            ->prepare(
                "UPDATE orders
                 SET is_kot_only = 0,
                     report_visible = 1,
                     billed_at = created_at
                 WHERE billed_at IS NULL
                   AND report_visible = 0
                   AND is_kot_only = 1
                   AND order_status IN ('completed', 'complete', 'paid', 'settled')
                   AND total_amount > 0
                   AND created_at < ?"
            )
            ->execute([$todayStart]);
    }

    private static function ensureReportIndexes(): void
    {
        $db = Database::connection();

        $indexes = [
            'idx_orders_created_at' => 'CREATE INDEX idx_orders_created_at ON orders(created_at)',
            'idx_orders_status_created_at' => 'CREATE INDEX idx_orders_status_created_at ON orders(order_status, created_at)',
            'idx_orders_client_created_at' => 'CREATE INDEX idx_orders_client_created_at ON orders(client_id, created_at)',
            'idx_orders_client_table_status' => 'CREATE INDEX idx_orders_client_table_status ON orders(client_id, table_id, order_status)',
            'idx_orders_client_billed_at' => 'CREATE INDEX idx_orders_client_billed_at ON orders(client_id, billed_at)',
            'uniq_orders_client_bill_number' => 'CREATE UNIQUE INDEX uniq_orders_client_bill_number ON orders(client_id, bill_number)',
        ];

        foreach ($indexes as $indexName => $sql) {
            $exists = $db->prepare(
                "SELECT 1
                 FROM INFORMATION_SCHEMA.STATISTICS
                 WHERE TABLE_SCHEMA = DATABASE()
                   AND TABLE_NAME = 'orders'
                   AND INDEX_NAME = ?
                 LIMIT 1"
            );
            $exists->execute([$indexName]);
            if (!$exists->fetch()) {
                $db->exec($sql);
            }
        }
    }

    private static function ensureCustomerForeignKey(): void
    {
        $db = Database::connection();

        $indexExists = $db->prepare(
            "SELECT 1
             FROM INFORMATION_SCHEMA.STATISTICS
             WHERE TABLE_SCHEMA = DATABASE()
               AND TABLE_NAME = 'orders'
               AND INDEX_NAME = 'idx_orders_customer'
             LIMIT 1"
        );
        $indexExists->execute();
        if (!$indexExists->fetch()) {
            $db->exec('CREATE INDEX idx_orders_customer ON orders(customer_id)');
        }

        $constraintExists = $db->prepare(
            "SELECT 1
             FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
             WHERE TABLE_SCHEMA = DATABASE()
               AND TABLE_NAME = 'orders'
               AND CONSTRAINT_NAME = 'fk_orders_customer'
             LIMIT 1"
        );
        $constraintExists->execute();
        if (!$constraintExists->fetch()) {
            $db->exec(
                'ALTER TABLE orders
                 ADD CONSTRAINT fk_orders_customer
                 FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE SET NULL'
            );
        }
    }

    private static function backfillCustomerIds(): void
    {
        $db = Database::connection();
        $orders = $db->prepare(
            "SELECT id, customer_name, customer_mobile
             FROM orders
             WHERE customer_id IS NULL
               AND client_id = ?
               AND (
                    NULLIF(TRIM(COALESCE(customer_name, '')), '') IS NOT NULL
                 OR NULLIF(TRIM(COALESCE(customer_mobile, '')), '') IS NOT NULL
               )"
        );
        $orders->execute([Client::currentId()]);
        $orders = $orders->fetchAll();

        if (!$orders) {
            return;
        }

        $update = $db->prepare('UPDATE orders SET customer_id = ?, sync_version = sync_version + 1 WHERE id = ? AND client_id = ? AND customer_id IS NULL');

        foreach ($orders as $order) {
            $customerId = Customer::findOrCreate($order['customer_name'] ?? null, $order['customer_mobile'] ?? null);
            if ($customerId !== null) {
                $update->execute([$customerId, $order['id'], Client::currentId()]);
            }
        }
    }

    private static function discountData(array $data): array
    {
        $blankToNull = static fn ($value) => isset($value) && trim((string) $value) !== '' ? $value : null;
        $type = (string) ($data['discount_type'] ?? $data['discountType'] ?? '');
        $amount = max(0, (float) ($data['discount_amount'] ?? $data['discountAmount'] ?? 0));
        $value = max(0, (float) ($data['discount_value'] ?? $data['discountValue'] ?? 0));

        if ($type === '' && $amount <= 0) {
            $value = 0;
        }

        return [
            'discount_amount' => $amount,
            'discount_type' => $type !== '' ? $type : null,
            'discount_value' => $value,
            'discount_label' => $blankToNull($data['discount_label'] ?? $data['discountLabel'] ?? null),
            'discount_date' => $blankToNull($data['discount_date'] ?? $data['discountDate'] ?? null),
            'discount_start_time' => $blankToNull($data['discount_start_time'] ?? $data['discountStartTime'] ?? null),
            'discount_end_time' => $blankToNull($data['discount_end_time'] ?? $data['discountEndTime'] ?? null),
            'discount_is_paused' => !empty($data['discount_is_paused'] ?? $data['discountIsPaused'] ?? false) ? 1 : 0,
        ];
    }

    private static function customerData(array $data): array
    {
        $blankToNull = static fn ($value) => isset($value) && trim((string) $value) !== '' ? trim((string) $value) : null;

        return [
            'customer_name' => $blankToNull($data['customer_name'] ?? $data['customerName'] ?? null),
            'customer_mobile' => $blankToNull($data['customer_mobile'] ?? $data['customerMobile'] ?? null),
        ];
    }

    private static function backfillBillNumbers(): void
    {
        $db = Database::connection();
        $billFilter = "report_visible = 1 AND is_kot_only = 0 AND billed_at IS NOT NULL";
        $clients = $db->query(
            "SELECT DISTINCT client_id
             FROM orders
             WHERE {$billFilter}
               AND bill_number IS NULL
             ORDER BY client_id"
        )->fetchAll();

        foreach ($clients as $client) {
            $clientId = (int) $client['client_id'];
            $nextStmt = $db->prepare(
                "SELECT COALESCE(MAX(bill_number), 0) + 1
                 FROM orders
                 WHERE client_id = ?"
            );
            $nextStmt->execute([$clientId]);
            $next = max(1, (int) $nextStmt->fetchColumn());

            $orders = $db->prepare(
                "SELECT id
                 FROM orders
                 WHERE client_id = ?
                   AND {$billFilter}
                   AND bill_number IS NULL
                 ORDER BY billed_at, id"
            );
            $orders->execute([$clientId]);

            $update = $db->prepare('UPDATE orders SET bill_number = ? WHERE id = ? AND client_id = ? AND bill_number IS NULL');
            foreach ($orders->fetchAll() as $order) {
                $update->execute([$next++, (int) $order['id'], $clientId]);
            }
        }
    }

    private static function normalizeDateTime(mixed $value): ?string
    {
        if ($value === null || trim((string) $value) === '') {
            return null;
        }

        try {
            $tz = new \DateTimeZone((string) env('APP_TIMEZONE', 'Asia/Kolkata'));
            return (new \DateTimeImmutable((string) $value))->setTimezone($tz)->format('Y-m-d H:i:s');
        } catch (\Throwable) {
            return null;
        }
    }

    private static function billMarker(bool $isFinalBill, array $data = []): array
    {
        $tz = new \DateTimeZone((string) env('APP_TIMEZONE', 'Asia/Kolkata'));
        $nowLocal = (new \DateTimeImmutable('now', $tz))->format('Y-m-d H:i:s');
        return [
            'is_kot_only' => $isFinalBill ? 0 : 1,
            'report_visible' => $isFinalBill ? 1 : 0,
            'billed_at' => $isFinalBill ? (self::normalizeDateTime($data['billed_at'] ?? $data['billedAt'] ?? null) ?? $nowLocal) : null,
        ];
    }

    private static function logStatus(int $orderId, string $status, ?int $changedBy = null): void
    {
        $last = Database::connection()->prepare(
            'SELECT status FROM order_status_logs WHERE order_id = ? ORDER BY id DESC LIMIT 1'
        );
        $last->execute([$orderId]);
        $lastStatus = $last->fetchColumn();

        if ($lastStatus === $status) {
            return;
        }

        $stmt = Database::connection()->prepare(
            'INSERT INTO order_status_logs (order_id, status, changed_by) VALUES (?, ?, ?)'
        );
        $stmt->execute([$orderId, $status, $changedBy]);
    }

    private static function uiStatusToOrderStatus(string $status): string
    {
        return match ($status) {
            'complete', 'completed', 'available' => 'completed',
            'cancelled' => 'cancelled',
            'occupied' => 'confirmed',
            default => 'pending',
        };
    }

    public static function all(): array
    {
        self::ensureOrderColumns();

        $sql = 'SELECT o.*, rt.table_number, u.name AS staff_name
                , COALESCE(c.name, o.customer_name) AS customer_name
                , COALESCE(c.mobile, o.customer_mobile) AS customer_mobile
                FROM orders o
                LEFT JOIN customers c ON c.id = o.customer_id
                LEFT JOIN restaurant_tables rt ON rt.id = o.table_id
                LEFT JOIN users u ON u.id = o.created_by
                WHERE o.client_id = ?
                ORDER BY o.created_at DESC';

        $stmt = Database::connection()->prepare($sql);
        $stmt->execute([Client::currentId()]);
        $orders = $stmt->fetchAll();

        if (empty($orders)) {
            return [];
        }

        $db = Database::connection();
        $itemsStmt = $db->prepare(
            'SELECT oi.* 
             FROM order_items oi
             JOIN orders o ON o.id = oi.order_id
             WHERE o.client_id = ?
             ORDER BY oi.order_id, oi.id'
        );
        $itemsStmt->execute([Client::currentId()]);
        $items = $itemsStmt->fetchAll();

        $itemsByOrder = [];
        foreach ($items as $item) {
            $itemsByOrder[(int)$item['order_id']][] = $item;
        }

        foreach ($orders as &$order) {
            $order['items'] = $itemsByOrder[(int)$order['id']] ?? [];
        }
        unset($order);

        return $orders;
    }

    public static function nextBillNumber(): array
    {
        self::ensureOrderColumns();

        $stmt = Database::connection()->prepare(
            'SELECT COALESCE(MAX(bill_number), 0) + 1 FROM orders WHERE client_id = ?'
        );
        $stmt->execute([Client::currentId()]);
        $next = max(1, (int) $stmt->fetchColumn());
        $prefix = self::currentBillPrefix();

        return [
            'next_bill_number' => $next,
            'bill_number' => $next,
            'bill_prefix' => $prefix,
            'formatted_bill_number' => self::formatBillNumber($next, $prefix),
        ];
    }

    private static function currentBillPrefix(): string
    {
        $client = Client::current();
        $slug = strtolower((string) ($client['slug'] ?? ''));
        $name = strtolower((string) ($client['name'] ?? ''));

        if (str_contains($slug, 'chay') || str_contains($name, 'chay')) {
            return 'CC';
        }

        if (str_contains($slug, 'daal') || str_contains($slug, 'dal') || str_contains($name, 'daal') || str_contains($name, 'dal')) {
            return 'DR';
        }

        return 'BILL';
    }

    private static function formatBillNumber(int $billNumber, ?string $prefix = null): ?string
    {
        if ($billNumber <= 0) {
            return null;
        }

        return '#' . ($prefix ?: self::currentBillPrefix()) . '-' . str_pad((string) $billNumber, 4, '0', STR_PAD_LEFT);
    }

    private static function nextBillNumberValue(int $clientId): int
    {
        $stmt = Database::connection()->prepare(
            'SELECT bill_number
             FROM orders
             WHERE client_id = ? AND bill_number IS NOT NULL
             ORDER BY bill_number DESC
             LIMIT 1
             FOR UPDATE'
        );
        $stmt->execute([$clientId]);
        return max(1, (int) $stmt->fetchColumn() + 1);
    }

    private static function billNumberForFinalBill(int $clientId, bool $isFinalBill, mixed $existingBillNumber = null): ?int
    {
        if (!$isFinalBill) {
            return null;
        }

        $existing = (int) ($existingBillNumber ?? 0);
        return $existing > 0 ? $existing : self::nextBillNumberValue($clientId);
    }

    public static function create(array $data): int
    {
        $db = Database::connection();
        self::ensureOrderColumns();
        $db->beginTransaction();

        try {
            $itemsTotal = array_reduce($data['items'] ?? [], fn ($sum, $item) => $sum + ((float) $item['price'] * (int) $item['quantity']), 0);
            $total = isset($data['total_amount']) ? (float) $data['total_amount'] : $itemsTotal;
            $discount = self::discountData($data);
            $customer = self::customerData($data);
            $customerId = Customer::findOrCreate($customer['customer_name'], $customer['customer_mobile']);
            $billMarker = self::billMarker(true, $data);
            $clientId = Client::currentId();
            $billNumber = self::billNumberForFinalBill($clientId, true);
            $stmt = $db->prepare(
                'INSERT INTO orders
                 (uuid, client_id, table_id, customer_id, created_by, order_status, total_amount, discount_amount, discount_type, discount_value, discount_label, discount_date, discount_start_time, discount_end_time, discount_is_paused, customer_name, customer_mobile, bill_note, is_kot_only, report_visible, billed_at, bill_number)
                 VALUES (UUID(), ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)'
            );
            $stmt->execute([
                $clientId,
                $data['table_id'] ?? null,
                $customerId,
                $data['created_by'] ?? null,
                $data['order_status'] ?? 'pending',
                $total,
                $discount['discount_amount'],
                $discount['discount_type'],
                $discount['discount_value'],
                $discount['discount_label'],
                $discount['discount_date'],
                $discount['discount_start_time'],
                $discount['discount_end_time'],
                $discount['discount_is_paused'],
                $customer['customer_name'],
                $customer['customer_mobile'],
                $data['bill_note'] ?? null,
                $billMarker['is_kot_only'],
                $billMarker['report_visible'],
                $billMarker['billed_at'],
                $billNumber,
            ]);
            $orderId = (int) $db->lastInsertId();

            foreach ($data['items'] ?? [] as $item) {
                OrderItem::createForOrder($orderId, $item);
            }

            $db->commit();
            return $orderId;
        } catch (\Throwable $error) {
            $db->rollBack();
            throw $error;
        }
    }

    public static function saveTableOrder(array $data): array
    {
        $db = Database::connection();
        self::ensureOrderColumns();
        $db->beginTransaction();

        try {
            $tableId = (int) $data['table_id'];
            $items = $data['items'] ?? [];
            $tableStatus = $data['table_status'] ?? $data['status'] ?? 'ordered';
            $orderStatus = self::uiStatusToOrderStatus($tableStatus);
            $itemsTotal = array_reduce($items, fn ($sum, $item) => $sum + ((float) ($item['price'] ?? 0) * (int) ($item['quantity'] ?? $item['qty'] ?? 1)), 0);
            $total = isset($data['total_amount']) ? (float) $data['total_amount'] : $itemsTotal;
            $discount = self::discountData($data);
            $customer = self::customerData($data);
            $customerId = Customer::findOrCreate($customer['customer_name'], $customer['customer_mobile']);
            $isFinalBill = in_array($tableStatus, ['available', 'complete', 'completed'], true) && count($items) > 0;
            $billMarker = self::billMarker($isFinalBill, $data);
            $clientId = Client::currentId();

            if ($tableStatus === 'available' && count($items) === 0) {
                $active = $db->prepare(
                    "SELECT id, bill_number
                     FROM orders
                     WHERE table_id = ?
                       AND client_id = ?
                       AND order_status IN ('pending', 'confirmed')
                     ORDER BY id DESC
                     LIMIT 1"
                );
                $active->execute([$tableId, Client::currentId()]);
                $activeOrder = $active->fetch();

                if ($activeOrder) {
                    $orderId = (int) $activeOrder['id'];
                    $deleteItems = $db->prepare('DELETE FROM order_items WHERE order_id = ?');
                    $deleteItems->execute([$orderId]);
                    $deleteLogs = $db->prepare('DELETE FROM order_status_logs WHERE order_id = ?');
                    $deleteLogs->execute([$orderId]);
                    $deleteOrder = $db->prepare('DELETE FROM orders WHERE id = ? AND client_id = ?');
                    $deleteOrder->execute([$orderId, Client::currentId()]);
                }

                Table::updateState($tableId, $tableStatus, 0, null);

                $db->commit();

                return [
                    'id' => isset($orderId) ? $orderId : null,
                    'table_id' => $tableId,
                    'order_status' => null,
                    'table_status' => $tableStatus,
                    'total_amount' => 0,
                    'cleared' => true,
                ];
            }

            $find = $db->prepare(
                "SELECT id, bill_number
                 FROM orders
                 WHERE table_id = ?
                   AND client_id = ?
                   AND order_status NOT IN ('cancelled', 'settled')
                 ORDER BY id DESC
                 LIMIT 1"
            );
            $find->execute([$tableId, $clientId]);
            $existing = $find->fetch() ?: null;
            $billNumber = self::billNumberForFinalBill($clientId, $isFinalBill, $existing['bill_number'] ?? null);

            if ($existing) {
                $orderId = (int) $existing['id'];
                $update = $db->prepare(
                    'UPDATE orders
                     SET order_status = ?, total_amount = ?, discount_amount = ?, discount_type = ?, discount_value = ?, discount_label = ?, discount_date = ?, discount_start_time = ?, discount_end_time = ?, discount_is_paused = ?, customer_id = ?, customer_name = ?, customer_mobile = ?, bill_note = ?, is_kot_only = ?, report_visible = ?, billed_at = ?, bill_number = ?, sync_version = sync_version + 1
                     WHERE id = ? AND client_id = ?'
                );
                $update->execute([
                    $orderStatus,
                    $total,
                    $discount['discount_amount'],
                    $discount['discount_type'],
                    $discount['discount_value'],
                    $discount['discount_label'],
                    $discount['discount_date'],
                    $discount['discount_start_time'],
                    $discount['discount_end_time'],
                    $discount['discount_is_paused'],
                    $customerId,
                    $customer['customer_name'],
                    $customer['customer_mobile'],
                    $data['bill_note'] ?? null,
                    $billMarker['is_kot_only'],
                    $billMarker['report_visible'],
                    $billMarker['billed_at'],
                    $billNumber,
                    $orderId,
                    $clientId,
                ]);

                $deleteItems = $db->prepare('DELETE FROM order_items WHERE order_id = ?');
                $deleteItems->execute([$orderId]);
            } else {
                $insert = $db->prepare(
                    'INSERT INTO orders
                     (uuid, client_id, table_id, customer_id, created_by, order_status, total_amount, discount_amount, discount_type, discount_value, discount_label, discount_date, discount_start_time, discount_end_time, discount_is_paused, customer_name, customer_mobile, bill_note, is_kot_only, report_visible, billed_at, bill_number)
                     VALUES (UUID(), ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)'
                );
                $insert->execute([
                    $clientId,
                    $tableId,
                    $customerId,
                    $data['created_by'] ?? null,
                    $orderStatus,
                    $total,
                    $discount['discount_amount'],
                    $discount['discount_type'],
                    $discount['discount_value'],
                    $discount['discount_label'],
                    $discount['discount_date'],
                    $discount['discount_start_time'],
                    $discount['discount_end_time'],
                    $discount['discount_is_paused'],
                    $customer['customer_name'],
                    $customer['customer_mobile'],
                    $data['bill_note'] ?? null,
                    $billMarker['is_kot_only'],
                    $billMarker['report_visible'],
                    $billMarker['billed_at'],
                    $billNumber,
                ]);
                $orderId = (int) $db->lastInsertId();
            }

            foreach ($items as $item) {
                OrderItem::createForOrder($orderId, $item);
            }

            self::logStatus($orderId, $orderStatus, isset($data['changed_by']) ? (int) $data['changed_by'] : null);

            Table::updateState(
                $tableId,
                $tableStatus,
                $tableStatus === 'available' ? 0 : $total,
                $tableStatus === 'available' ? null : ($data['order_timestamp'] ?? (int) round(microtime(true) * 1000))
            );

            $db->commit();

            return [
                'id' => $orderId,
                'bill_number' => $billNumber,
                'table_id' => $tableId,
                'order_status' => $orderStatus,
                'table_status' => $tableStatus,
                'total_amount' => $total,
            ];
        } catch (\Throwable $error) {
            $db->rollBack();
            throw $error;
        }
    }

    public static function activeTableOrders(): array
    {
        self::ensureOrderColumns();

        $db = Database::connection();
        $orders = $db->prepare(
            "SELECT o.*, rt.table_status,
                    COALESCE(c.name, o.customer_name) AS customer_name,
                    COALESCE(c.mobile, o.customer_mobile) AS customer_mobile
             FROM orders o
             LEFT JOIN customers c ON c.id = o.customer_id
             JOIN (
                SELECT o2.table_id, MAX(o2.id) AS id
                FROM orders o2
                WHERE o2.table_id IS NOT NULL
                  AND o2.client_id = ?
                  AND o2.order_status NOT IN ('cancelled', 'settled')
                GROUP BY o2.table_id
             ) latest ON latest.id = o.id
             LEFT JOIN restaurant_tables rt ON rt.id = o.table_id"
        );
        $orders->execute([Client::currentId(), Client::currentId()]);
        $orders = $orders->fetchAll();

        if (!$orders) {
            return [];
        }

        $ids = array_column($orders, 'id');
        $placeholders = implode(',', array_fill(0, count($ids), '?'));
        $stmt = $db->prepare("SELECT * FROM order_items WHERE order_id IN ($placeholders) ORDER BY id");
        $stmt->execute($ids);
        $itemsByOrder = [];

        foreach ($stmt->fetchAll() as $item) {
            $itemsByOrder[(int) $item['order_id']][] = $item;
        }

        foreach ($orders as &$order) {
            $order['items'] = $itemsByOrder[(int) $order['id']] ?? [];
        }

        return $orders;
    }

    public static function updateStatus(int $id, string $status, ?int $changedBy = null): bool
    {
        $db = Database::connection();
        $stmt = $db->prepare('UPDATE orders SET order_status = ?, sync_version = sync_version + 1 WHERE id = ? AND client_id = ?');
        $ok = $stmt->execute([$status, $id, Client::currentId()]);

        if ($ok && $stmt->rowCount() > 0) {
            self::logStatus($id, $status, $changedBy);
        }

        return $ok;
    }

    public static function delete(int $id): bool
    {
        $db = Database::connection();
        self::ensureOrderColumns();
        $db->beginTransaction();

        try {
            $exists = $db->prepare('SELECT id FROM orders WHERE id = ? AND client_id = ? LIMIT 1');
            $exists->execute([$id, Client::currentId()]);
            if (!$exists->fetch()) {
                $db->rollBack();
                return false;
            }

            $deleteItems = $db->prepare('DELETE FROM order_items WHERE order_id = ?');
            $deleteItems->execute([$id]);

            $deleteLogs = $db->prepare('DELETE FROM order_status_logs WHERE order_id = ?');
            $deleteLogs->execute([$id]);

            $deleteOrder = $db->prepare('DELETE FROM orders WHERE id = ? AND client_id = ?');
            $deleteOrder->execute([$id, Client::currentId()]);

            $db->commit();
            return true;
        } catch (\Throwable $error) {
            $db->rollBack();
            throw $error;
        }
    }
}
