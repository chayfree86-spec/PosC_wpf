<?php

namespace App\Core;

use PDO;
use Throwable;

/**
 * Applies pending SQL migrations from /migrations to the live database, once each, in filename
 * order — the server-side counterpart of the deploy flow "git push → Hostinger pulls → schema
 * catches up".
 *
 * How data survives a deploy:
 *   - Every migration is recorded in schema_migrations the moment it succeeds, and an already
 *     recorded migration is NEVER run again. A deploy therefore only ever runs the *new* files.
 *   - The runner never drops or recreates the database, and never touches an existing table on
 *     its own — it runs exactly the SQL in the migration files and nothing else. Whether a given
 *     migration is safe is the migration's own responsibility (write additive, idempotent SQL);
 *     the runner's job is only to make sure each runs a single time.
 *
 * It is deliberately conservative about *when* it runs (see autoRun): a signature of the
 * migrations folder is cached, so once everything is applied the per-request cost is a stat of a
 * handful of files and nothing more — no database round-trip.
 */
final class MigrationRunner
{
    private PDO $db;
    private string $dir;
    private string $storage;

    /** How long to wait before retrying after a failed run, so a broken migration can't storm. */
    private const RETRY_COOLDOWN_SECONDS = 120;

    /**
     * A sentinel row in schema_migrations meaning "this database has been adopted by the runner".
     *
     * It exists to protect an EXISTING database. The migrations folder carries the project's whole
     * history — including a create-all-tables migration and a seed migration that overwrites rows
     * with ON DUPLICATE KEY UPDATE. On a live database those must never run; the schema is already
     * there and the seed would stamp real categories and prices back to defaults. So until a
     * baseline is recorded (marking everything present as already applied) autoRun stays dormant
     * and refuses to run anything on its own.
     */
    private const BASELINE_MARKER = '__baseline__';

    public function __construct(?PDO $db = null, ?string $migrationsDir = null, ?string $storageDir = null)
    {
        $this->db = $db ?? Database::connection();
        $this->dir = rtrim($migrationsDir ?? dirname(__DIR__, 2) . '/migrations', '/\\');
        $this->storage = $this->resolveStorage($storageDir);
    }

    /**
     * The zero-config entry the API bootstrap calls on every request. Cheap when there is nothing
     * to do, safe under concurrency, and it never lets a migration problem take the API down —
     * the worst case is that the schema stays where it was and the failure is logged.
     */
    public function autoRun(): void
    {
        try {
            $state = $this->readState();

            // Dormant until the database has been adopted. Once we've seen the baseline we cache
            // the fact (it never un-baselines), so the steady state costs no query — but while it
            // is missing we re-check each request cheaply, so the runner wakes on its own the
            // moment the baseline SQL is run, without needing the migration files to change.
            if (($state['baselined'] ?? false) !== true) {
                if (!$this->isBaselined()) {
                    return;
                }
                $state['baselined'] = true;
                $this->writeState($state);
            }

            $signature = $this->signature();

            // Fast path: the folder hasn't changed since we last finished, so there is nothing new.
            if (($state['sig'] ?? null) === $signature && ($state['ok'] ?? false) === true) {
                return;
            }

            // A run that failed on this same set of files backs off, rather than retrying on every
            // single request until someone notices.
            if (($state['sig'] ?? null) === $signature && ($state['ok'] ?? false) === false
                && (time() - (int) ($state['ts'] ?? 0)) < self::RETRY_COOLDOWN_SECONDS) {
                return;
            }

            // One runner at a time: a second request during a deploy just skips and serves.
            $lock = @fopen($this->storage . '/migrations.lock', 'c');
            if ($lock === false || !flock($lock, LOCK_EX | LOCK_NB)) {
                if ($lock !== false) {
                    fclose($lock);
                }
                return;
            }

            try {
                $results = $this->run();
                $failed = array_filter($results, static fn ($r) => $r['status'] === 'failed');
                $this->writeState(['sig' => $signature, 'ok' => count($failed) === 0, 'ts' => time()]);
                if ($results) {
                    $this->log($results);
                }
            } finally {
                flock($lock, LOCK_UN);
                fclose($lock);
            }
        } catch (Throwable $e) {
            // Never surface a migration problem as a broken API. It is logged and retried later.
            $this->log([['migration' => '(runner)', 'status' => 'failed', 'error' => $e->getMessage()]]);
        }
    }

    /**
     * Applies every pending migration, stopping at the first failure (later files may depend on an
     * earlier one). Returns a per-file log — this is what the CLI prints.
     */
    public function run(): array
    {
        $this->ensureTable();
        $applied = $this->applied();
        $results = [];

        foreach ($this->files() as $path) {
            $name = basename($path);
            if (in_array($name, $applied, true)) {
                continue;
            }

            try {
                $sql = (string) file_get_contents($path);
                foreach ($this->splitStatements($sql) as $statement) {
                    $this->db->exec($statement);
                }
                $this->record($name);
                $results[] = ['migration' => $name, 'status' => 'applied'];
            } catch (Throwable $e) {
                $results[] = ['migration' => $name, 'status' => 'failed', 'error' => $e->getMessage()];
                break;
            }
        }

        return $results;
    }

    /**
     * Adopts an existing database: records every migration currently in the folder as applied,
     * WITHOUT running any of them, and drops the baseline marker so autoRun comes alive.
     *
     * This is the one-time step for a database whose schema is already in place (the live server).
     * It runs no SQL against the data — it only writes rows into schema_migrations — so it cannot
     * change or lose anything. From here on only migrations added AFTER the baseline are executed.
     */
    public function baseline(): array
    {
        $this->ensureTable();
        $names = array_map('basename', $this->files());
        foreach ($names as $name) {
            $this->record($name);
        }
        $this->record(self::BASELINE_MARKER);

        // Reflect it in the cached state immediately, so the very next request takes the fast path.
        $state = $this->readState();
        $state['baselined'] = true;
        $this->writeState($state);

        return $names;
    }

