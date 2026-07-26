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

        if (User::findByEmail($data['email'])) {
            error_response('Email already exists.', 409);
        }

        $id = User::create($data);
        success_response(User::find($id), 'User created.', 201);
    }

    public function login(): void
    {
        $data = request_json();
        $client = Client::select($data['client'] ?? $_SERVER['HTTP_X_POS_CLIENT'] ?? env('POS_DEFAULT_CLIENT', 'daalroti'));

        $isMobilePinLogin = isset($data['mobile']) || isset($data['phone']) || isset($data['pin']);
        $errors = $isMobilePinLogin
            ? validate_required($data, [isset($data['phone']) ? 'phone' : 'mobile', 'pin'])
            : validate_required($data, ['email', 'password']);

        if ($errors) {
            error_response('Validation failed.', 422, $errors);
        }

        if ($isMobilePinLogin) {
            $phone = preg_replace('/\D+/', '', (string) ($data['mobile'] ?? $data['phone'] ?? ''));
            $pin = preg_replace('/\D+/', '', (string) ($data['pin'] ?? ''));

            if (strlen($phone) < 10 || strlen($pin) < 4) {
                error_response('Valid mobile number aur 4 digit PIN enter karein.', 422);
            }

            $user = User::findByPhone($phone);
            $isValid = false;

            if ($user && !empty($user['pin'])) {
                $isValid = password_verify($pin, $user['pin']);
            }

            if (!$user || !$isValid || !(int) ($user['is_active'] ?? 1)) {
                error_response('Selected client ke liye mobile number ya PIN galat hai.', 401);
            }

            unset($user['password'], $user['pin']);

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
        }

        $user = User::findByEmail($data['email']);

        if (!$user || !password_verify($data['password'], $user['password'] ?? '')) {
            error_response('Invalid email or password.', 401);
        }

        unset($user['password'], $user['pin']);

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

    public function clients(): void
    {
        success_response(Client::all());
    }

    public function updateProfile(): void
    {
        $data = request_json();
        $user = $this->resolveProfileUser($data);

        $payload = [];
        if (isset($data['name'])) {
            $payload['name'] = trim((string) $data['name']);
        }
        if (isset($data['phone'])) {
            $payload['phone'] = trim((string) $data['phone']);
        }
        if (isset($data['email'])) {
            $payload['email'] = trim((string) $data['email']);
        }
        if (isset($data['pin']) && $data['pin'] !== '') {
            $pin = preg_replace('/\D+/', '', (string) $data['pin']);
            if (strlen($pin) !== 4) {
                error_response('PIN must be 4 digits.', 422);
            }
            $payload['pin'] = $pin;
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
