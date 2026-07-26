<?php

namespace App\Services;

class JWTService
{
    public static function encode(array $payload): string
    {
        $config = require dirname(__DIR__, 2) . '/config/app.php';
        $payload['iat'] = time();
        $payload['exp'] = time() + (int) $config['jwt_ttl'];

        $header = ['typ' => 'JWT', 'alg' => 'HS256'];
        $segments = [
            self::base64UrlEncode(json_encode($header)),
            self::base64UrlEncode(json_encode($payload)),
        ];
        $signature = hash_hmac('sha256', implode('.', $segments), $config['jwt_secret'], true);
        $segments[] = self::base64UrlEncode($signature);

        return implode('.', $segments);
    }

    public static function decode(string $token): ?array
    {
        $config = require dirname(__DIR__, 2) . '/config/app.php';
        $parts = explode('.', $token);

        if (count($parts) !== 3) {
            return null;
        }

        [$header, $payload, $signature] = $parts;
        $expected = self::base64UrlEncode(hash_hmac('sha256', "$header.$payload", $config['jwt_secret'], true));

        if (!hash_equals($expected, $signature)) {
            return null;
        }

        $decoded = json_decode(self::base64UrlDecode($payload), true);

        if (!is_array($decoded) || (($decoded['exp'] ?? 0) < time())) {
            return null;
        }

        return $decoded;
    }

    private static function base64UrlEncode(string $value): string
    {
        return rtrim(strtr(base64_encode($value), '+/', '-_'), '=');
    }

    private static function base64UrlDecode(string $value): string
    {
        return base64_decode(strtr($value, '-_', '+/')) ?: '';
    }
}
