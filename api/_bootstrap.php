<?php

declare(strict_types=1);

date_default_timezone_set('Asia/Kolkata');

$root = dirname(__DIR__);

require_once $root . '/app/helpers/response.php';
require_once $root . '/app/helpers/validator.php';

spl_autoload_register(function (string $class) use ($root): void {
    $prefix = 'App\\';
    if (strncmp($class, $prefix, strlen($prefix)) !== 0) {
        return;
    }

    $segments = explode('\\', substr($class, strlen($prefix)));
    if ($segments) {
        $segments[0] = strtolower($segments[0]);
    }

    $file = $root . '/app/' . implode('/', $segments) . '.php';
    if (is_file($file)) {
        require_once $file;
    }
});

function env(string $key, mixed $default = null): mixed
{
    static $values = null;

    if ($values === null) {
        $envFile = dirname(__DIR__) . '/.env';
        $parsed = is_file($envFile) ? parse_ini_file($envFile, false, INI_SCANNER_TYPED) : [];
        $values = is_array($parsed) ? $parsed : [];
    }

    return array_key_exists($key, $values) ? $values[$key] : $default;
}

header('Access-Control-Allow-Origin: *');
header('Access-Control-Allow-Headers: Content-Type, Authorization, X-POS-Client');
header('Access-Control-Allow-Methods: GET, POST, PUT, PATCH, DELETE, OPTIONS');
header('Access-Control-Allow-Private-Network: true');

if (($_SERVER['REQUEST_METHOD'] ?? '') === 'OPTIONS') {
    http_response_code(204);
    exit;
}
