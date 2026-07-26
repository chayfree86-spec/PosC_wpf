<?php

namespace App\Models;

use App\Core\Database;
use App\Models\Client;

class GalleryImage
{
    private static bool $tableChecked = false;

    private static function ensureTable(): void
    {
        if (self::$tableChecked) {
            return;
        }

        $db = Database::connection();
        $db->exec(
            "CREATE TABLE IF NOT EXISTS gallery_images (
                id INT AUTO_INCREMENT PRIMARY KEY,
                client_id INT NOT NULL,
                url TEXT NOT NULL,
                filename VARCHAR(255),
                is_visible TINYINT(1) DEFAULT 1,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                KEY idx_gallery_images_client (client_id)
            )"
        );

        // Add category_id and sub_category_id if they don't exist
        try {
            $db->exec("ALTER TABLE gallery_images ADD COLUMN category_id INT NULL");
        } catch (\Throwable $e) {}
        try {
            $db->exec("ALTER TABLE gallery_images ADD COLUMN sub_category_id INT NULL");
        } catch (\Throwable $e) {}

        self::$tableChecked = true;
    }

    public static function all(): array
    {
        self::ensureTable();
        $stmt = Database::connection()->prepare(
            'SELECT * FROM gallery_images ORDER BY id DESC'
        );
        $stmt->execute();
        return $stmt->fetchAll();
    }

    public static function create(array $data): int
    {
        self::ensureTable();
        $stmt = Database::connection()->prepare(
            'INSERT INTO gallery_images (client_id, url, filename, is_visible, category_id, sub_category_id) VALUES (?, ?, ?, ?, ?, ?)'
        );
        $stmt->execute([
            Client::currentId(),
            $data['url'],
            $data['filename'] ?? null,
            $data['is_visible'] ?? 1,
            !empty($data['category_id']) ? (int) $data['category_id'] : null,
            !empty($data['sub_category_id']) ? (int) $data['sub_category_id'] : null
        ]);
        return (int) Database::connection()->lastInsertId();
    }

    public static function update(int $id, array $data): bool
    {
        self::ensureTable();
        $stmt = Database::connection()->prepare(
            'UPDATE gallery_images SET url = ?, filename = ?, is_visible = ?, category_id = ?, sub_category_id = ? WHERE id = ?'
        );
        return $stmt->execute([
            $data['url'],
            $data['filename'] ?? null,
            $data['is_visible'] ?? 1,
            !empty($data['category_id']) ? (int) $data['category_id'] : null,
            !empty($data['sub_category_id']) ? (int) $data['sub_category_id'] : null,
            $id
        ]);
    }

    public static function delete(int $id): bool
    {
        self::ensureTable();
        $stmt = Database::connection()->prepare(
            'DELETE FROM gallery_images WHERE id = ?'
        );
        return $stmt->execute([$id]);
    }
}
