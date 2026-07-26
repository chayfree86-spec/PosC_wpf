<?php

namespace App\Models;

use App\Core\Database;

class Category
{
    public static function all(): array
    {
        return Database::connection()
            ->query('SELECT * FROM categories ORDER BY parent_id IS NOT NULL, sort_order, name')
            ->fetchAll();
    }

    public static function create(array $data): int
    {
        $stmt = Database::connection()->prepare(
            'INSERT INTO categories (uuid, name, image, parent_id, sort_order, is_active) VALUES (UUID(), ?, ?, ?, ?, ?)'
        );
        $stmt->execute([
            $data['name'],
            $data['image'] ?? null,
            $data['parent_id'] ?? null,
            $data['sort_order'] ?? 0,
            $data['is_active'] ?? 1,
        ]);

        return (int) Database::connection()->lastInsertId();
    }

    public static function update(int $id, array $data): bool
    {
        $stmt = Database::connection()->prepare(
            'UPDATE categories SET name = ?, image = ?, parent_id = ?, sort_order = ?, is_active = ?, sync_version = sync_version + 1 WHERE id = ?'
        );

        return $stmt->execute([
            $data['name'],
            $data['image'] ?? null,
            $data['parent_id'] ?? null,
            $data['sort_order'] ?? 0,
            $data['is_active'] ?? 1,
            $id,
        ]);
    }

    public static function delete(int $id): bool
    {
        $stmt = Database::connection()->prepare('DELETE FROM categories WHERE id = ?');
        return $stmt->execute([$id]);
    }
}
