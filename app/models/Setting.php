<?php

namespace App\Models;

use App\Core\Database;

class Setting
{
    private const DEFAULT_SHORTCUTS = [
        ['id' => 'switch_quick_bill', 'label' => 'Open Quick Bill', 'key' => 'F1', 'group' => 'Order Panel', 'icon' => 'Zap'],
        ['id' => 'toggle_search', 'label' => 'Toggle Search / Voice', 'key' => 'F2', 'group' => 'Order Panel', 'icon' => 'Search'],
        ['id' => 'voice_toggle', 'label' => 'Voice Search Toggle', 'key' => 'Shift', 'group' => 'Order Panel', 'icon' => 'Mic'],
        ['id' => 'note', 'label' => 'Add Bill Note', 'key' => 'F3', 'group' => 'Order Panel', 'icon' => 'FileText'],
        ['id' => 'bill_print', 'label' => 'Print Bill', 'key' => 'F4', 'group' => 'Billing & Payments', 'icon' => 'Printer'],
        ['id' => 'switch_table', 'label' => 'Open Table Order', 'key' => 'F5', 'group' => 'Order Panel', 'icon' => 'UtensilsCrossed'],
        ['id' => 'customer', 'label' => 'Add Customer', 'key' => 'Alt+U', 'group' => 'Order Panel', 'icon' => 'User'],
        ['id' => 'toggle_parcel', 'label' => 'Toggle Parcel Mode', 'key' => 'F7', 'group' => 'Order Panel', 'icon' => 'Layers'],
        ['id' => 'save_kot', 'label' => 'Save KOT', 'key' => 'Alt+K', 'group' => 'Order Panel', 'icon' => 'Zap'],
        ['id' => 'print_kot', 'label' => 'Print KOT', 'key' => 'F8', 'group' => 'Table Management', 'icon' => 'Printer'],
        ['id' => 'clear_table', 'label' => 'Clear Table', 'key' => '', 'group' => 'Table Management', 'icon' => 'Trash2'],
        ['id' => 'settle_bill', 'label' => 'Settle Bill / Payment', 'key' => 'F10', 'group' => 'Billing & Payments', 'icon' => 'Wallet'],
        ['id' => 'transfer', 'label' => 'Transfer Table', 'key' => 'F11', 'group' => 'Table Management', 'icon' => 'ArrowRight'],
        ['id' => 'switch_qr_order', 'label' => 'Open QR Order', 'key' => 'F12', 'group' => 'Order Panel', 'icon' => 'QrCode'],
        ['id' => 'add_extra', 'label' => 'Add Extra Item', 'key' => 'Insert', 'group' => 'Order Panel', 'icon' => 'Plus'],
        ['id' => 'clear_selection', 'label' => 'Clear Selection / Close', 'key' => 'ESC', 'group' => 'Order Panel', 'icon' => 'X'],
        ['id' => 'hold_bill', 'label' => 'Hold Bill', 'key' => 'Ctrl+H', 'group' => 'Billing & Payments', 'icon' => 'Minus'],
        ['id' => 'discount', 'label' => 'Add Discount', 'key' => 'Alt+G', 'group' => 'Billing & Payments', 'icon' => 'Percent'],
        ['id' => 'draft_bill', 'label' => 'Draft Bill', 'key' => 'Alt+D', 'group' => 'Billing & Payments', 'icon' => 'FileText'],
        ['id' => 'mark_occupied', 'label' => 'Mark as Occupied', 'key' => 'Alt+1', 'group' => 'Table Management', 'icon' => 'Clock'],
        ['id' => 'mark_ordered', 'label' => 'Mark as Ordered', 'key' => 'Alt+2', 'group' => 'Table Management', 'icon' => 'Clock'],
        ['id' => 'mark_complete', 'label' => 'Mark as Complete', 'key' => 'Alt+3', 'group' => 'Table Management', 'icon' => 'Clock'],
        ['id' => 'change_table', 'label' => 'Change Table', 'key' => 'Alt+C', 'group' => 'Table Management', 'icon' => 'Edit2'],
        ['id' => 'merge_table', 'label' => 'Merge Table', 'key' => 'Alt+M', 'group' => 'Table Management', 'icon' => 'Layers'],
        ['id' => 'split_table', 'label' => 'Split Table', 'key' => 'Alt+S', 'group' => 'Table Management', 'icon' => 'PlusCircle'],
        ['id' => 'open_cash', 'label' => 'Open Cash Drawer', 'key' => '', 'group' => 'Billing & Payments', 'icon' => 'Wallet'],
    ];

