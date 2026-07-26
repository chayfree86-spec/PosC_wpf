<?php

namespace App\Controllers;

use App\Models\GstRate;

class GstController
{
    public function index(): void
    {
        success_response(GstRate::all());
    }

    public function store(): void
    {
        $data = request_json();
        $errors = validate_required($data, ['name', 'rate_percent']);

        if ($errors) {
            error_response('Validation failed.', 422, $errors);
        }

        success_response(['id' => GstRate::create($data)], 'GST rate created.', 201);
    }

    public function update(string $id): void
    {
        $data = request_json();
        $errors = validate_required($data, ['name', 'rate_percent']);

        if ($errors) {
            error_response('Validation failed.', 422, $errors);
        }

        $rateId = (int) $id;
        GstRate::update($rateId, $data);
        success_response(GstRate::find($rateId), 'GST rate updated.');
    }

    public function destroy(string $id): void
    {
        GstRate::delete((int) $id);
        success_response(null, 'GST rate deleted.');
    }
}
