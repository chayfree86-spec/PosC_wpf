<?php
$root = dirname(__DIR__, 2);
require_once $root . '/app/helpers/response.php';
require_once $root . '/app/helpers/validator.php';

spl_autoload_register(function (string $class) use ($root): void {
    $prefix = 'App\\';
    if (strncmp($class, $prefix, strlen($prefix)) !== 0) { return; }
    $segments = explode('\\', substr($class, strlen($prefix)));
    if ($segments) { $segments[0] = strtolower($segments[0]); }
    $file = $root . '/app/' . implode('/', $segments) . '.php';
    if (is_file($file)) { require_once $file; }
});

function env(string $key, mixed $default = null): mixed {
    static $values = null;
    if ($values === null) {
        $envFile = dirname(__DIR__, 2) . '/.env';
        $parsed = is_file($envFile) ? parse_ini_file($envFile, false, INI_SCANNER_TYPED) : [];
        $values = is_array($parsed) ? $parsed : [];
    }
    return array_key_exists($key, $values) ? $values[$key] : $default;
}

echo json_encode([
    'DB_DATABASE' => env('DB_DATABASE'),
    'DB_HOST' => env('DB_HOST'),
    'DB_USERNAME' => env('DB_USERNAME'),
    'DB_PASSWORD' => env('DB_PASSWORD'),
    'SERVER_NAME' => $_SERVER['SERVER_NAME'] ?? 'N/A',
    'HTTP_HOST' => $_SERVER['HTTP_HOST'] ?? 'N/A',
    'REQUEST_URI' => $_SERVER['REQUEST_URI'] ?? 'N/A',
], JSON_PRETTY_PRINT);
