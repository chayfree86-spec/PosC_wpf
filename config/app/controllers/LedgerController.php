<?php

namespace App\Controllers;

use App\Models\Ledger;
use App\Services\JWTService;

class LedgerController
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
        success_response(Ledger::summary());
    }

    public function storeCustomer(): void
    {
        try {
            success_response(Ledger::createCustomer($this->attachCreator(request_json())), 'Customer saved.', 201);
        } catch (\InvalidArgumentException $error) {
            error_response($error->getMessage(), 422);
        }
    }

    public function updateCustomer(string $id): void
    {
        try {
            success_response(Ledger::updateCustomer((int) $id, request_json()), 'Customer updated.');
        } catch (\InvalidArgumentException $error) {
            error_response($error->getMessage(), 422);
        } catch (\RuntimeException $error) {
            error_response($error->getMessage(), 404);
        }
    }

    public function deleteCustomer(string $id): void
    {
        try {
            Ledger::deleteCustomer((int) $id);
            success_response(['id' => (int) $id], 'Customer deleted.');
        } catch (\RuntimeException $error) {
            error_response($error->getMessage(), 404);
        }
    }

    public function storeEntry(string $id): void
    {
        try {
            $data = $this->attachCreator(request_json());
            success_response(Ledger::createEntry((int) $id, $data), 'Ledger entry saved.', 201);
        } catch (\InvalidArgumentException $error) {
            error_response($error->getMessage(), 422);
        } catch (\RuntimeException $error) {
            error_response($error->getMessage(), 404);
        }
    }

    public function updateEntry(string $id): void
    {
        try {
            success_response(Ledger::updateEntry((int) $id, request_json()), 'Ledger entry updated.');
        } catch (\InvalidArgumentException $error) {
            error_response($error->getMessage(), 422);
        } catch (\RuntimeException $error) {
            error_response($error->getMessage(), 404);
        }
    }

    public function deleteEntry(string $id): void
    {
        try {
            Ledger::deleteEntry((int) $id);
            success_response(['id' => (int) $id], 'Ledger entry deleted.');
        } catch (\RuntimeException $error) {
            error_response($error->getMessage(), 404);
        }
    }
}
