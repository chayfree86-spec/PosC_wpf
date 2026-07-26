<?php

declare(strict_types=1);

require_once dirname(__DIR__) . '/api/_bootstrap.php';

function isSeedMigration(string $path): bool
{
    return str_contains(basename($path), 'seed');
}

function isRunnableMigration(string $path): bool
{
    return preg_match('/^\d{4}_\d{2}_\d{2}_\d{6}_.+\.sql$/', basename($path)) === 1;
}

function splitSqlStatements(string $sql): array
{
    $statements = [];
    $buffer = '';
    $length = strlen($sql);
    $quote = null;
    $lineComment = false;
    $blockComment = false;

    for ($i = 0; $i < $length; $i++) {
        $char = $sql[$i];
        $next = $i + 1 < $length ? $sql[$i + 1] : '';

        if ($lineComment) {
            $buffer .= $char;
            if ($char === "\n") {
                $lineComment = false;
            }
            continue;
        }

        if ($blockComment) {
            $buffer .= $char;
            if ($char === '*' && $next === '/') {
                $buffer .= $next;
                $i++;
                $blockComment = false;
            }
            continue;
        }

        if ($quote !== null) {
            $buffer .= $char;
            if ($char === '\\' && $next !== '') {
                $buffer .= $next;
                $i++;
                continue;
            }
            if ($char === $quote) {
                $quote = null;
            }
            continue;
        }

        if (($char === '-' && $next === '-') || $char === '#') {
            $lineComment = true;
            $buffer .= $char;
            if ($char === '-') {
                $buffer .= $next;
                $i++;
            }
            continue;
        }

        if ($char === '/' && $next === '*') {
            $blockComment = true;
            $buffer .= $char . $next;
            $i++;
            continue;
        }

        if ($char === '\'' || $char === '"' || $char === '`') {
            $quote = $char;
            $buffer .= $char;
            continue;
        }

        if ($char === ';') {
            $statement = trim($buffer);
            if ($statement !== '') {
                $statements[] = $statement;
            }
            $buffer = '';
            continue;
        }

        $buffer .= $char;
    }

    $statement = trim($buffer);
    if ($statement !== '') {
        $statements[] = $statement;
    }

    return $statements;
}

try {
    $includeSeeds = in_array('--seed', $argv ?? [], true) || (($_GET['seed'] ?? '') === '1');
    $db = App\Core\Database::connection();
    $db->exec(
        'CREATE TABLE IF NOT EXISTS schema_migrations (
            migration VARCHAR(255) PRIMARY KEY,
            applied_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci'
    );

    $files = array_values(array_filter(glob(__DIR__ . '/*.sql') ?: [], 'isRunnableMigration'));
    sort($files, SORT_STRING);

    $messages = [];
    foreach ($files as $file) {
        $name = basename($file);
        if (!$includeSeeds && isSeedMigration($file)) {
            $messages[] = "Skipped seed: {$name}";
            continue;
        }

        $alreadyApplied = $db
            ->prepare('SELECT 1 FROM schema_migrations WHERE migration = ? LIMIT 1');
        $alreadyApplied->execute([$name]);

        if ($alreadyApplied->fetchColumn()) {
            $messages[] = "Already applied: {$name}";
            continue;
        }

        $sql = file_get_contents($file);
        if ($sql === false || trim($sql) === '') {
            $messages[] = "Skipped empty file: {$name}";
            continue;
        }

        foreach (splitSqlStatements($sql) as $statement) {
            $db->exec($statement);
        }

        $record = $db->prepare('INSERT INTO schema_migrations (migration) VALUES (?)');
        $record->execute([$name]);
        $messages[] = "Applied: {$name}";
    }

    $response = [
        'success' => true,
        'seed' => $includeSeeds,
        'message' => implode(' | ', $messages),
    ];
} catch (Throwable $e) {
    http_response_code(500);
    $response = [
        'success' => false,
        'message' => 'Migration failed: ' . $e->getMessage(),
    ];
}

echo json_encode($response, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE);
