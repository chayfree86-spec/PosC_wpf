<?php

return [
    'name' => env('APP_NAME', 'POS QR System'),
    'env' => env('APP_ENV', 'production'),
    'debug' => filter_var(env('APP_DEBUG', false), FILTER_VALIDATE_BOOLEAN),
    'url' => env('APP_URL', ''),
    'jwt_secret' => env('JWT_SECRET', 'change-this-secret-key'),
    'jwt_ttl' => (int) env('JWT_TTL', 86400),
];
