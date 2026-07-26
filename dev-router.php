<?php

$path = parse_url($_SERVER['REQUEST_URI'], PHP_URL_PATH) ?: '/';

if (str_starts_with($path, '/possoftware-final/')) {
    $_SERVER['REQUEST_URI'] = substr($_SERVER['REQUEST_URI'], strlen('/possoftware-final')) ?: '/';
    $path = parse_url($_SERVER['REQUEST_URI'], PHP_URL_PATH) ?: '/';
}

$file = __DIR__ . $path;

if (is_file($file)) {
    return false;
}

// The router strips dirname(SCRIPT_NAME) from the path so the API can live under a
// sub-directory on Apache (/possoftware-final/...). The built-in server sets SCRIPT_NAME
// to the requested path instead, so "/api/health" had "/api" stripped off it and every
// route except "/api" itself came back as 404. Point it at the front controller.
$_SERVER['SCRIPT_NAME'] = '/index.php';
$_SERVER['PHP_SELF'] = '/index.php';

require __DIR__ . '/index.php';
