<?php

namespace App\Controllers;

use App\Models\GalleryImage;

class GalleryController
{
    public function index(): void
    {
        success_response(GalleryImage::all());
    }

    public function store(): void
    {
        $data = request_json();
        $errors = validate_required($data, ['url']);

        if ($errors) {
            error_response('Validation failed.', 422, $errors);
        }

        success_response(['id' => GalleryImage::create($data)], 'Gallery image saved.', 201);
    }

    public function update(string $id): void
    {
        $data = request_json();
        $errors = validate_required($data, ['url']);

        if ($errors) {
            error_response('Validation failed.', 422, $errors);
        }

        GalleryImage::update((int) $id, $data);
        success_response(null, 'Gallery image updated.');
    }

    public function destroy(string $id): void
    {
        GalleryImage::delete((int) $id);
        success_response(null, 'Gallery image deleted.');
    }
}

