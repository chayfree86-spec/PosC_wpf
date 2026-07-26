<?php

namespace App\Controllers;

use App\Models\Setting;

class SettingController
{
    public function index(): void
    {
        success_response(Setting::all());
    }

    public function update(string $key): void
    {
        $data = request_json();
        success_response(Setting::put($key, $data['value'] ?? null), 'Setting saved.');
    }
}
