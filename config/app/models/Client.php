<?php

namespace App\Models;

use App\Core\Database;

class Client
{
    private static bool $schemaChecked = false;
    private static ?array $current = null;

    public static function ensureSchema(): void
    {
        if (self::$schemaChecked) {
            return;
        }

        $db = Database::connection();
        $db->exec(
            "CREATE TABLE IF NOT EXISTS clients (
                id INT AUTO_INCREMENT PRIMARY KEY,
                uuid VARCHAR(36) UNIQUE,
                name VARCHAR(150) NOT NULL,
                slug VARCHAR(80) NOT NULL UNIQUE,
                is_active TINYINT(1) DEFAULT 1,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
            )"
        );

        self::seedDefaultClients();
        self::$schemaChecked = true;
    }

    public static function all(): array
    {
        self::ensureSchema();
        return Database::connection()
            ->query('SELECT id, uuid, name, slug FROM clients WHERE is_active = 1 ORDER BY id')
            ->fetchAll();
    }

    public static function current(): array
    {
        if (self::$current) {
            return self::$current;
        }

        $client = self::fromToken() ?: self::findBySlug(self::requestedSlug());
        if (!$client) {
            $client = self::defaultClient();
        }

        self::$current = $client;
        return self::$current;
    }

    public static function currentId(): int
    {
        return (int) self::current()['id'];
    }

    public static function select(mixed $client): array
    {
        $selected = self::findBySlug(self::normalizeSlug((string) $client));
        if (!$selected) {
            error_response('Invalid client selected.', 422);
        }

        self::$current = $selected;
        return $selected;
    }

    public static function findBySlug(string $slug): ?array
    {
        self::ensureSchema();
        $slug = self::normalizeSlug($slug);
        if ($slug === '') {
            return null;
        }

        $stmt = Database::connection()->prepare('SELECT * FROM clients WHERE slug = ? AND is_active = 1 LIMIT 1');
        $stmt->execute([$slug]);
        return $stmt->fetch() ?: null;
    }

    public static function normalizeSlug(string $slug): string
    {
        $slug = strtolower(trim($slug));
        return preg_replace('/[^a-z0-9_-]/', '', $slug) ?: '';
    }

    private static function requestedSlug(): string
    {
        return self::normalizeSlug((string) ($_SERVER['HTTP_X_POS_CLIENT'] ?? $_GET['client'] ?? $_POST['client'] ?? ''));
    }

    private static function fromToken(): ?array
    {
        $header = $_SERVER['HTTP_AUTHORIZATION'] ?? '';
        if (!preg_match('/Bearer\s+([^.]+\.[^.]+\.[^.]+)/', $header, $matches)) {
            return null;
        }

        $payload = \App\Services\JWTService::decode($matches[1]);
        if (!$payload) {
            return null;
        }

        if (!empty($payload['client_id'])) {
            self::ensureSchema();
            $stmt = Database::connection()->prepare('SELECT * FROM clients WHERE id = ? AND is_active = 1 LIMIT 1');
            $stmt->execute([(int) $payload['client_id']]);
            return $stmt->fetch() ?: null;
        }

        if (!empty($payload['client'])) {
            return self::findBySlug((string) $payload['client']);
        }

        return null;
    }

    private static function defaultClient(): array
    {
        self::ensureSchema();
        $slug = self::normalizeSlug((string) env('POS_DEFAULT_CLIENT', 'daalroti'));
        return self::findBySlug($slug) ?: self::firstClient();
    }

    private static function firstClient(): array
    {
        $client = Database::connection()
            ->query('SELECT * FROM clients WHERE is_active = 1 ORDER BY id LIMIT 1')
            ->fetch();

        if ($client) {
            return $client;
        }

        self::insertClient('Dal Roti', 'daalroti');
        return self::findBySlug('daalroti');
    }

    private static function seedDefaultClients(): void
    {
        self::insertClient('Dal Roti', 'daalroti');
        self::insertClient('Chay Chaupal', 'chaychaupal');
    }

    private static function insertClient(string $name, string $slug): void
    {
        $stmt = Database::connection()->prepare(
            'INSERT INTO clients (uuid, name, slug)
             VALUES (UUID(), ?, ?)
             ON DUPLICATE KEY UPDATE name = VALUES(name), is_active = 1'
        );
        $stmt->execute([$name, $slug]);
    }
}
