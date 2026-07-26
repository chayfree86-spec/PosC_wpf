<?php

namespace App\Middleware;

use App\Models\User;
use App\Services\JWTService;

class AuthMiddleware
{
    public static function user(): array
    {
        $header = $_SERVER['HTTP_AUTHORIZATION'] ?? '';

        if (!preg_match('/Bearer\s+(.+)/', $header, $matches)) {
            error_response('Unauthorized.', 401);
        }

        $payload = JWTService::decode($matches[1]);

        if (!$payload || empty($payload['sub'])) {
            error_response('Invalid token.', 401);
        }

        $user = User::find((int) $payload['sub']);

        if (!$user || !(int) $user['is_active']) {
            error_response('User is not active.', 401);
        }

        return $user;
    }
}