    /** Whether this database has been adopted (see baseline / BASELINE_MARKER). */
    public function isBaselined(): bool
    {
        $this->ensureTable();
        $stmt = $this->db->prepare('SELECT 1 FROM `schema_migrations` WHERE `migration` = ? LIMIT 1');
        $stmt->execute([self::BASELINE_MARKER]);
        return (bool) $stmt->fetchColumn();
    }

    /** Migration files not yet recorded as applied, in filename order. */
    public function pending(): array
    {
        $this->ensureTable();
        $applied = $this->applied();
        return array_values(array_filter(
            array_map('basename', $this->files()),
            static fn ($name) => !in_array($name, $applied, true)
        ));
    }

    // ── internals ────────────────────────────────────────────────────────────

    private function ensureTable(): void
    {
        $this->db->exec(
            "CREATE TABLE IF NOT EXISTS `schema_migrations` (
                `migration` VARCHAR(255) NOT NULL,
                `applied_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                PRIMARY KEY (`migration`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4"
        );
    }

    private function applied(): array
    {
        return $this->db->query('SELECT `migration` FROM `schema_migrations`')
            ->fetchAll(PDO::FETCH_COLUMN) ?: [];
    }

    private function record(string $name): void
    {
        // INSERT IGNORE: recording is idempotent even if a hand-run migration got there first.
        $stmt = $this->db->prepare(
            'INSERT IGNORE INTO `schema_migrations` (`migration`, `applied_at`) VALUES (?, NOW())'
        );
        $stmt->execute([$name]);
    }

    /** All *.sql migration files, sorted so the date-prefixed names apply in order. */
    private function files(): array
    {
        if (!is_dir($this->dir)) {
            return [];
        }
        $files = glob($this->dir . '/*.sql') ?: [];
        sort($files, SORT_STRING);
        return $files;
    }

    /**
     * Splits a migration file into individual statements on top-level semicolons, ignoring the
     * ones inside quotes, backticks, or comments. Targets ordinary schema migrations (DDL + simple
     * DML); it does not parse stored routines that redefine DELIMITER.
     */
    private function splitStatements(string $sql): array
    {
        $statements = [];
        $buffer = '';
        $len = strlen($sql);
        $inSingle = $inDouble = $inBacktick = false;

        for ($i = 0; $i < $len; $i++) {
            $ch = $sql[$i];
            $next = $i + 1 < $len ? $sql[$i + 1] : '';

            if (!$inSingle && !$inDouble && !$inBacktick) {
                // -- line comment (MySQL needs whitespace/EOL after the dashes)
                if ($ch === '-' && $next === '-'
                    && ($i + 2 >= $len || ctype_space($sql[$i + 2]))) {
                    while ($i < $len && $sql[$i] !== "\n") {
                        $i++;
                    }
                    continue;
                }
                // # line comment
                if ($ch === '#') {
                    while ($i < $len && $sql[$i] !== "\n") {
                        $i++;
                    }
                    continue;
                }
                // /* block comment */
                if ($ch === '/' && $next === '*') {
                    $i += 2;
                    while ($i + 1 < $len && !($sql[$i] === '*' && $sql[$i + 1] === '/')) {
                        $i++;
                    }
                    $i++; // land on the closing '/'
                    continue;
                }
                // statement terminator
                if ($ch === ';') {
                    $trimmed = trim($buffer);
                    if ($trimmed !== '') {
                        $statements[] = $trimmed;
                    }
                    $buffer = '';
                    continue;
                }
            }

            if (!$inDouble && !$inBacktick && $ch === "'") {
                $inSingle = !$inSingle;
            } elseif (!$inSingle && !$inBacktick && $ch === '"') {
                $inDouble = !$inDouble;
            } elseif (!$inSingle && !$inDouble && $ch === '`') {
                $inBacktick = !$inBacktick;
            }

            $buffer .= $ch;
        }

        $trimmed = trim($buffer);
        if ($trimmed !== '') {
            $statements[] = $trimmed;
        }

        return $statements;
    }

    /** A cheap fingerprint of the migrations folder — name, size and mtime of each file. */
    private function signature(): string
    {
        $parts = [];
        foreach ($this->files() as $path) {
            $parts[] = basename($path) . ':' . filesize($path) . ':' . filemtime($path);
        }
        return md5(implode('|', $parts));
    }

    private function readState(): array
    {
        $raw = @file_get_contents($this->storage . '/migrations.state.json');
        if ($raw === false) {
            return [];
        }
        $data = json_decode($raw, true);
        return is_array($data) ? $data : [];
    }

    private function writeState(array $state): void
    {
        @file_put_contents(
            $this->storage . '/migrations.state.json',
            json_encode($state),
            LOCK_EX
        );
    }

    private function log(array $results): void
    {
        $line = '[' . date('Y-m-d H:i:s') . '] ' . json_encode($results) . "\n";
        @file_put_contents($this->storage . '/migrations.log', $line, FILE_APPEND | LOCK_EX);
    }

    /** A writable place for the lock/state/log; falls back to the system temp dir. */
    private function resolveStorage(?string $override): string
    {
        $candidate = $override ?? dirname(__DIR__, 2) . '/storage';
        if (!is_dir($candidate)) {
            @mkdir($candidate, 0775, true);
        }
        if (is_dir($candidate) && is_writable($candidate)) {
            return $candidate;
        }
        return sys_get_temp_dir();
    }
}