    private const SHORTCUT_ALIASES = [
        'settle_table' => 'settle_bill',
        'table_settle' => 'settle_bill',
        'settle_table_bill' => 'settle_bill',
        'table_settle_bill' => 'settle_bill',
    ];

    private static function ensureTable(): void
    {
        $db = Database::connection();
        $db->exec(
            'CREATE TABLE IF NOT EXISTS app_settings (
                client_id INT NOT NULL DEFAULT 1,
                `key` VARCHAR(100) NOT NULL,
                `value` JSON NULL,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                PRIMARY KEY (client_id, `key`)
            )'
        );

        $hasClient = $db->prepare(
            "SELECT 1
             FROM INFORMATION_SCHEMA.COLUMNS
             WHERE TABLE_SCHEMA = DATABASE()
               AND TABLE_NAME = 'app_settings'
               AND COLUMN_NAME = 'client_id'
             LIMIT 1"
        );
        $hasClient->execute();

        if ($hasClient->fetch()) {
            return;
        }

        Client::ensureSchema();
        $defaultClientId = (int) ($db->query("SELECT id FROM clients WHERE slug = 'daalroti' LIMIT 1")->fetchColumn() ?: 1);
        $db->exec(
            'CREATE TABLE IF NOT EXISTS app_settings_client_migration (
                client_id INT NOT NULL DEFAULT 1,
                `key` VARCHAR(100) NOT NULL,
                `value` JSON NULL,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                PRIMARY KEY (client_id, `key`)
            )'
        );
        $db->exec(
            'INSERT INTO app_settings_client_migration (client_id, `key`, `value`, updated_at)
             SELECT ' . $defaultClientId . ', `key`, `value`, updated_at FROM app_settings
             ON DUPLICATE KEY UPDATE `value` = VALUES(`value`), updated_at = VALUES(updated_at)'
        );
        $db->exec('RENAME TABLE app_settings TO app_settings_old_client_migration_runtime, app_settings_client_migration TO app_settings');
        $db->exec('DROP TABLE app_settings_old_client_migration_runtime');
    }

    private static function seedDefaults(): void
    {
        self::ensureTable();
        $stmt = Database::connection()->prepare('SELECT `value` FROM app_settings WHERE client_id = ? AND `key` = ? LIMIT 1');
        $stmt->execute([Client::currentId(), 'shortcuts']);
        $row = $stmt->fetch();

        if (!$row) {
            self::put('shortcuts', self::DEFAULT_SHORTCUTS);
            return;
        }

        $saved = json_decode($row['value'] ?? 'null', true);
        if (!is_array($saved)) {
            self::put('shortcuts', self::DEFAULT_SHORTCUTS);
            return;
        }

        $savedById = self::shortcutMap($saved);

        $merged = [];
        $changed = false;
        foreach (self::DEFAULT_SHORTCUTS as $defaultShortcut) {
            $id = $defaultShortcut['id'];
            if (isset($savedById[$id])) {
                $mergedShortcut = array_merge($defaultShortcut, $savedById[$id]);
            } else {
                $mergedShortcut = $defaultShortcut;
                $changed = true;
            }
            if ($id === 'clear_table' && ($mergedShortcut['key'] ?? '') !== '') {
                $mergedShortcut['key'] = '';
                $changed = true;
            }
            $shortcutKey = strtoupper(str_replace(' ', '', $mergedShortcut['key'] ?? ''));
            if ($id === 'customer' && ($shortcutKey === '' || $shortcutKey === 'F6')) {
                $mergedShortcut['key'] = 'Alt+U';
                $changed = true;
            }
            $merged[] = $mergedShortcut;
        }

        $defaultIds = array_column(self::DEFAULT_SHORTCUTS, 'id');
        foreach ($saved as $shortcut) {
            if (!is_array($shortcut) || !isset($shortcut['id'])) {
                continue;
            }

            $id = self::canonicalShortcutId((string) $shortcut['id']);
            if ($id !== $shortcut['id']) {
                $changed = true;
                continue;
            }

            if (!in_array($id, $defaultIds, true)) {
                $merged[] = $shortcut;
            }
        }

        if ($changed || count($merged) !== count($saved)) {
            self::put('shortcuts', $merged);
        }
    }

    public static function all(): array
    {
        self::seedDefaults();
        $rows = Database::connection()
            ->prepare('SELECT `key`, `value`, updated_at FROM app_settings WHERE client_id = ? ORDER BY `key`');
        $rows->execute([Client::currentId()]);
        $currentRows = $rows->fetchAll();

        $settings = [];
        foreach ($currentRows as $row) {
            $settings[$row['key']] = [
                'key' => $row['key'],
                'value' => json_decode($row['value'] ?? 'null', true),
                'updated_at' => $row['updated_at'],
            ];
        }

        // Fallback to client_id = 1 for any settings not defined for the current client
        if (Client::currentId() !== 1) {
            $rows = Database::connection()
                ->prepare('SELECT `key`, `value`, updated_at FROM app_settings WHERE client_id = 1 ORDER BY `key`');
            $rows->execute();
            $client1Rows = $rows->fetchAll();
            foreach ($client1Rows as $row) {
                if (!isset($settings[$row['key']])) {
                    $settings[$row['key']] = [
                        'key' => $row['key'],
                        'value' => json_decode($row['value'] ?? 'null', true),
                        'updated_at' => $row['updated_at'],
                    ];
                }
            }
        }

        return array_values($settings);
    }

    public static function get(string $key, mixed $default = null): mixed
    {
        self::seedDefaults();
        $stmt = Database::connection()->prepare('SELECT `value` FROM app_settings WHERE client_id = ? AND `key` = ? LIMIT 1');
        $stmt->execute([Client::currentId(), $key]);
        $row = $stmt->fetch();

        if (!$row && Client::currentId() !== 1) {
            $stmt = Database::connection()->prepare('SELECT `value` FROM app_settings WHERE client_id = 1 AND `key` = ? LIMIT 1');
            $stmt->execute([$key]);
            $row = $stmt->fetch();
        }

        if (!$row) {
            return $default;
        }

        return json_decode($row['value'] ?? 'null', true) ?? $default;
    }

    public static function put(string $key, mixed $value): array
    {
        self::ensureTable();
        if ($key === 'shortcuts' && is_array($value)) {
            $value = self::normalizeShortcuts($value);
        }

        $stmt = Database::connection()->prepare(
            'INSERT INTO app_settings (client_id, `key`, `value`) VALUES (?, ?, ?)
             ON DUPLICATE KEY UPDATE `value` = VALUES(`value`), updated_at = CURRENT_TIMESTAMP'
        );
        $stmt->execute([Client::currentId(), $key, json_encode($value, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES)]);

        return [
            'key' => $key,
            'value' => $value,
        ];
    }

    private static function canonicalShortcutId(string $id): string
    {
        return self::SHORTCUT_ALIASES[$id] ?? $id;
    }

    private static function shortcutMap(array $shortcuts): array
    {
        $map = [];
        foreach ($shortcuts as $shortcut) {
            if (!is_array($shortcut) || !isset($shortcut['id'])) {
                continue;
            }

            $id = self::canonicalShortcutId((string) $shortcut['id']);
            $shortcut['id'] = $id;
            $map[$id] = array_merge($map[$id] ?? [], $shortcut);
        }

        return $map;
    }

    private static function normalizeShortcuts(array $shortcuts): array
    {
        $savedById = self::shortcutMap($shortcuts);
        $defaultIds = array_column(self::DEFAULT_SHORTCUTS, 'id');
        $normalized = [];

        foreach (self::DEFAULT_SHORTCUTS as $defaultShortcut) {
            $id = $defaultShortcut['id'];
            $shortcut = array_merge($defaultShortcut, $savedById[$id] ?? []);

            if ($id === 'clear_table') {
                $shortcut['key'] = '';
            }

            $shortcutKey = strtoupper(str_replace(' ', '', $shortcut['key'] ?? ''));
            if ($id === 'customer' && ($shortcutKey === '' || $shortcutKey === 'F6')) {
                $shortcut['key'] = 'Alt+U';
            }

            $normalized[] = $shortcut;
        }

        foreach ($shortcuts as $shortcut) {
            if (!is_array($shortcut) || !isset($shortcut['id'])) {
                continue;
            }

            $id = self::canonicalShortcutId((string) $shortcut['id']);
            if (!in_array($id, $defaultIds, true)) {
                $shortcut['id'] = $id;
                $normalized[] = $shortcut;
            }
        }

        return $normalized;
    }
}
