<?php

namespace App\Models;

use App\Core\Database;
use App\Models\Client;
use App\Services\JWTService;

class MenuItem
{
    private static bool $preferenceTableChecked = false;

    private static function ensurePreferenceTable(): void
    {
        if (self::$preferenceTableChecked) {
            return;
        }

        Database::connection()->exec(
            "CREATE TABLE IF NOT EXISTS menu_item_client_preferences (
                id INT AUTO_INCREMENT PRIMARY KEY,
                client_id INT NOT NULL,
                menu_item_id INT NOT NULL,
                is_favorite TINYINT(1) NOT NULL DEFAULT 0,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                UNIQUE KEY uq_menu_item_client_preference (client_id, menu_item_id),
                KEY idx_menu_item_client_preference_item (menu_item_id)
            )"
        );
        self::$preferenceTableChecked = true;
    }

    private static function currentUserId(): int
    {
        $header = $_SERVER['HTTP_AUTHORIZATION'] ?? '';
        if (!preg_match('/Bearer\s+(.+)/', $header, $matches)) {
            return 0;
        }

        $payload = JWTService::decode($matches[1]);
        return !empty($payload['sub']) ? (int) $payload['sub'] : 0;
    }

    public static function all(): array
    {
        self::ensurePreferenceTable();
        $sql = 'SELECT mi.*, c.name AS category_name, c.image AS category_image,
                       sc.name AS sub_category_name, sc.image AS sub_category_image,
                       COALESCE(sales.selling_qty, 0) AS selling_qty,
                       COALESCE(pref.is_favorite, 0) AS is_favorite,
                       COALESCE(pref.is_favorite, 0) AS isFavorite
                FROM menu_items mi
                JOIN categories c ON c.id = mi.category_id
                LEFT JOIN categories sc ON sc.id = mi.sub_category_id
                LEFT JOIN menu_item_client_preferences pref
                  ON pref.menu_item_id = mi.id AND pref.client_id = ?
                LEFT JOIN (
                    SELECT oi.item_id, SUM(oi.quantity) AS selling_qty
                    FROM order_items oi
                    JOIN orders o ON o.id = oi.order_id
                    WHERE o.client_id = ?
                      AND o.created_by = ?
                      AND o.report_visible = 1
                      AND o.is_kot_only = 0
                      AND o.billed_at IS NOT NULL
                    GROUP BY oi.item_id
                ) sales ON sales.item_id = mi.id
                ORDER BY c.sort_order, c.name, mi.name';

        $stmt = Database::connection()->prepare($sql);
        $stmt->execute([Client::currentId(), Client::currentId(), self::currentUserId()]);
        $rows = $stmt->fetchAll();
        foreach ($rows as &$row) {
            $row = self::populateCode($row);
        }
        return $rows;
    }

    public static function find(int $id): ?array
    {
        self::ensurePreferenceTable();
        $stmt = Database::connection()->prepare(
            'SELECT mi.*, c.name AS category_name, c.image AS category_image,
                    sc.name AS sub_category_name, sc.image AS sub_category_image,
                    COALESCE(sales.selling_qty, 0) AS selling_qty,
                    COALESCE(pref.is_favorite, 0) AS is_favorite,
                    COALESCE(pref.is_favorite, 0) AS isFavorite
             FROM menu_items mi
             JOIN categories c ON c.id = mi.category_id
             LEFT JOIN categories sc ON sc.id = mi.sub_category_id
             LEFT JOIN menu_item_client_preferences pref
               ON pref.menu_item_id = mi.id AND pref.client_id = ?
             LEFT JOIN (
                 SELECT oi.item_id, SUM(oi.quantity) AS selling_qty
                 FROM order_items oi
                 JOIN orders o ON o.id = oi.order_id
                 WHERE o.client_id = ?
                   AND o.created_by = ?
                   AND o.report_visible = 1
                   AND o.is_kot_only = 0
                   AND o.billed_at IS NOT NULL
                 GROUP BY oi.item_id
             ) sales ON sales.item_id = mi.id
             WHERE mi.id = ?'
        );
        $stmt->execute([Client::currentId(), Client::currentId(), self::currentUserId(), $id]);
        $item = $stmt->fetch();
        if ($item) {
            $item = self::populateCode($item);
        }

        return $item ?: null;
    }

    public static function create(array $data): int
    {
        $isFavorite = array_key_exists('isFavorite', $data) || array_key_exists('is_favorite', $data)
            ? filter_var($data['isFavorite'] ?? $data['is_favorite'], FILTER_VALIDATE_BOOLEAN)
            : false;

        $desc = $data['description'] ?? null;
        if (array_key_exists('code', $data)) {
            $desc = self::mergeCodeIntoDescription($desc, $data['code']);
        }

        $stmt = Database::connection()->prepare(
            'INSERT INTO menu_items (uuid, name, category_id, sub_category_id, price, image, is_veg, is_available, description)
             VALUES (UUID(), ?, ?, ?, ?, ?, ?, ?, ?)'
        );
        $stmt->execute([
            $data['name'],
            $data['category_id'],
            $data['sub_category_id'] ?? null,
            $data['price'],
            $data['image'] ?? null,
            $data['is_veg'] ?? 1,
            $data['is_available'] ?? 1,
            self::clientNeutralDescription($desc),
        ]);

        $id = (int) Database::connection()->lastInsertId();
        if ($isFavorite) {
            self::setFavorite($id, true);
        }

        return $id;
    }

    public static function update(int $id, array $data): bool
    {
        if (array_key_exists('isFavorite', $data) || array_key_exists('is_favorite', $data)) {
            self::setFavorite($id, filter_var($data['isFavorite'] ?? $data['is_favorite'], FILTER_VALIDATE_BOOLEAN));
        }

        $existing = self::find($id);
        $desc = $existing ? ($existing['description'] ?? null) : null;

        if (array_key_exists('code', $data)) {
            $desc = self::mergeCodeIntoDescription($desc, $data['code']);
        } elseif (array_key_exists('description', $data)) {
            $desc = $data['description'];
        }

        $stmt = Database::connection()->prepare(
            'UPDATE menu_items
             SET name = ?, category_id = ?, sub_category_id = ?, price = ?, image = ?, is_veg = ?, is_available = ?, description = ?, sync_version = sync_version + 1
             WHERE id = ?'
        );

        return $stmt->execute([
            $data['name'],
            $data['category_id'],
            $data['sub_category_id'] ?? null,
            $data['price'],
            $data['image'] ?? null,
            $data['is_veg'] ?? 1,
            $data['is_available'] ?? 1,
            self::clientNeutralDescription($desc),
            $id,
        ]);
    }

    public static function delete(int $id): bool
    {
        self::ensurePreferenceTable();
        $deletePreferences = Database::connection()->prepare('DELETE FROM menu_item_client_preferences WHERE menu_item_id = ?');
        $deletePreferences->execute([$id]);

        $stmt = Database::connection()->prepare('DELETE FROM menu_items WHERE id = ?');
        return $stmt->execute([$id]);
    }

    private static function setFavorite(int $id, bool $isFavorite): bool
    {
        self::ensurePreferenceTable();
        $stmt = Database::connection()->prepare(
            'INSERT INTO menu_item_client_preferences (client_id, menu_item_id, is_favorite)
             VALUES (?, ?, ?)
             ON DUPLICATE KEY UPDATE is_favorite = VALUES(is_favorite), updated_at = CURRENT_TIMESTAMP'
        );
        return $stmt->execute([Client::currentId(), $id, $isFavorite ? 1 : 0]);
    }

    private static function clientNeutralDescription(mixed $description): mixed
    {
        if (!is_string($description) || trim($description) === '') {
            return $description;
        }

        $decoded = json_decode($description, true);
        if (!is_array($decoded) || !array_key_exists('isFavorite', $decoded)) {
            return $description;
        }

        unset($decoded['isFavorite']);
        return json_encode($decoded, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
    }

    private static function populateCode(array $row): array
    {
        $row['code'] = null;
        if (!empty($row['description'])) {
            $decoded = json_decode($row['description'], true);
            if (is_array($decoded) && isset($decoded['code'])) {
                $row['code'] = $decoded['code'];
            }
        }
        return $row;
    }

    private static function mergeCodeIntoDescription(mixed $description, ?string $code): ?string
    {
        $descArray = [];
        if (is_string($description) && trim($description) !== '') {
            $decoded = json_decode($description, true);
            if (is_array($decoded)) {
                $descArray = $decoded;
            }
        }
        $descArray['code'] = $code;
        return json_encode($descArray, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
    }
}
