<?php

namespace App\Controllers;

use App\Models\Table;

class TableController
{
    public function index(): void
    {
        success_response(Table::all());
    }

    public function store(): void
    {
        $data = request_json();
        $errors = validate_required($data, ['table_number']);

        if ($errors) {
            error_response('Validation failed.', 422, $errors);
        }

        success_response(['id' => Table::create($data)], 'Table created.', 201);
    }

    public function update(string $id): void
    {
        $data = request_json();
        $errors = validate_required($data, ['table_number']);

        if ($errors) {
            error_response('Validation failed.', 422, $errors);
        }

        Table::update((int) $id, $data);
        success_response(null, 'Table updated.');
    }

    public function destroy(string $id): void
    {
        Table::delete((int) $id);
        success_response(null, 'Table deleted.');
    }

    public function status(string $id): void
    {
        $data = request_json();
        $errors = validate_required($data, ['status']);

        if ($errors) {
            error_response('Validation failed.', 422, $errors);
        }

        $tableId = (int) $id;
        Table::updateStatus(
            $tableId,
            $data['status'],
            (float) ($data['amount'] ?? 0),
            isset($data['order_timestamp']) ? (int) $data['order_timestamp'] : null
        );

        success_response(Table::find($tableId), 'Table status updated.');
    }
}
