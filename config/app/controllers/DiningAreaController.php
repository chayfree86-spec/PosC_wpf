<?php

namespace App\Controllers;

use App\Models\DiningArea;

class DiningAreaController
{
    public function index(): void
    {
        success_response(DiningArea::all());
    }

    public function store(): void
    {
        $data = request_json();
        $errors = validate_required($data, ['name']);

        if ($errors) {
            error_response('Validation failed.', 422, $errors);
        }

        success_response(['id' => DiningArea::create($data)], 'Dining area created.', 201);
    }

    public function update(string $id): void
    {
        $data = request_json();
        $errors = validate_required($data, ['name']);

        if ($errors) {
            error_response('Validation failed.', 422, $errors);
        }

        DiningArea::update((int) $id, $data);
        success_response(null, 'Dining area updated.');
    }

    public function destroy(string $id): void
    {
        DiningArea::delete((int) $id);
        success_response(null, 'Dining area deleted.');
    }
}
