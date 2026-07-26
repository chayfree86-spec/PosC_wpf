<?php

namespace App\Models;

use App\Core\Database;

class User
{
    private static bool $schemaChecked = false;

    private static function ensureSchema(): void
    {
        if (self::$schemaChecked) {
            return;
        }

        Client::ensureSchema();
        $db = Database::connection();
        $exists = $db->prepare(
            "SELECT 1
             FROM INFORMATION_SCHEMA.COLUMNS
             WHERE TABLE_SCHEMA = DATABASE()
               AND TABLE_NAME = 'users'
               AND COLUMN_NAME = 'client_id'
             LIMIT 1"
        );
        $exists->execute();

        if (!$exists->fetch()) {
            $db->exec('ALTER TABLE users ADD COLUMN client_id INT NOT NULL DEFAULT 1 AFTER uuid');
            $defaultClientId = (int) ($db->query("SELECT id FROM clients WHERE slug = 'daalroti' LIMIT 1")->fetchColumn() ?: 1);
            $db->exec('UPDATE users SET client_id = ' . $defaultClientId . ' WHERE client_id = 1');
        }

        self::$schemaChecked = true;
    }

    public static function find(int $id): ?array
    {
        self::ensureSchema();
        $stmt = Database::connection()->prepare('SELECT * FROM users WHERE id = ? AND client_id = ? LIMIT 1');
        $stmt->execute([$id, Client::currentId()]);
        return $stmt->fetch() ?: null;
    }

    public static function findByEmail(string $email): ?array
    {
        self::ensureSchema();
        $stmt = Database::connection()->prepare('SELECT * FROM users WHERE email = ? AND client_id = ? LIMIT 1');
        $stmt->execute([$email, Client::currentId()]);
        return $stmt->fetch() ?: null;
    }

    public static function findByPhone(string $phone): ?array
    {
        self::ensureSchema();
        $digits = preg_replace('/\D+/', '', $phone);
        $lastTen = substr($digits, -10);
        $stmt = Database::connection()->prepare(
            'SELECT * FROM users
             WHERE REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(phone, " ", ""), "-", ""), "+", ""), "(", ""), ")", "") LIKE ?
               AND client_id = ?
             ORDER BY id
             LIMIT 1'
        );
        $stmt->execute(['%' . $lastTen, Client::currentId()]);
        return $stmt->fetch() ?: null;
    }

    public static function create(array $data): int
    {
        self::ensureSchema();
        $stmt = Database::connection()->prepare(
            'INSERT INTO users (uuid, client_id, name, phone, email, password, pin, role) VALUES (UUID(), ?, ?, ?, ?, ?, ?, ?)'
        );
        $stmt->execute([
            Client::currentId(),
            $data['name'],
            $data['phone'] ?? null,
            $data['email'] ?? null,
            isset($data['password']) ? password_hash($data['password'], PASSWORD_DEFAULT) : null,
            isset($data['pin']) ? password_hash(preg_replace('/\D+/', '', (string) $data['pin']), PASSWORD_DEFAULT) : null,
            $data['role'] ?? 'waiter',
        ]);

        return (int) Database::connection()->lastInsertId();
    }

    public static function updateProfile(int $id, array $data): ?array
    {
        self::ensureSchema();
        $fields = [];
        $params = [];

        foreach (['name', 'phone', 'email'] as $field) {
            if (array_key_exists($field, $data)) {
                $fields[] = "{$field} = ?";
                $params[] = $data[$field] ?: null;
            }
        }

        if (!empty($data['pin'])) {
            $fields[] = 'pin = ?';
            $params[] = password_hash(preg_replace('/\D+/', '', (string) $data['pin']), PASSWORD_DEFAULT);
        }

        if (!$fields) {
            return self::find($id);
        }

        $params[] = $id;
        $params[] = Client::currentId();
        $stmt = Database::connection()->prepare(
            'UPDATE users SET ' . implode(', ', $fields) . ', updated_at = CURRENT_TIMESTAMP WHERE id = ? AND client_id = ?'
        );
        $stmt->execute($params);

        return self::find($id);
    }
}
