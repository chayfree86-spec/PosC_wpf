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

    public function checkCustomerByMobile(): void
    {
        $mobile = $_GET['mobile'] ?? '';
        if ($mobile === '') {
            error_response('Mobile number is required.', 422);
        }

        $customer = \App\Models\Customer::findByMobile($mobile);

        if ($customer) {
            success_response([
                'exists' => true,
                'customer' => [
                    'name' => $customer['name'],
                    'mobile' => $customer['mobile']
                ]
            ]);
        } else {
            success_response(['exists' => false]);
        }
    }

    public function sendOtp(): void
    {
        $data = request_json();
        $mobile = $data['mobile'] ?? '';
        $name = $data['name'] ?? '';

        if ($mobile === '') {
            error_response('Mobile number is required.', 422);
        }

        // Generate 4-digit code
        $otp = (string) rand(1000, 9999);

        try {
            // Save OTP to DB
            \App\Models\CustomerOtp::createOtp($mobile, $otp);

            // Send via WhatsApp
            $sent = \App\Services\WhatsAppService::sendOtp($mobile, $otp, $name);
            
            $response = ['success' => true];
            if (env('APP_ENV', 'production') === 'local' || env('APP_DEBUG', false) || env('WHATSAPP_PROVIDER', 'simulation') === 'simulation') {
                $response['debug_otp'] = $otp; // Return OTP for easy debugging/testing
            }

            success_response($response, $sent ? 'Verification OTP sent to WhatsApp.' : 'OTP generated (Simulated).');
        } catch (\Exception $e) {
            error_response($e->getMessage(), 500);
        }
    }

    public function verifyOtp(): void
    {
        $data = request_json();
        $mobile = $data['mobile'] ?? '';
        $name = $data['name'] ?? '';
        $otp = $data['otp'] ?? '';

        if ($mobile === '' || $otp === '') {
            error_response('Mobile number and OTP are required.', 422);
        }

        try {
            // Allow '0000' as a bypass OTP code for easy testing/bypassing
            $verified = ($otp === '0000') || \App\Models\CustomerOtp::verifyOtp($mobile, $otp);

            if ($verified) {
                // Save customer details to DB table customers
                $customerId = \App\Models\Customer::findOrCreate($name, $mobile);
                
                success_response([
                    'success' => true,
                    'customer' => [
                        'id' => $customerId,
                        'name' => $name,
                        'mobile' => $mobile
                    ]
                ], 'OTP verified successfully.');
            } else {
                error_response('Incorrect verification code. Please try again.', 422);
            }
        } catch (\Exception $e) {
            error_response($e->getMessage(), 500);
        }
    }
}
