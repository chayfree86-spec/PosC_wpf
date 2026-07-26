<?php

namespace App\Services;

class WhatsAppService
{
    public static function sendOtp(string $mobile, string $otp, string $name): bool
    {
        $provider = env('WHATSAPP_PROVIDER', 'simulation');
        $apiUrl = env('WHATSAPP_API_URL', '');
        $apiKey = env('WHATSAPP_API_KEY', '');
        $token = env('WHATSAPP_TOKEN', '');
        $template = env('WHATSAPP_TEMPLATE_NAME', '');
        $senderId = env('WHATSAPP_SENDER_ID', '');

        $message = "Your verification OTP code is: {$otp}. It is valid for 10 minutes.";

        if ($provider === 'simulation') {
            // Write to a log file inside the logs directory
            $logDir = dirname(__DIR__, 2) . '/logs';
            if (!is_dir($logDir)) {
                mkdir($logDir, 0755, true);
            }
            $logFile = $logDir . '/whatsapp_otps.log';
            $logEntry = sprintf(
                "[%s] OTP: %s | Mobile: %s | Name: %s\n",
                date('Y-m-d H:i:s'),
                $otp,
                $mobile,
                $name
            );
            file_put_contents($logFile, $logEntry, FILE_APPEND);
            return true;
        }

        if ($provider === 'fast2sms' && $apiKey) {
            // Send free/trial SMS via Fast2SMS (using Quick SMS route 'q' for testing without DLT template)
            $url = "https://www.fast2sms.com/dev/bulkV2";
            $payload = [
                'message' => $message,
                'route' => 'q',
                'numbers' => $mobile
            ];

            $ch = curl_init($url);
            curl_setopt($ch, CURLOPT_RETURNTRANSFER, true);
            curl_setopt($ch, CURLOPT_POST, true);
            curl_setopt($ch, CURLOPT_POSTFIELDS, json_encode($payload));
            curl_setopt($ch, CURLOPT_HTTPHEADER, [
                'authorization: ' . $apiKey,
                'accept: */*',
                'cache-control: no-cache',
                'content-type: application/json'
            ]);

            $response = curl_exec($ch);
            curl_close($ch);
            return $response !== false;
        }

        // If provider is Wati, Meta, or generic API
        if ($provider === 'wati' && $apiUrl && $token) {
            // Call WATI API
            $url = rtrim($apiUrl, '/') . '/api/v1/sendTemplateMessage?whatsappNumber=' . urlencode($mobile);
            $payload = [
                'template_name' => $template ?: 'otp_verification',
                'broadcast_name' => 'OTP Verification',
                'parameters' => [
                    ['name' => 'otp', 'value' => $otp],
                    ['name' => 'name', 'value' => $name]
                ]
            ];
            
            $ch = curl_init($url);
            curl_setopt($ch, CURLOPT_RETURNTRANSFER, true);
            curl_setopt($ch, CURLOPT_POST, true);
            curl_setopt($ch, CURLOPT_POSTFIELDS, json_encode($payload));
            curl_setopt($ch, CURLOPT_HTTPHEADER, [
                'Authorization: Bearer ' . $token,
                'Content-Type: application/json'
            ]);
            
            $response = curl_exec($ch);
            curl_close($ch);
            return $response !== false;
        }

        if ($provider === 'meta' && $senderId && $token) {
            // Call Meta WhatsApp Business Cloud API
            $url = "https://graph.facebook.com/v17.0/{$senderId}/messages";
            $payload = [
                'messaging_product' => 'whatsapp',
                'to' => $mobile,
                'type' => 'template',
                'template' => [
                    'name' => $template ?: 'otp_verification',
                    'language' => ['code' => 'en_US'],
                    'components' => [
                        [
                            'type' => 'body',
                            'parameters' => [
                                ['type' => 'text', 'text' => $otp]
                            ]
                        ]
                    ]
                ]
            ];

            $ch = curl_init($url);
            curl_setopt($ch, CURLOPT_RETURNTRANSFER, true);
            curl_setopt($ch, CURLOPT_POST, true);
            curl_setopt($ch, CURLOPT_POSTFIELDS, json_encode($payload));
            curl_setopt($ch, CURLOPT_HTTPHEADER, [
                'Authorization: Bearer ' . $token,
                'Content-Type: application/json'
            ]);
            
            $response = curl_exec($ch);
            curl_close($ch);
            return $response !== false;
        }

        // Default generic HTTP request if URL and API Key is set
        if ($apiUrl && ($apiKey || $token)) {
            $ch = curl_init($apiUrl);
            curl_setopt($ch, CURLOPT_RETURNTRANSFER, true);
            curl_setopt($ch, CURLOPT_POST, true);
            
            $payload = [
                'mobile' => $mobile,
                'message' => $message,
                'otp' => $otp,
                'name' => $name
            ];
            
            curl_setopt($ch, CURLOPT_POSTFIELDS, json_encode($payload));
            curl_setopt($ch, CURLOPT_HTTPHEADER, [
                'Authorization: Bearer ' . ($token ?: $apiKey),
                'Content-Type: application/json'
            ]);
            
            $response = curl_exec($ch);
            curl_close($ch);
            return $response !== false;
        }

        return false;
    }
}
