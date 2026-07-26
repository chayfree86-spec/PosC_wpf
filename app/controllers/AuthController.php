<?php

namespace App\Controllers;

use App\Middleware\AuthMiddleware;
use App\Models\Client;
use App\Models\User;
use App\Services\JWTService;

class AuthController
{
    public function register(): void
    {
        $data = request_json();
        if (!empty($data['client'])) {
            Client::select($data['client']);
        }
        $errors = validate_required($data, ['name', 'email', 'password']);

        if ($errors) {
            error_response('Validation failed.', 422, $errors);
        }

        // Sanitize free-text fields (same stored-XSS guard as updateProfile).
        $data['name'] = self::sanitizeProfileText((string) $data['name']);
        $data['email'] = self::sanitizeProfileText((string) $data['email'], 150);
        if ($data['name'] === '') {
            error_response('Name is required.', 422);
        }
        if (!filter_var($data['email'], FILTER_VALIDATE_EMAIL)) {
            error_response('Invalid email address.', 422);
        }
        if (isset($data['phone'])) {
            $data['phone'] = substr(preg_replace('/[^0-9+\-\s()]/', '', (string) $data['phone']) ?? '', 0, 20);
        }

        if (User::findByEmail($data['email'])) {
            error_response('Email already exists.', 409);
        }

        $id = User::create($data);
        success_response(User::find($id), 'User created.', 201);
    }

    public function login(): void
    {
        $data = request_json();
        $requestedClientSlug = $data['client'] ?? $_SERVER['HTTP_X_POS_CLIENT'] ?? env('POS_DEFAULT_CLIENT', 'daalroti');
        
        $isMobilePinLogin = isset($data['mobile']) || isset($data['phone']) || isset($data['pin']);
        $errors = $isMobilePinLogin
            ? validate_required($data, [isset($data['phone']) ? 'phone' : 'mobile', 'pin'])
            : validate_required($data, ['email', 'password']);

        if ($errors) {
            error_response('Validation failed.', 422, $errors);
        }

        $allClients = Client::all();
        $phone = $isMobilePinLogin ? preg_replace('/\D+/', '', (string) ($data['mobile'] ?? $data['phone'] ?? '')) : '';
        $pin = $isMobilePinLogin ? preg_replace('/\D+/', '', (string) ($data['pin'] ?? '')) : '';
        $email = !$isMobilePinLogin ? trim((string) ($data['email'] ?? '')) : '';
        
        $matchedClient = null;
        $matchedUser = null;
        
        // 1. Try the requested client first
        try {
            $client = Client::select($requestedClientSlug);
            if ($isMobilePinLogin) {
                if (strlen($phone) >= 10 && strlen($pin) >= 4) {
                    $user = User::findByPhone($phone);
                    if ($user && password_verify($pin, $user['pin'] ?? '') && (int) ($user['is_active'] ?? 1)) {
                        $matchedClient = $client;
                        $matchedUser = $user;
                    }
                }
            } else {
                $user = User::findByEmail($email);
                if ($user && password_verify($data['password'], $user['password'] ?? '') && (int) ($user['is_active'] ?? 1)) {
                    $matchedClient = $client;
                    $matchedUser = $user;
                }
            }
        } catch (\Throwable $e) {
            // Ignore error and proceed to scan others
        }
        
        // 2. If not matched, scan all other active clients!
        if (!$matchedUser) {
            foreach ($allClients as $c) {
                if ($c['slug'] === $requestedClientSlug) {
                    continue; // Already tried
                }
                
                try {
                    Client::select($c['slug']);
                    if ($isMobilePinLogin) {
                        if (strlen($phone) >= 10 && strlen($pin) >= 4) {
                            $user = User::findByPhone($phone);
                            if ($user && password_verify($pin, $user['pin'] ?? '') && (int) ($user['is_active'] ?? 1)) {
                                $matchedClient = $c;
                                $matchedUser = $user;
                                break;
                            }
                        }
                    } else {
                        $user = User::findByEmail($email);
                        if ($user && password_verify($data['password'], $user['password'] ?? '') && (int) ($user['is_active'] ?? 1)) {
                            $matchedClient = $c;
                            $matchedUser = $user;
                            break;
                        }
                    }
                } catch (\Throwable $e) {
                    continue;
                }
            }
        }
        
        // 3. If still not matched, return error
        if (!$matchedUser) {
            if ($isMobilePinLogin) {
                error_response('Selected client ke liye mobile number ya PIN galat hai.', 401);
            } else {
                error_response('Invalid email or password.', 401);
            }
        }
        
        // 4. Select the matched client and return successful login response
        $client = Client::select($matchedClient['slug']);
        $user = $matchedUser;
        
        unset($user['password'], $user['pin']);
        
        if ($isMobilePinLogin) {
            success_response([
                'token' => JWTService::encode([
                    'sub' => $user['id'] ?? 0,
                    'phone' => substr($phone, -10),
                    'role' => $user['role'] ?? 'manager',
                    'client_id' => (int) $client['id'],
                    'client' => $client['slug'],
                ]),
                'client' => $client,
                'user' => $user,
            ], 'Login successful.');
        } else {
            success_response([
                'token' => JWTService::encode([
                    'sub' => $user['id'],
                    'role' => $user['role'],
                    'client_id' => (int) $client['id'],
                    'client' => $client['slug'],
                ]),
                'client' => $client,
                'user' => $user,
            ], 'Login successful.');
        }
    }

