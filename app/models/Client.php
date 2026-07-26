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

        // PERFORMANCE: this ran a CREATE TABLE plus TWO seed INSERT..ON
        // DUPLICATE writes on EVERY api request. The schema/seed only needs
        // re-checking rarely -- cache the fact it's done for 24h (same pattern
        // as Order::ensureOrderColumns), which removes ~4 round-trips and two
        // writes from every report/menu call.
        $cacheFile = sys_get_temp_dir() . '/pos_clients_schema_' . md5((string) env('DB_DATABASE', 'pos_qr_system')) . '.cache';
        if (file_exists($cacheFile) && (time() - filemtime($cacheFile)) < 86400) {
            self::$schemaChecked = true;
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

        // Add missing columns if they don't exist
        try {
            $db->query("SELECT uuid FROM clients LIMIT 1");
        } catch (\PDOException $e) {
            try {
                $db->exec("ALTER TABLE clients ADD COLUMN uuid VARCHAR(36) AFTER id");
                $db->exec("UPDATE clients SET uuid = UUID() WHERE uuid IS NULL OR uuid = ''");
                $db->exec("ALTER TABLE clients ADD UNIQUE (uuid)");
            } catch (\PDOException $ex) {
                // Ignore if already added by concurrent request
            }
        }

        try {
            $db->query("SELECT is_active FROM clients LIMIT 1");
        } catch (\PDOException $e) {
            try {
                $db->exec("ALTER TABLE clients ADD COLUMN is_active TINYINT(1) DEFAULT 1 AFTER slug");
            } catch (\PDOException $ex) {
                // Ignore if already added by concurrent request
            }
        }

        self::seedDefaultClients();
        self::$schemaChecked = true;
        @touch($cacheFile);
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

        // EXPLICIT client identification (X-Client-Id header, X-POS-Client
        // header or client/client_id request param) wins over the login
        // token's client. The admin dashboard keeps ONE login token but lets
        // the operator switch clients from a dropdown -- with the old
        // token-first order every page silently kept showing the LOGIN
        // client's data no matter what was selected. The token remains the
        // fallback when a request doesn't say which client it means.
        $client = self::fromClientIdHeader() ?: self::findBySlug(self::requestedSlug()) ?: self::fromToken();
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

    private static function fromClientIdHeader(): ?array
    {
        $clientId = (int) ($_SERVER['HTTP_X_CLIENT_ID'] ?? $_GET['client_id'] ?? $_POST['client_id'] ?? 0);
        if ($clientId > 0) {
            self::ensureSchema();
            $stmt = Database::connection()->prepare('SELECT * FROM clients WHERE id = ? AND is_active = 1 LIMIT 1');
            $stmt->execute([$clientId]);
            return $stmt->fetch() ?: null;
        }
        return null;
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
