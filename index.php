<?php

declare(strict_types=1);

date_default_timezone_set('Asia/Kolkata');

use App\Core\Router;

$root = __DIR__;

require_once $root . '/app/helpers/response.php';
require_once $root . '/app/helpers/validator.php';

spl_autoload_register(function (string $class): void {
    global $root;

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
        $envFile = __DIR__ . '/.env';
        $parsed = is_file($envFile) ? parse_ini_file($envFile, false, INI_SCANNER_TYPED) : [];
        $values = is_array($parsed) ? $parsed : [];
    }

    return array_key_exists($key, $values) ? $values[$key] : $default;
}

set_exception_handler(function (Throwable $error): void {
    $debug = filter_var(env('APP_DEBUG', false), FILTER_VALIDATE_BOOLEAN);

    error_response(
        'Server error.',
        500,
        $debug ? [
            'message' => $error->getMessage(),
            'file' => $error->getFile(),
            'line' => $error->getLine(),
        ] : null
    );
});

header('Access-Control-Allow-Origin: *');
header('Access-Control-Allow-Headers: Content-Type, Authorization, X-POS-Client');
header('Access-Control-Allow-Methods: GET, POST, PUT, PATCH, DELETE, OPTIONS');

if ($_SERVER['REQUEST_METHOD'] === 'OPTIONS') {
    http_response_code(204);
    exit;
}

$router = new Router();
require $root . '/routes/api.php';

$dispatchUri = $_SERVER['REQUEST_URI'];
if (isset($_GET['__path'])) {
    $dispatchUri = (string) $_GET['__path'];

    // The menu-admin proxy packs the ORIGINAL query string inside __path
    // (proxy.php?__api=1&__path=/api/reports/summary?date=...&report_client=...).
    // Only the path part was being used for routing while the embedded
    // parameters never reached $_GET -- so controllers silently fell back to
    // defaults (today / day / token's client). That made every report filter
    // (date, month, client switch) look broken. Unpack them here.
    $embeddedQuery = parse_url($dispatchUri, PHP_URL_QUERY);
    if ($embeddedQuery) {
        parse_str($embeddedQuery, $embeddedParams);
        if (is_array($embeddedParams) && $embeddedParams) {
            $_GET = array_merge($_GET, $embeddedParams);
            $_REQUEST = array_merge($_REQUEST, $embeddedParams);
        }
    }
}

$router->dispatch($_SERVER['REQUEST_METHOD'], $dispatchUri);
