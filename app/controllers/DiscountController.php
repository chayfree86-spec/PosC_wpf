<?php

namespace App\Controllers;

use App\Models\Discount;

class DiscountController
{
    public function index(): void
    {
        success_response(Discount::all());
    }

    public function show(string $id): void
    {
        $discount = Discount::find((int) $id);
        if (!$discount) {
            error_response('Discount not found.', 404);
            return;
        }
        success_response($discount);
    }

    public function store(): void
    {
        $data = request_json();
        if (empty($data['name'])) {
            error_response('Discount name is required.', 422);
            return;
        }

        $id = Discount::create($data);
        success_response(['id' => $id], 'Discount created successfully.', 201);
    }

    public function update(string $id): void
    {
        $data = request_json();
        $success = Discount::update((int) $id, $data);
        
        if ($success) {
            success_response(null, 'Discount updated successfully.');
        } else {
            error_response('Failed to update discount.');
        }
    }

    public function destroy(string $id): void
    {
        $success = Discount::delete((int) $id);
        if ($success) {
            success_response(null, 'Discount deleted successfully.');
        } else {
            error_response('Failed to delete discount.');
        }
    }
}
