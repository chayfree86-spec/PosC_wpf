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

        $cacheFile = sys_get_temp_dir() . '/pos_schema_checked_' . md5((string) env('DB_DATABASE', 'pos_qr_system')) . '.cache';
        if (file_exists($cacheFile) && (time() - filemtime($cacheFile)) < 86400) {
            self::$orderColumnsChecked = true;
            return;
        }
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
            'sqlite_uuid' => 'VARCHAR(36) DEFAULT NULL',
            'is_parcel_mode' => 'TINYINT(1) NOT NULL DEFAULT 0',
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
        @file_put_contents($cacheFile, 'verified');
        self::$orderColumnsChecked = true;
    }

    private static function backfillReportMarkers(): void
    {
        Database::connection()
            ->prepare(
                "UPDATE orders
                 SET is_kot_only = 0,
                     report_visible = 1,
                     billed_at = COALESCE(updated_at, created_at)
                 WHERE billed_at IS NULL
                   AND report_visible = 0
                   AND is_kot_only = 1
                   AND order_status IN ('completed', 'complete', 'paid', 'settled')
                   AND total_amount > 0"
            )
            ->execute();
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
            'uniq_orders_sqlite_uuid' => 'CREATE UNIQUE INDEX uniq_orders_sqlite_uuid ON orders(sqlite_uuid)',
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
        // Orders carry three statuses and only three: ordered, completed, settled.
        // 'pending' and 'confirmed' used to leak in from here; the column is an enum of
        // the three, so anything else was silently stored as an empty status.
        return match ($status) {
            'complete', 'completed' => 'completed',
            'available', 'settled' => 'settled',
            'cancelled' => 'cancelled',
            default => 'ordered',
        };
    }

    private static function truthy(mixed $value): bool
    {
        if (is_bool($value)) {
            return $value;
        }

        if (is_numeric($value)) {
            return (int) $value === 1;
        }

        return in_array(strtolower(trim((string) $value)), ['1', 'true', 'yes', 'full', 'replace'], true);
    }

    private static function orderItemKey(array $item): string
    {
        $clientItemId = (string) ($item['client_item_id'] ?? $item['clientItemId'] ?? $item['id'] ?? '');
        $itemId = (string) ($item['item_id'] ?? $item['itemId'] ?? '');
        $name = strtolower(trim((string) ($item['item_name'] ?? $item['itemName'] ?? $item['name'] ?? '')));
        $parcel = !empty($item['is_parcel']) || !empty($item['isParcel']) ? 'parcel' : 'dine';

        return ($itemId !== '' ? $itemId : ($clientItemId !== '' ? $clientItemId : $name)) . '|' . $parcel;
    }

    private static function existingOrderItems(int $orderId): array
    {
        $stmt = Database::connection()->prepare(
            'SELECT item_id, client_item_id, item_name, price, quantity, is_parcel, total, discount_amount, discount_type, discount_value, discount_label
             FROM order_items
             WHERE order_id = ?
             ORDER BY id'
        );
        $stmt->execute([$orderId]);

        return array_map(static fn ($item) => [
            'item_id' => $item['item_id'] !== null ? (int) $item['item_id'] : null,
            'client_item_id' => $item['client_item_id'] ?? null,
            'item_name' => $item['item_name'] ?? null,
            'name' => $item['item_name'] ?? null,
            'price' => (float) ($item['price'] ?? 0),
            'quantity' => (int) ($item['quantity'] ?? 1),
            'qty' => (int) ($item['quantity'] ?? 1),
            'is_parcel' => (int) ($item['is_parcel'] ?? 0),
            'total' => (float) ($item['total'] ?? 0),
            'discount_amount' => (float) ($item['discount_amount'] ?? 0),
            'discount_type' => $item['discount_type'] ?? null,
            'discount_value' => (float) ($item['discount_value'] ?? 0),
            'discount_label' => $item['discount_label'] ?? null,
        ], $stmt->fetchAll());
    }

    private static function mergeOrderItems(array $existingItems, array $incomingItems): array
    {
        $merged = [];
        $positions = [];

        foreach ($existingItems as $item) {
            $key = self::orderItemKey($item);
            if ($key === '|dine') {
                $key = 'existing-' . count($merged);
            }
            $positions[$key] = count($merged);
            $merged[] = $item;
        }

        foreach ($incomingItems as $item) {
            $key = self::orderItemKey($item);
            if ($key !== '|dine' && array_key_exists($key, $positions)) {
                $pos = $positions[$key];
                $existingQty = (int) ($merged[$pos]['quantity'] ?? $merged[$pos]['qty'] ?? 1);
                $incomingQty = (int) ($item['quantity'] ?? $item['qty'] ?? 1);
                $merged[$pos] = array_merge($merged[$pos], $item);
                $newQty = $existingQty + $incomingQty;
                $merged[$pos]['quantity'] = $newQty;
                $merged[$pos]['qty'] = $newQty;
                $merged[$pos]['total'] = $newQty * (float) ($merged[$pos]['price'] ?? 0);
                continue;
            }

            $positions[$key ?: ('incoming-' . count($merged))] = count($merged);
            $merged[] = $item;
        }

        return $merged;
    }

    public static function all(?string $startDate = null, ?string $endDate = null, ?string $status = null): array
    {
        self::ensureOrderColumns();

        $sql = 'SELECT o.*, rt.table_number, u.name AS staff_name
                , COALESCE(c.name, o.customer_name) AS customer_name
                , COALESCE(c.mobile, o.customer_mobile) AS customer_mobile
                FROM orders o
                LEFT JOIN customers c ON c.id = o.customer_id
                LEFT JOIN restaurant_tables rt ON rt.id = o.table_id
                LEFT JOIN users u ON u.id = o.created_by
                WHERE o.client_id = ?';
        
        $params = [Client::currentId()];

        if ($startDate) {
            $sql .= ' AND o.billed_at >= ?';
            $params[] = $startDate . ' 00:00:00';
        }
        if ($endDate) {
            $sql .= ' AND o.billed_at <= ?';
            $params[] = $endDate . ' 23:59:59';
        }
        if ($status) {
            $statuses = array_map('trim', explode(',', $status));
            $placeholders = implode(',', array_fill(0, count($statuses), '?'));
            $sql .= " AND o.order_status IN ($placeholders)";
            foreach ($statuses as $s) {
                $params[] = $s;
            }
        }

        // Only include reports visible orders for date filtering
        if ($startDate || $endDate) {
            $sql .= ' AND o.report_visible = 1 AND o.is_kot_only = 0 AND o.billed_at IS NOT NULL';
        }

        $sql .= ' ORDER BY o.billed_at DESC, o.id DESC LIMIT 500';

        $stmt = Database::connection()->prepare($sql);
        $stmt->execute($params);
        $orders = $stmt->fetchAll();

        if (empty($orders)) {
            return [];
        }

        $orderIds = array_column($orders, 'id');
        $placeholders = implode(',', array_fill(0, count($orderIds), '?'));

        $db = Database::connection();
        $itemsStmt = $db->prepare(
            "SELECT oi.* 
             FROM order_items oi
             WHERE oi.order_id IN ($placeholders)
             ORDER BY oi.order_id, oi.id"
        );
        $itemsStmt->execute($orderIds);
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
                 (uuid, client_id, table_id, customer_id, created_by, order_status, total_amount, discount_amount, discount_type, discount_value, discount_label, discount_date, discount_start_time, discount_end_time, discount_is_paused, customer_name, customer_mobile, bill_note, is_kot_only, report_visible, billed_at, bill_number, is_parcel_mode)
                 VALUES (UUID(), ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)'
            );
            $stmt->execute([
                $clientId,
                $data['table_id'] ?? null,
                $customerId,
                $data['created_by'] ?? null,
                $data['order_status'] ?? 'ordered',
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
                !empty($data['is_parcel_mode']) ? 1 : 0,
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
            $tableId = $data['table_id'];
            if (!is_numeric($tableId) || strlen((string) $tableId) > 10) {
                $resolvedTable = Table::findByToken((string) $tableId);
                if ($resolvedTable) {
                    $tableId = (int) $resolvedTable['id'];
                } else {
                    throw new \Exception("Table with token " . $tableId . " not found.");
                }
            } else {
                $tableId = (int) $tableId;
            }
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

            // Table clear (available + empty items): sirf table state reset, orders ko touch nahi karte
            if ($tableStatus === 'available' && count($items) === 0) {
                Table::updateState($tableId, $tableStatus, 0, null);

                $db->commit();

                return [
                    'id' => null,
                    'table_id' => $tableId,
                    'order_status' => null,
                    'table_status' => $tableStatus,
                    'total_amount' => 0,
                    'cleared' => true,
                ];
            }

            if ($tableStatus === 'available') {
                // A settled order is done. Matching it again here — which used to happen
                // whenever this settle push arrived without the order's own sqlite_uuid
                // already on record (a crash or a dropped intermediate sync both do this) —
                // meant a brand new bill on the same table got merged into the OLD closed
                // one instead of becoming its own row, silently erasing the old bill's items.
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
            } else {
                $find = $db->prepare(
                    "SELECT o.id, o.bill_number
                     FROM orders o
                     LEFT JOIN table_client_states ts ON ts.table_id = o.table_id AND ts.client_id = ?
                     WHERE o.table_id = ?
                       AND o.client_id = ?
                       AND o.order_status != 'cancelled'
                       AND COALESCE(ts.table_status, 'available') != 'available'
                     ORDER BY o.id DESC
                     LIMIT 1"
                );
                $find->execute([$clientId, $tableId, $clientId]);
            }
            $existing = $find->fetch() ?: null;

            // The till stamps every order with its SQLite uuid, so when it is sent we can
            // match the exact order instead of guessing "latest order on this table" — which
            // on the settle path could otherwise land on a bill that was already closed.
            $sqliteUuid = trim((string) ($data['sqlite_uuid'] ?? ''));
            if ($sqliteUuid !== '') {
                $byUuid = $db->prepare('SELECT id, bill_number FROM orders WHERE sqlite_uuid = ? AND client_id = ? LIMIT 1');
                $byUuid->execute([$sqliteUuid, $clientId]);
                $existing = $byUuid->fetch() ?: $existing;
            }

            // Bill number, in order of preference: the one already on this order, then the
            // one the till printed, and only then a fresh one. Settling used to arrive here
            // without either, so a table that printed bill #71 was settled as #72 and the
            // paper in the customer's hand no longer matched the report.
            $existingBillNumber = (int) ($existing['bill_number'] ?? 0);
            if ($existingBillNumber <= 0 && !empty($data['bill_number'])) {
                $candidate = (int) $data['bill_number'];
                $taken = $db->prepare('SELECT id FROM orders WHERE client_id = ? AND bill_number = ? LIMIT 1');
                $taken->execute([$clientId, $candidate]);
                $clash = $taken->fetch();
                if (!$clash || (int) $clash['id'] === (int) ($existing['id'] ?? 0)) {
                    $existingBillNumber = $candidate;
                }
            }

            $billNumber = self::billNumberForFinalBill($clientId, $isFinalBill, $existingBillNumber);

            if ($existing) {
                $orderId = (int) $existing['id'];
                $update = $db->prepare(
                    'UPDATE orders
                     SET sqlite_uuid = COALESCE(sqlite_uuid, ?), order_status = ?, total_amount = ?, discount_amount = ?, discount_type = ?, discount_value = ?, discount_label = ?, discount_date = ?, discount_start_time = ?, discount_end_time = ?, discount_is_paused = ?, customer_id = ?, customer_name = ?, customer_mobile = ?, bill_note = ?, is_kot_only = ?, report_visible = ?, billed_at = ?, bill_number = ?, is_parcel_mode = ?, sync_version = sync_version + 1
                     WHERE id = ? AND client_id = ?'
                );
                $update->execute([
                    $sqliteUuid !== '' ? $sqliteUuid : null,
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
                    !empty($data['is_parcel_mode']) ? 1 : 0,
                    $orderId,
                    $clientId,
                ]);

                // Full cart saves replace items. Partial late/offline syncs merge with DB items
                // so a single newly added item cannot wipe the earlier table cart.
                if (count($items) > 0) {
                    $existingItems = self::existingOrderItems($orderId);
                    $isMergeExplicit = self::truthy($data['merge_items'] ?? $data['mergeItems'] ?? false);
                    $replaceItems = !$isMergeExplicit && (self::truthy($data['replace_items'] ?? $data['replaceItems'] ?? $data['full_cart'] ?? $data['fullCart'] ?? false)
                        || count($items) >= count($existingItems));
                    if (!$replaceItems && count($existingItems) > 0) {
                        $items = self::mergeOrderItems($existingItems, $items);
                        $total = array_reduce($items, fn ($sum, $item) => $sum + ((float) ($item['price'] ?? 0) * (int) ($item['quantity'] ?? $item['qty'] ?? 1)), 0);
                        $updateTotal = $db->prepare('UPDATE orders SET total_amount = ?, sync_version = sync_version + 1 WHERE id = ? AND client_id = ?');
                        $updateTotal->execute([$total, $orderId, $clientId]);
                    }
                    $deleteItems = $db->prepare('DELETE FROM order_items WHERE order_id = ?');
                    $deleteItems->execute([$orderId]);
                }
            } else {
                $insert = $db->prepare(
                    'INSERT INTO orders
                     (uuid, sqlite_uuid, client_id, table_id, customer_id, created_by, order_status, total_amount, discount_amount, discount_type, discount_value, discount_label, discount_date, discount_start_time, discount_end_time, discount_is_paused, customer_name, customer_mobile, bill_note, is_kot_only, report_visible, billed_at, bill_number, is_parcel_mode)
                     VALUES (UUID(), ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)'
                );
                $insert->execute([
                    $sqliteUuid !== '' ? $sqliteUuid : null,
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
                    !empty($data['is_parcel_mode']) ? 1 : 0,
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
                LEFT JOIN table_client_states ts2 ON ts2.table_id = o2.table_id AND ts2.client_id = ?
                WHERE o2.table_id IS NOT NULL
                  AND o2.client_id = ?
                  AND o2.order_status != 'cancelled'
                  AND COALESCE(ts2.table_status, 'available') != 'available'
                  -- FINAL BILL FILTER: settled/billed orders NEVER come back to table
                  AND (o2.report_visible = 0 OR o2.report_visible IS NULL)
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

    /**
     * Sync a settled order from local SQLite.
     * Duplicate-safe: checks sqlite_uuid before creating.
     */
    public static function syncFromLocal(array $data): array
    {
        $db = Database::connection();
        self::ensureOrderColumns();

        $sqliteUuid = trim((string) ($data['sqlite_uuid'] ?? ''));
        if ($sqliteUuid === '') {
            return ['error' => 'sqlite_uuid is required', 'synced' => false];
        }

        // Duplicate check: does an order with this sqlite_uuid already exist?
        $exists = $db->prepare(
            'SELECT id FROM orders WHERE sqlite_uuid = ? LIMIT 1'
        );
        $exists->execute([$sqliteUuid]);
        $existing = $exists->fetch();

        if ($existing) {
            return [
                'already_synced' => true,
                'synced' => true,
                'server_id' => (int) $existing['id'],
            ];
        }

        // Create the order
        $db->beginTransaction();
        try {
            $items = $data['items'] ?? [];
            $itemsTotal = array_reduce(
                $items,
                fn ($sum, $item) => $sum + ((float) ($item['price'] ?? 0) * (int) ($item['quantity'] ?? $item['qty'] ?? 1)),
                0
            );
            $total = isset($data['total_amount']) ? (float) $data['total_amount'] : $itemsTotal;
            $discount = self::discountData($data);
            $customer = self::customerData($data);
            $customerId = Customer::findOrCreate($customer['customer_name'], $customer['customer_mobile']);
            $clientId = Client::currentId();

            // Use provided bill_number or generate next
            $billNumber = !empty($data['bill_number']) ? (int) $data['bill_number'] : self::nextBillNumberValue($clientId);

            // Check bill_number conflict
            $billCheck = $db->prepare(
                'SELECT id FROM orders WHERE client_id = ? AND bill_number = ? LIMIT 1'
            );
            $billCheck->execute([$clientId, $billNumber]);
            if ($billCheck->fetch()) {
                // Bill number exists but UUID is different → assign next available
                $billNumber = self::nextBillNumberValue($clientId);
            }

            $billedAt = self::normalizeDateTime($data['billed_at'] ?? $data['billedAt'] ?? null);
            $createdAt = self::normalizeDateTime($data['created_at'] ?? $data['createdAt'] ?? null);
            $updatedAt = self::normalizeDateTime($data['updated_at'] ?? $data['updatedAt'] ?? null);
            $tz = new \DateTimeZone((string) env('APP_TIMEZONE', 'Asia/Kolkata'));
            $nowLocal = (new \DateTimeImmutable('now', $tz))->format('Y-m-d H:i:s');
             $stmt = $db->prepare(
                'INSERT INTO orders
                 (uuid, client_id, table_id, customer_id, created_by, order_status, total_amount,
                  discount_amount, discount_type, discount_value, discount_label,
                  discount_date, discount_start_time, discount_end_time, discount_is_paused,
                  customer_name, customer_mobile, bill_note,
                  is_kot_only, report_visible, billed_at, bill_number, sqlite_uuid, created_at, updated_at, is_parcel_mode)
                 VALUES (UUID(), ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0, 1, ?, ?, ?, ?, ?, ?)'
            );
            $stmt->execute([
                $clientId,
                $data['table_id'] ?? null,
                $customerId,
                $data['created_by'] ?? null,
                $data['order_status'] ?? 'settled',
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
                $billedAt ?? $nowLocal,
                $billNumber,
                $sqliteUuid,
                $createdAt ?? $nowLocal,
                $updatedAt ?? $nowLocal,
                !empty($data['is_parcel_mode']) ? 1 : 0,
            ]);
            $orderId = (int) $db->lastInsertId();

            // Insert items
            foreach ($items as $item) {
                OrderItem::createForOrder($orderId, $item);
            }

            self::logStatus($orderId, $data['order_status'] ?? 'settled', isset($data['created_by']) ? (int) $data['created_by'] : null);

            $db->commit();

            return [
                'synced' => true,
                'already_synced' => false,
                'server_id' => $orderId,
                'bill_number' => $billNumber,
            ];
        } catch (\Throwable $error) {
            $db->rollBack();
            throw $error;
        }
    }

    /**
     * Sync a batch of settled orders from local SQLite.
     * Processes all orders inside a single transaction with prepared statements for optimal performance.
     */
    public static function syncBatchFromLocal(array $orders): array
    {
        $db = Database::connection();
        self::ensureOrderColumns();

        $results = [];
        if (empty($orders)) {
            return $results;
        }

        // 1. Batch fetch to check duplicate UUIDs in one query
        $uuids = [];
        foreach ($orders as $data) {
            $sqliteUuid = trim((string) ($data['sqlite_uuid'] ?? ''));
            if ($sqliteUuid !== '') {
                $uuids[] = $sqliteUuid;
            }
        }

        $existingOrdersMap = [];
        if (!empty($uuids)) {
            $placeholders = implode(',', array_fill(0, count($uuids), '?'));
            $duplicateCheck = $db->prepare("SELECT sqlite_uuid, id FROM orders WHERE sqlite_uuid IN ($placeholders)");
            $duplicateCheck->execute($uuids);
            foreach ($duplicateCheck->fetchAll() as $row) {
                $existingOrdersMap[$row['sqlite_uuid']] = (int) $row['id'];
            }
        }

        // 2. Prefetch existing bill numbers to prevent SELECT in loop
        $clientId = Client::currentId();
        $existingBillsStmt = $db->prepare('SELECT bill_number FROM orders WHERE client_id = ? AND bill_number IS NOT NULL');
        $existingBillsStmt->execute([$clientId]);
        $existingBillsSet = array_flip($existingBillsStmt->fetchAll(\PDO::FETCH_COLUMN));

        $maxBillNumber = count($existingBillsSet) > 0 ? max(array_keys($existingBillsSet)) : 0;

        $db->beginTransaction();

        try {
            $insertOrder = $db->prepare(
                'INSERT INTO orders
                 (uuid, client_id, table_id, customer_id, created_by, order_status, total_amount,
                  discount_amount, discount_type, discount_value, discount_label,
                  discount_date, discount_start_time, discount_end_time, discount_is_paused,
                  customer_name, customer_mobile, bill_note,
                  is_kot_only, report_visible, billed_at, bill_number, sqlite_uuid, created_at, updated_at, is_parcel_mode)
                 VALUES (UUID(), ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0, 1, ?, ?, ?, ?, ?, ?)'
            );

            $insertOrderItem = $db->prepare(
                'INSERT INTO order_items (uuid, order_id, item_id, client_item_id, item_name, price, quantity, is_parcel, total, discount_amount, discount_type, discount_value, discount_label)
                 VALUES (UUID(), ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)'
            );

            $logStatusStmt = $db->prepare('INSERT INTO order_status_logs (order_id, status, changed_by) VALUES (?, ?, ?)');

            $tz = new \DateTimeZone((string) env('APP_TIMEZONE', 'Asia/Kolkata'));
            
            // Memory cache for customer IDs to prevent duplicate database lookups
            $customerCache = [];

            foreach ($orders as $data) {
                $sqliteUuid = trim((string) ($data['sqlite_uuid'] ?? ''));
                if ($sqliteUuid === '') {
                    $results[] = [
                        'sqlite_uuid' => '',
                        'success' => false,
                        'error' => 'sqlite_uuid is required'
                    ];
                    continue;
                }

                // Check duplicate from our pre-fetched map
                if (isset($existingOrdersMap[$sqliteUuid])) {
                    $results[] = [
                        'sqlite_uuid' => $sqliteUuid,
                        'success' => true,
                        'already_synced' => true,
                        'server_id' => $existingOrdersMap[$sqliteUuid]
                    ];
                    continue;
                }

                try {
                    // Calculate totals
                    $items = $data['items'] ?? [];
                    $itemsTotal = array_reduce(
                        $items,
                        fn ($sum, $item) => $sum + ((float) ($item['price'] ?? 0) * (int) ($item['quantity'] ?? $item['qty'] ?? 1)),
                        0
                    );
                    $total = isset($data['total_amount']) ? (float) $data['total_amount'] : $itemsTotal;
                    $discount = self::discountData($data);
                    
                    // Customer caching
                    $customerName = isset($data['customer_name']) && trim($data['customer_name']) !== '' ? trim($data['customer_name']) : null;
                    $customerMobile = isset($data['customer_mobile']) && trim($data['customer_mobile']) !== '' ? trim($data['customer_mobile']) : null;
                    $cacheKey = ($customerName ?? '') . '|' . ($customerMobile ?? '');
                    
                    if ($customerName === null && $customerMobile === null) {
                        $customerId = null;
                    } elseif (isset($customerCache[$cacheKey])) {
                        $customerId = $customerCache[$cacheKey];
                    } else {
                        $customerId = Customer::findOrCreate($customerName, $customerMobile);
                        $customerCache[$cacheKey] = $customerId;
                    }

                    // Bill number selection and conflict resolution
                    $billNumber = !empty($data['bill_number']) ? (int) $data['bill_number'] : 0;
                    
                    if ($billNumber <= 0 || isset($existingBillsSet[$billNumber])) {
                        $maxBillNumber = max($maxBillNumber, 1) + 1;
                        $billNumber = $maxBillNumber;
                        $existingBillsSet[$billNumber] = true;
                    } else {
                        $existingBillsSet[$billNumber] = true;
                        $maxBillNumber = max($maxBillNumber, $billNumber);
                    }

                    $billedAt = self::normalizeDateTime($data['billed_at'] ?? $data['billedAt'] ?? null);
                    $createdAt = self::normalizeDateTime($data['created_at'] ?? $data['createdAt'] ?? null);
                    $updatedAt = self::normalizeDateTime($data['updated_at'] ?? $data['updatedAt'] ?? null);
                    $nowLocal = (new \DateTimeImmutable('now', $tz))->format('Y-m-d H:i:s');

                    // Execute order insert
                    $insertOrder->execute([
                        $clientId,
                        $data['table_id'] ?? null,
                        $customerId,
                        $data['created_by'] ?? null,
                        $data['order_status'] ?? 'settled',
                        $total,
                        $discount['discount_amount'],
                        $discount['discount_type'],
                        $discount['discount_value'],
                        $discount['discount_label'],
                        $discount['discount_date'],
                        $discount['discount_start_time'],
                        $discount['discount_end_time'],
                        $discount['discount_is_paused'],
                        $customerName,
                        $customerMobile,
                        $data['bill_note'] ?? null,
                        $billedAt ?? $nowLocal,
                        $billNumber,
                        $sqliteUuid,
                        $createdAt ?? $nowLocal,
                        $updatedAt ?? $nowLocal,
                        !empty($data['is_parcel_mode']) ? 1 : 0,
                    ]);

                    $orderId = (int) $db->lastInsertId();

                    // Insert items
                    foreach ($items as $item) {
                        $price = (float) ($item['price'] ?? 0);
                        $quantity = (int) ($item['quantity'] ?? $item['qty'] ?? 1);
                        $clientItemId = (string) ($item['client_item_id'] ?? $item['id'] ?? '');
                        $itemId = $item['item_id'] ?? null;

                        if ($itemId === null && preg_match('/^\d+/', $clientItemId, $matches)) {
                            $itemId = (int) $matches[0];
                        }

                        $discAmount = (float) ($item['discount_amount'] ?? $item['discountAmount'] ?? 0);
                        $discType = $item['discount_type'] ?? $item['discountType'] ?? null;
                        $discValue = (float) ($item['discount_value'] ?? $item['discountValue'] ?? 0);
                        $discLabel = $item['discount_label'] ?? $item['discountLabel'] ?? null;

                        $subtotal = $price * $quantity;
                        $finalTotal = max(0, $subtotal - $discAmount);

                        $insertOrderItem->execute([
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
                    }

                    // Directly insert status log
                    $logStatusStmt->execute([$orderId, $data['order_status'] ?? 'settled', isset($data['created_by']) ? (int) $data['created_by'] : null]);

                    $results[] = [
                        'sqlite_uuid' => $sqliteUuid,
                        'success' => true,
                        'already_synced' => false,
                        'server_id' => $orderId,
                        'bill_number' => $billNumber
                    ];
                } catch (\Throwable $itemError) {
                    $results[] = [
                        'sqlite_uuid' => $sqliteUuid,
                        'success' => false,
                        'error' => $itemError->getMessage()
                    ];
                }
            }

            $db->commit();
            return $results;
        } catch (\Throwable $error) {
            $db->rollBack();
            throw $error;
        }
    }
}

