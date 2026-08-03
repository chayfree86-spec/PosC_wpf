<?php

declare(strict_types=1);

/**
 * Runs pending database migrations from the command line:  php migrate.php
 *
 * The reliable, explicit half of the deploy flow — run it over SSH, or as a post-deploy command
 * if the host supports one. The API also applies migrations on its own (see MigrationRunner
 * ::autoRun in index.php) for hosts where no command can run after a git pull, so on Hostinger's
 * shared plans you can rely on that and keep this for when you want to see the result yourself.
 *
 * Exit code 0 = nothing pending or all applied; 1 = a migration failed.
 */

date_default_timezone_set('Asia/Kolkata');

$root = __DIR__;

// Same tiny .env reader the API uses, so this script needs no framework boot.
if (!function_exists('env')) {
    function env(string $key, mixed $default = null): mixed
    {
        static $values = null;
        if ($values === null) {
            $envFile = __DIR__ . '/.env';
            $parsed = is_file($envFile) ? parse_ini_file($envFile, false, INI_SCANNER_TYPED) : [];
            $values = is_array($parsed) ? $parsed : [];
        }
        return array_key_exists($key, $values) ? $values[$key] : $default;
    }
}

// Minimal autoloader for the App\ classes this script touches.
spl_autoload_register(static function (string $class) use ($root): void {
    $prefix = 'App\\';
    if (strncmp($class, $prefix, strlen($prefix)) !== 0) {
        return;
    }
    $segments = explode('\\', substr($class, strlen($prefix)));
    $segments[0] = strtolower($segments[0]);
    $file = $root . '/app/' . implode('/', $segments) . '.php';
    if (is_file($file)) {
        require_once $file;
    }
});

use App\Core\MigrationRunner;

$runner = new MigrationRunner();

// One-time adoption of an existing database: record everything present as applied without running
// it, and arm the auto-runner. Safe on a live DB — writes only to schema_migrations, runs no data
// SQL. Do this once after pointing the runner at a database that already has its schema.
if (in_array('--baseline', $argv, true)) {
    $names = $runner->baseline();
    fwrite(STDOUT, 'Baselined ' . count($names) . " migration(s) as applied (none executed):\n");
    foreach ($names as $n) {
        fwrite(STDOUT, "  = $n\n");
    }
    fwrite(STDOUT, "Auto-migration is now armed. Future migrations will apply on deploy.\n");
    exit(0);
}

$pending = $runner->pending();
if (!$pending) {
    fwrite(STDOUT, "Nothing to migrate — schema is up to date.\n");
    exit(0);
}

fwrite(STDOUT, 'Pending: ' . implode(', ', $pending) . "\n");

$results = $runner->run();
$failed = false;
foreach ($results as $r) {
    if ($r['status'] === 'applied') {
        fwrite(STDOUT, "  ✓ {$r['migration']}\n");
    } else {
        $failed = true;
        fwrite(STDERR, "  ✗ {$r['migration']}: {$r['error']}\n");
    }
}

exit($failed ? 1 : 0);
