<?php

namespace App\Models;

use App\Core\Database;

class Ledger
{
    private static bool $schemaChecked = false;

    public static function ensureSchema(): void
    {
        if (self::$schemaChecked) {
            return;
        }

        Customer::ensureTable();

        Database::connection()->exec(
            "CREATE TABLE IF NOT EXISTS customer_ledger_entries (
                id INT AUTO_INCREMENT PRIMARY KEY,
                uuid VARCHAR(36) UNIQUE,
                client_id INT NOT NULL DEFAULT 1,
                customer_id INT NOT NULL,
                entry_type ENUM('debit','credit') NOT NULL,
                amount DECIMAL(10,2) NOT NULL,
                note VARCHAR(255) DEFAULT NULL,
                payment_mode VARCHAR(20) DEFAULT NULL,
                created_by INT NULL,
                occurred_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                sync_version INT DEFAULT 1,
                CONSTRAINT fk_customer_ledger_customer
                  FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE CASCADE,
                CONSTRAINT fk_customer_ledger_created_by
                  FOREIGN KEY (created_by) REFERENCES users(id) ON DELETE SET NULL
            )"
        );

        self::ensureClientColumn();
        self::ensurePaymentModeColumn();
        self::ensureCustomerLink();
        self::$schemaChecked = true;
    }

    private static function ensureClientColumn(): void
    {
        $db = Database::connection();
        $exists = $db->prepare(
            "SELECT 1
             FROM INFORMATION_SCHEMA.COLUMNS
             WHERE TABLE_SCHEMA = DATABASE()
               AND TABLE_NAME = 'customer_ledger_entries'
               AND COLUMN_NAME = 'client_id'
             LIMIT 1"
        );
        $exists->execute();
        if (!$exists->fetch()) {
            $db->exec('ALTER TABLE customer_ledger_entries ADD COLUMN client_id INT NOT NULL DEFAULT 1 AFTER uuid');
        }
    }

    /**
     * The payment mode a request is asking for, or null when it doesn't mention one.
     *
     * Null matters: every UPDATE writes payment_mode through COALESCE, so a client that
     * doesn't send the field leaves whatever is already stored alone instead of clearing it.
     */
    private static function paymentMode(array $data): ?string
    {
        foreach (['payment_mode', 'paymentMode'] as $key) {
            if (isset($data[$key]) && trim((string) $data[$key]) !== '') {
                return strtolower(trim((string) $data[$key]));
            }
        }
        return null;
    }

    /**
     * How the money moved — cash / upi / card. The till has always recorded this; without
     * a column here it was the one ledger field that stayed behind on the counter.
     */
    private static function ensurePaymentModeColumn(): void
    {
        $db = Database::connection();
        $exists = $db->prepare(
            "SELECT 1
             FROM INFORMATION_SCHEMA.COLUMNS
             WHERE TABLE_SCHEMA = DATABASE()
               AND TABLE_NAME = 'customer_ledger_entries'
               AND COLUMN_NAME = 'payment_mode'
             LIMIT 1"
        );
        $exists->execute();
        if (!$exists->fetch()) {
            $db->exec('ALTER TABLE customer_ledger_entries ADD COLUMN payment_mode VARCHAR(20) DEFAULT NULL AFTER note');
        }
    }

    private static function ensureCustomerLink(): void
    {
        $db = Database::connection();

        // Fix orphaned entries: UPDATE client_id to match customer instead of DELETE
        $orphaned = $db->query(
            'SELECT e.id AS entry_id, e.client_id AS entry_client, c.client_id AS customer_client
             FROM customer_ledger_entries e
             JOIN customers c ON c.id = e.customer_id
             WHERE e.client_id <> c.client_id'
        )->fetchAll();

        foreach ($orphaned as $row) {
            $db->prepare(
                'UPDATE customer_ledger_entries SET client_id = ?, sync_version = sync_version + 1
                 WHERE id = ?'
            )->execute([$row['customer_client'], $row['entry_id']]);
        }

        // Delete entries with no matching customer at all
        $db->exec(
            'DELETE e FROM customer_ledger_entries e
             LEFT JOIN customers c ON c.id = e.customer_id
             WHERE c.id IS NULL'
        );

        $indexExists = $db->query(
            "SELECT 1
             FROM INFORMATION_SCHEMA.STATISTICS
             WHERE TABLE_SCHEMA = DATABASE()
               AND TABLE_NAME = 'customer_ledger_entries'
               AND INDEX_NAME = 'idx_customer_ledger_customer'
             LIMIT 1"
        )->fetchColumn();

        if (!$indexExists) {
            $db->exec('CREATE INDEX idx_customer_ledger_customer ON customer_ledger_entries(customer_id)');
        }

        $constraintExists = $db->query(
            "SELECT 1
             FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
             WHERE TABLE_SCHEMA = DATABASE()
               AND TABLE_NAME = 'customer_ledger_entries'
               AND COLUMN_NAME = 'customer_id'
               AND REFERENCED_TABLE_NAME = 'customers'
               AND REFERENCED_COLUMN_NAME = 'id'
             LIMIT 1"
        )->fetchColumn();

        if (!$constraintExists) {
            $db->exec(
                'ALTER TABLE customer_ledger_entries
                 ADD CONSTRAINT fk_customer_ledger_customer
                 FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE CASCADE'
            );
        }
    }

    public static function summary(): array
    {
        self::ensureSchema();

        $db = Database::connection();
        $customers = $db->query(
            "SELECT c.*,
                    COALESCE(ledger.balance, 0) AS balance,
                    COALESCE(ledger.entry_count, 0) AS entry_count,
                    ledger.last_entry_at
             FROM customers c
             LEFT JOIN (
                SELECT customer_id,
                       COALESCE(SUM(CASE
                           WHEN entry_type = 'debit' THEN amount
                           WHEN entry_type = 'credit' THEN -amount
                           ELSE 0
                       END), 0) AS balance,
                       COUNT(*) AS entry_count,
                       MAX(occurred_at) AS last_entry_at
                FROM customer_ledger_entries
                WHERE client_id = " . (int) Client::currentId() . "
                GROUP BY customer_id
             ) ledger ON ledger.customer_id = c.id
             WHERE c.client_id = " . (int) Client::currentId() . "
               AND COALESCE(ledger.entry_count, 0) > 0
             ORDER BY COALESCE(ledger.last_entry_at, c.updated_at, c.created_at) DESC, c.id DESC"
        )->fetchAll();

        $entries = $db->query(
            "SELECT e.*, c.name AS customer_name, c.mobile AS customer_mobile
             FROM customer_ledger_entries e
             JOIN customers c ON c.id = e.customer_id
             WHERE e.amount > 0
               AND e.client_id = " . (int) Client::currentId() . "
               AND c.client_id = " . (int) Client::currentId() . "
             ORDER BY e.occurred_at DESC, e.id DESC"
        )->fetchAll();

        return [
            'customers' => $customers,
            'entries' => $entries,
        ];
    }

    private static function isOpeningEntry(array $entry): bool
    {
        // Opening entry: amount = 0 AND note = 'Customer created'
        return (float) ($entry['amount'] ?? 0) <= 0
            && (string) ($entry['note'] ?? '') === 'Customer created';
    }

    private static function ensureOpeningEntry(int $customerId, ?int $createdBy = null): void
    {
        $existing = Database::connection()->prepare(
            'SELECT id FROM customer_ledger_entries WHERE customer_id = ? AND client_id = ? LIMIT 1'
        );
        $existing->execute([$customerId, Client::currentId()]);

        if ($existing->fetch()) {
            return;
        }

        $stmt = Database::connection()->prepare(
            'INSERT INTO customer_ledger_entries (uuid, client_id, customer_id, entry_type, amount, note, created_by)
             VALUES (UUID(), ?, ?, ?, 0, ?, ?)'
        );
        $stmt->execute([Client::currentId(), $customerId, 'debit', 'Customer created', $createdBy]);
    }

    private static function parseOccurredAt(array $data): ?string
    {
        if (!isset($data['occurred_at']) || trim((string) $data['occurred_at']) === '') {
            return null;
        }

        $timestamp = strtotime((string) $data['occurred_at']);
        return $timestamp !== false ? date('Y-m-d H:i:s', $timestamp) : null;
    }

    private static function fetchCustomer(int $customerId): array
    {
        $stmt = Database::connection()->prepare('SELECT * FROM customers WHERE id = ? AND client_id = ? LIMIT 1');
        $stmt->execute([$customerId, Client::currentId()]);
        $customer = $stmt->fetch();

        if (!$customer) {
            throw new \RuntimeException('Customer not found.');
        }

        return $customer;
    }

    private static function fetchEntry(int $entryId): array
    {
        $entry = Database::connection()->prepare(
            "SELECT e.*, c.name AS customer_name, c.mobile AS customer_mobile
             FROM customer_ledger_entries e
             JOIN customers c ON c.id = e.customer_id
             WHERE e.id = ?
               AND e.client_id = ?
               AND c.client_id = ?
             LIMIT 1"
        );
        $entry->execute([$entryId, Client::currentId(), Client::currentId()]);
        $row = $entry->fetch();

        if (!$row) {
            throw new \RuntimeException('Ledger entry not found.');
        }

        return $row;
    }

    public static function createCustomer(array $data): array
    {
        self::ensureSchema();

        $name = isset($data['name']) && trim((string) $data['name']) !== '' ? trim((string) $data['name']) : null;
        $mobile = isset($data['mobile']) && trim((string) $data['mobile']) !== '' ? trim((string) $data['mobile']) : null;

        if ($name === null && $mobile === null) {
            throw new \InvalidArgumentException('Customer name or mobile is required.');
        }

        $customerId = Customer::findOrCreate($name, $mobile);

        // findOrCreate only knows about name and mobile, so an address typed at the till was
        // being dropped even though the column is right there.
        if (array_key_exists('address', $data)) {
            $address = trim((string) ($data['address'] ?? ''));
            $setAddress = Database::connection()->prepare(
                'UPDATE customers SET address = ?, sync_version = sync_version + 1 WHERE id = ? AND client_id = ?'
            );
            $setAddress->execute([$address !== '' ? $address : null, $customerId, Client::currentId()]);
        }

        $createdBy = isset($data['created_by']) && (int) $data['created_by'] > 0 ? (int) $data['created_by'] : null;
        self::ensureOpeningEntry($customerId, $createdBy);

        $stmt = Database::connection()->prepare('SELECT * FROM customers WHERE id = ? AND client_id = ? LIMIT 1');
        $stmt->execute([$customerId, Client::currentId()]);

        return $stmt->fetch() ?: [];
    }

    public static function updateCustomer(int $customerId, array $data): array
    {
        self::ensureSchema();

        self::fetchCustomer($customerId);

        $name = isset($data['name']) && trim((string) $data['name']) !== '' ? trim((string) $data['name']) : null;
        $mobile = isset($data['mobile']) && trim((string) $data['mobile']) !== '' ? trim((string) $data['mobile']) : null;

        if ($name === null && $mobile === null) {
            throw new \InvalidArgumentException('Customer name or mobile is required.');
        }

        $normalizedMobile = Customer::normalizeMobile($mobile);
        if ($normalizedMobile !== null) {
            $existing = Database::connection()->prepare('SELECT id FROM customers WHERE normalized_mobile = ? AND client_id = ? AND id <> ? LIMIT 1');
            $existing->execute([$normalizedMobile, Client::currentId(), $customerId]);
            if ($existing->fetch()) {
                throw new \InvalidArgumentException('Mobile number already exists.');
            }
        }

        // COALESCE on address: an update that doesn't mention it must leave it alone rather
        // than wipe it.
        $address = array_key_exists('address', $data) && trim((string) $data['address']) !== ''
            ? trim((string) $data['address'])
            : null;

        $update = Database::connection()->prepare(
            'UPDATE customers
             SET name = ?, mobile = ?, normalized_mobile = ?, address = COALESCE(?, address),
                 sync_version = sync_version + 1
             WHERE id = ? AND client_id = ?'
        );
        $update->execute([$name, $mobile, $normalizedMobile, $address, $customerId, Client::currentId()]);

        return self::fetchCustomer($customerId);
    }

    public static function deleteCustomer(int $customerId): void
    {
        self::ensureSchema();

        self::fetchCustomer($customerId);

        $stmt = Database::connection()->prepare('DELETE FROM customers WHERE id = ? AND client_id = ?');
        $stmt->execute([$customerId, Client::currentId()]);
    }

    public static function createEntry(int $customerId, array $data): array
    {
        self::ensureSchema();

        $type = (string) ($data['type'] ?? $data['entry_type'] ?? 'debit');
        if (!in_array($type, ['debit', 'credit'], true)) {
            throw new \InvalidArgumentException('Entry type must be debit or credit.');
        }

        $amount = (float) ($data['amount'] ?? 0);
        if ($amount <= 0) {
            throw new \InvalidArgumentException('Amount must be greater than zero.');
        }

        $note = isset($data['note']) && trim((string) $data['note']) !== '' ? trim((string) $data['note']) : null;
        $paymentMode = self::paymentMode($data);
        $createdBy = isset($data['created_by']) && (int) $data['created_by'] > 0 ? (int) $data['created_by'] : null;
        $occurredAt = self::parseOccurredAt($data);

        $customer = Database::connection()->prepare('SELECT id FROM customers WHERE id = ? AND client_id = ? LIMIT 1');
        $customer->execute([$customerId, Client::currentId()]);
        if (!$customer->fetch()) {
            throw new \RuntimeException('Customer not found.');
        }

        // Upsert by uuid: a synced device entry carries its uuid. If that uuid already
        // exists for this client we UPDATE it (so an edit propagates) instead of
        // inserting a duplicate (so a sync retry never double-counts the balance).
        $uuid = isset($data['uuid']) && trim((string) $data['uuid']) !== '' ? trim((string) $data['uuid']) : null;
        if ($uuid !== null) {
            $dupe = Database::connection()->prepare('SELECT id FROM customer_ledger_entries WHERE uuid = ? AND client_id = ? LIMIT 1');
            $dupe->execute([$uuid, Client::currentId()]);
            $existing = $dupe->fetch();
            if ($existing) {
                $upd = Database::connection()->prepare(
                    'UPDATE customer_ledger_entries SET entry_type = ?, amount = ?, note = ?, payment_mode = COALESCE(?, payment_mode)'
                    . ($occurredAt ? ', occurred_at = ?' : '')
                    . ' WHERE id = ? AND client_id = ?'
                );
                $params = $occurredAt
                    ? [$type, $amount, $note, $paymentMode, $occurredAt, (int) $existing['id'], Client::currentId()]
                    : [$type, $amount, $note, $paymentMode, (int) $existing['id'], Client::currentId()];
                $upd->execute($params);
                return self::fetchEntry((int) $existing['id']);
            }
        }

        $uuidExpr = $uuid !== null ? '?' : 'UUID()';
        if ($occurredAt) {
            $sql = "INSERT INTO customer_ledger_entries (uuid, client_id, customer_id, entry_type, amount, note, payment_mode, created_by, occurred_at)
                    VALUES ($uuidExpr, ?, ?, ?, ?, ?, ?, ?, ?)";
            $params = [Client::currentId(), $customerId, $type, $amount, $note, $paymentMode, $createdBy, $occurredAt];
        } else {
            $sql = "INSERT INTO customer_ledger_entries (uuid, client_id, customer_id, entry_type, amount, note, payment_mode, created_by)
                    VALUES ($uuidExpr, ?, ?, ?, ?, ?, ?, ?)";
            $params = [Client::currentId(), $customerId, $type, $amount, $note, $paymentMode, $createdBy];
        }
        if ($uuid !== null) {
            array_unshift($params, $uuid);
        }
        $stmt = Database::connection()->prepare($sql);
        $stmt->execute($params);
        $entryId = (int) Database::connection()->lastInsertId();

        return self::fetchEntry($entryId);
    }

    public static function updateEntry(int $entryId, array $data): array
    {
        self::ensureSchema();

        $entry = self::fetchEntry($entryId);
        if (self::isOpeningEntry($entry)) {
            throw new \InvalidArgumentException('Opening ledger entry cannot be edited.');
        }

        $type = (string) ($data['type'] ?? $data['entry_type'] ?? 'debit');
        if (!in_array($type, ['debit', 'credit'], true)) {
            throw new \InvalidArgumentException('Entry type must be debit or credit.');
        }

        $amount = (float) ($data['amount'] ?? 0);
        if ($amount <= 0) {
            throw new \InvalidArgumentException('Amount must be greater than zero.');
        }

        $note = isset($data['note']) && trim((string) $data['note']) !== '' ? trim((string) $data['note']) : null;
        $paymentMode = self::paymentMode($data);
        $occurredAt = self::parseOccurredAt($data) ?: (string) ($entry['occurred_at'] ?? date('Y-m-d H:i:s'));

        $stmt = Database::connection()->prepare(
            'UPDATE customer_ledger_entries
             SET entry_type = ?, amount = ?, note = ?, payment_mode = COALESCE(?, payment_mode),
                 occurred_at = ?, sync_version = sync_version + 1
             WHERE id = ? AND client_id = ?'
        );
        $stmt->execute([$type, $amount, $note, $paymentMode, $occurredAt, $entryId, Client::currentId()]);

        return self::fetchEntry($entryId);
    }

    public static function deleteEntry(int $entryId): void
    {
        self::ensureSchema();

        $entry = self::fetchEntry($entryId);
        if (self::isOpeningEntry($entry)) {
            throw new \RuntimeException('Opening ledger entry cannot be deleted.');
        }

        $stmt = Database::connection()->prepare('DELETE FROM customer_ledger_entries WHERE id = ? AND client_id = ?');
        $stmt->execute([$entryId, Client::currentId()]);
    }
}
