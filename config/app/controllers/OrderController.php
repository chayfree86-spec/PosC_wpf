<?php

namespace App\Controllers;

use App\Models\Order;
use App\Services\JWTService;

class OrderController
{
    private function attachCreator(array $data): array
    {
        if (!empty($data['created_by'])) {
            return $data;
        }

        $header = $_SERVER['HTTP_AUTHORIZATION'] ?? '';
        if (preg_match('/Bearer\s+(.+)/', $header, $matches)) {
            $payload = JWTService::decode($matches[1]);
            if (!empty($payload['sub'])) {
                $data['created_by'] = (int) $payload['sub'];
            }
        }

        return $data;
    }

    public function index(): void
    {
        success_response(Order::all());
    }

    public function nextBillNumber(): void
    {
        success_response(Order::nextBillNumber());
    }

    public function store(): void
    {
        $data = $this->attachCreator(request_json());

        if (empty($data['items']) || !is_array($data['items'])) {
            error_response('Order items are required.', 422);
        }

        success_response(['id' => Order::create($data)], 'Order created.', 201);
    }

    public function saveTableOrder(): void
    {
        $data = $this->attachCreator(request_json());
        $errors = validate_required($data, ['table_id', 'items']);

        if ($errors || !is_array($data['items'])) {
            error_response('Validation failed.', 422, $errors ?: ['items' => 'Order items must be an array.']);
        }

        success_response(Order::saveTableOrder($data), 'Table order saved.', 201);
    }

    public function status(string $id): void
    {
        $data = request_json();
        $errors = validate_required($data, ['order_status']);

        if ($errors) {
            error_response('Validation failed.', 422, $errors);
        }

        Order::updateStatus((int) $id, $data['order_status'], $data['changed_by'] ?? null);
        success_response(null, 'Order status updated.');
    }

    public function destroy(string $id): void
    {
        if (!Order::delete((int) $id)) {
            error_response('Order not found.', 404);
        }

        success_response(null, 'Order deleted.');
    }
}
