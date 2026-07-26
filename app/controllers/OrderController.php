<?php

namespace App\Controllers;

use App\Models\Order;
use App\Services\JWTService;

class OrderController
{
    private function isUnsafeLocalBackfill(array $data): bool
    {
        $source = strtolower(trim((string) ($data['source'] ?? $data['sync_source'] ?? $data['syncSource'] ?? '')));

        return $source === 'electron-local-report-backfill'
            || $source === 'local-report-backfill';
    }

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
        $startDate = $_GET['start_date'] ?? null;
        $endDate = $_GET['end_date'] ?? null;
        // If date filter is present, force status to 'settled,completed' as requested
        $status = ($startDate || $endDate) ? 'settled,completed' : ($_GET['status'] ?? null);
        success_response(Order::all($startDate, $endDate, $status));
    }

    public function nextBillNumber(): void
    {
        success_response(Order::nextBillNumber());
    }

    public function store(): void
    {
        $data = $this->attachCreator(request_json());

        if ($this->isUnsafeLocalBackfill($data)) {
            success_response(['skipped' => true], 'Unsafe local report backfill ignored.');
        }

        if (empty($data['items']) || !is_array($data['items'])) {
            error_response('Order items are required.', 422);
        }

        success_response(['id' => Order::create($data)], 'Order created.', 201);
    }

    public function saveTableOrder(): void
    {
        $data = $this->attachCreator(request_json());

        if ($this->isUnsafeLocalBackfill($data)) {
            success_response(['skipped' => true], 'Unsafe local report backfill ignored.');
        }

        $errors = validate_required($data, ['table_id']);

        if ($errors || !array_key_exists('items', $data) || !is_array($data['items'])) {
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

    public function syncFromLocal(): void
    {
        $data = $this->attachCreator(request_json());

        if (isset($data['orders']) && is_array($data['orders'])) {
            $results = Order::syncBatchFromLocal($data['orders']);
            success_response(['results' => $results], 'Batch processed.', 201);
        } else {
            if (empty($data['sqlite_uuid'])) {
                error_response('sqlite_uuid is required for sync.', 422);
            }

            $result = Order::syncFromLocal($data);

            if (!empty($result['error'])) {
                error_response($result['error'], 422);
            }

            $message = !empty($result['already_synced']) ? 'Order already synced.' : 'Order synced successfully.';
            success_response($result, $message, 201);
        }
    }
}
