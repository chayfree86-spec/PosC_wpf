<?php

namespace App\Middleware;

class RoleMiddleware
{
    public static function require(array $roles): array
    {
        $user = AuthMiddleware::user();

        if (!in_array($user['role'], $roles, true)) {
            error_response('Forbidden.', 403);
        }

        return $user;
    }
}
