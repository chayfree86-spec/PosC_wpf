<?php

declare(strict_types=1);

// CORS Headers (Safety fallback for local/cross-origin testing)
header('Access-Control-Allow-Origin: *');
header('Access-Control-Allow-Headers: Content-Type, Authorization, X-POS-Client');
header('Access-Control-Allow-Methods: GET, POST, PUT, PATCH, DELETE, OPTIONS');
header('Access-Control-Allow-Private-Network: true');

if (($_SERVER['REQUEST_METHOD'] ?? '') === 'OPTIONS') {
    http_response_code(204);
    exit;
}

// Load the main API entry point (index.php at root)
$rootIndex = dirname(__DIR__, 2) . '/index.php';

if (is_file($rootIndex)) {
    require_once $rootIndex;
} else {
    http_response_code(404);
    header('Content-Type: application/json');
    echo json_encode([
        'success' => false,
        'message' => 'API Entry Point not found.'
    ]);
}
