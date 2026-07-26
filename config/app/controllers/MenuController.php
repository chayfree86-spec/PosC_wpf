<?php

namespace App\Controllers;

use App\Models\Category;
use App\Models\MenuItem;

class MenuController
{
    public function categories(): void
    {
        success_response(Category::all());
    }

    public function storeCategory(): void
    {
        $data = request_json();
        $errors = validate_required($data, ['name']);

        if ($errors) {
            error_response('Validation failed.', 422, $errors);
        }

        success_response(['id' => Category::create($data)], 'Category created.', 201);
    }

    public function updateCategory(string $id): void
    {
        $data = request_json();
        $errors = validate_required($data, ['name']);

        if ($errors) {
            error_response('Validation failed.', 422, $errors);
        }

        Category::update((int) $id, $data);
        success_response(null, 'Category updated.');
    }

    public function deleteCategory(string $id): void
    {
        Category::delete((int) $id);
        success_response(null, 'Category deleted.');
    }

    public function items(): void
    {
        success_response(MenuItem::all());
    }

    public function storeItem(): void
    {
        $data = request_json();
        $errors = validate_required($data, ['name', 'category_id', 'price']);

        if ($errors) {
            error_response('Validation failed.', 422, $errors);
        }

        success_response(['id' => MenuItem::create($data)], 'Menu item created.', 201);
    }

    public function updateItem(string $id): void
    {
        $data = request_json();
        $errors = validate_required($data, ['name', 'category_id', 'price']);

        if ($errors) {
            error_response('Validation failed.', 422, $errors);
        }

        $itemId = (int) $id;
        MenuItem::update($itemId, $data);
        success_response(MenuItem::find($itemId), 'Menu item updated.');
    }

    public function deleteItem(string $id): void
    {
        MenuItem::delete((int) $id);
        success_response(null, 'Menu item deleted.');
    }
}
