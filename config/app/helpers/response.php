<?php

function json_response(mixed $data = null, int $status = 200): void
{
    http_response_code($status);
    header('Content-Type: application/json; charset=utf-8');
    echo json_encode($data, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
    exit;
}

function success_response(mixed $data = null, string $message = 'OK', int $status = 200): void
{
    json_response([
        'success' => true,
        'message' => $message,
        'data' => $data,
    ], $status);
}

function error_response(string $message, int $status = 400, mixed $errors = null): void
{
    json_response([
        'success' => false,
        'message' => $message,
        'errors' => $errors,
    ], $status);
}

function request_json(): array
{
    $raw = file_get_contents('php://input') ?: '';
    if ($raw === '') {
        return $_POST ?: [];
    }

    $decoded = json_decode($raw, true);
    return is_array($decoded) ? $decoded : [];
}