    public function clients(): void
    {
        success_response(Client::all());
    }

    /**
     * Sanitize a free-text profile field: strip HTML tags, control chars and
     * angle brackets so stored-XSS payloads (e.g. <img onerror=...>) can never
     * be persisted, and cap the length. A stored payload was found in the
     * users.name column injected via this endpoint.
     */
    private static function sanitizeProfileText(string $value, int $maxLen = 100): string
    {
        $value = strip_tags($value);
        $value = str_replace(['<', '>', '"', "\0"], '', $value);
        $value = preg_replace('/[\x00-\x1F\x7F]/u', '', $value) ?? '';
        $value = trim($value);
        if (function_exists('mb_substr')) {
            return mb_substr($value, 0, $maxLen);
        }
        return substr($value, 0, $maxLen);
    }

    public function updateProfile(): void
    {
        $data = request_json();
        $user = $this->resolveProfileUser($data);

        $payload = [];
        if (isset($data['name'])) {
            $payload['name'] = self::sanitizeProfileText((string) $data['name']);
        }
        if (isset($data['phone'])) {
            // Phone: digits, +, spaces, dashes, parens only.
            $payload['phone'] = substr(preg_replace('/[^0-9+\-\s()]/', '', (string) $data['phone']) ?? '', 0, 20);
        }
        if (isset($data['email'])) {
            $email = self::sanitizeProfileText((string) $data['email'], 150);
            if ($email !== '' && !filter_var($email, FILTER_VALIDATE_EMAIL)) {
                error_response('Invalid email address.', 422);
            }
            $payload['email'] = $email;
        }
        if (isset($data['pin']) && $data['pin'] !== '') {
            $rawPin = (string) $data['pin'];
            // Strip non-digits — handles masked placeholders like "••••" or garbled chars
            $pin = preg_replace('/\D+/', '', $rawPin);
            // If strip returns empty (placeholder was submitted), skip PIN update
            if ($pin === '') {
                // No actual PIN change — skip silently
            } elseif (strlen($pin) !== 4) {
                error_response('PIN must be 4 digits.', 422);
            } else {
                $payload['pin'] = $pin;
            }
        }

        if (empty($payload['name'] ?? $user['name'] ?? '')) {
            error_response('Name is required.', 422);
        }

        $updated = User::updateProfile((int) $user['id'], $payload);
        unset($updated['password'], $updated['pin']);

        success_response([
            'user' => $updated,
        ], 'Profile updated.');
    }

    public function profile(): void
    {
        $user = $this->resolveProfileUser([]);
        unset($user['password'], $user['pin']);
        success_response([
            'user' => $user,
        ]);
    }

    private function resolveProfileUser(array $data): array
    {
        $header = $_SERVER['HTTP_AUTHORIZATION'] ?? '';

        if (preg_match('/Bearer\s+(.+)/', $header, $matches)) {
            $payload = JWTService::decode($matches[1]);

            if (!empty($payload['sub'])) {
                $user = User::find((int) $payload['sub']);
                if ($user && (int) ($user['is_active'] ?? 1)) {
                    return $user;
                }
            }

            if (!empty($payload['phone'])) {
                $user = User::findByPhone((string) $payload['phone']);
                if ($user && (int) ($user['is_active'] ?? 1)) {
                    return $user;
                }
            }
        }

        $phone = (string) ($data['current_phone'] ?? $data['phone'] ?? '');
        if ($phone !== '') {
            $user = User::findByPhone($phone);
            if ($user && (int) ($user['is_active'] ?? 1)) {
                return $user;
            }
        }

        $email = trim((string) ($data['current_email'] ?? $data['email'] ?? ''));
        if ($email !== '') {
            $user = User::findByEmail($email);
            if ($user && (int) ($user['is_active'] ?? 1)) {
                return $user;
            }
        }

        error_response('User not found for profile update.', 404);
    }
}
