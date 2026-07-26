<?php

namespace App\Controllers;

class QzController
{
    public function certificate(): void
    {
        $certificate = $this->readConfiguredValue(
            'QZ_CERTIFICATE',
            'QZ_CERTIFICATE_PATH',
            'config/qz/qz-certificate.pem'
        );

        if (trim($certificate) === '') {
            http_response_code(404);
            header('Content-Type: text/plain; charset=utf-8');
            echo 'QZ certificate is not configured. Upload config/qz/qz-certificate.pem or set QZ_CERTIFICATE_PATH.';
            exit;
        }

        http_response_code(200);
        header('Content-Type: text/plain; charset=utf-8');
        echo $certificate;
        exit;
    }

    public function sign(): void
    {
        if (($_SERVER['REQUEST_METHOD'] ?? 'GET') !== 'POST') {
            if (isset($_GET['raw']) && (string) $_GET['raw'] === '1') {
                http_response_code(204);
                header('Content-Type: text/plain; charset=utf-8');
                exit;
            }

            error_response('QZ signing requires POST.', 405);
        }

        $data = request_json();
        $toSign = (string) ($data['data'] ?? $data['request'] ?? $data['toSign'] ?? '');

        if ($toSign === '') {
            error_response('QZ signing payload is required.', 422);
        }

        $privateKey = $this->readConfiguredValue(
            'QZ_PRIVATE_KEY',
            'QZ_PRIVATE_KEY_PATH',
            'config/qz/qz-private-key.pem'
        );
        $passphrase = (string) env('QZ_PRIVATE_KEY_PASSPHRASE', '');

        if (trim($privateKey) === '') {
            error_response('QZ private key is not configured.', 404);
        }

        $signature = $this->signPayload($toSign, $privateKey, $passphrase);
        if ($signature === '') {
            error_response('QZ signing failed.', 500);
        }

        if (isset($_GET['raw']) && (string) $_GET['raw'] === '1') {
            http_response_code(200);
            header('Content-Type: text/plain; charset=utf-8');
            echo $signature;
            exit;
        }

        json_response([
            'success' => true,
            'ok' => true,
            'signature' => $signature,
        ]);
    }

    private function signPayload(string $toSign, string $privateKey, string $passphrase): string
    {
        if (function_exists('openssl_pkey_get_private') && function_exists('openssl_sign')) {
            $key = $passphrase !== ''
                ? openssl_pkey_get_private($privateKey, $passphrase)
                : openssl_pkey_get_private($privateKey);

            if ($key === false) {
                error_response('QZ private key could not be loaded.', 500);
            }

            $signature = '';
            $ok = openssl_sign($toSign, $signature, $key, 'sha512');
            return $ok ? base64_encode($signature) : '';
        }

        if (PHP_OS_FAMILY === 'Windows' && $passphrase === '') {
            return $this->signPayloadWithPowerShell($toSign, $privateKey);
        }

        error_response('QZ signing requires PHP OpenSSL or Windows PowerShell fallback.', 500);
    }

    private function signPayloadWithPowerShell(string $toSign, string $privateKey): string
    {
        $keyFile = tempnam(sys_get_temp_dir(), 'pos_qz_key_');
        $dataFile = tempnam(sys_get_temp_dir(), 'pos_qz_data_');
        if ($keyFile === false || $dataFile === false) {
            error_response('Unable to create QZ signing buffer.', 500);
        }

        file_put_contents($keyFile, $privateKey);
        file_put_contents($dataFile, $toSign);

        $script = <<<'PS1'
$ErrorActionPreference = 'Stop'
$pem = Get-Content -LiteralPath $env:POS_QZ_KEY_FILE -Raw
$data = [System.IO.File]::ReadAllText($env:POS_QZ_DATA_FILE, [System.Text.Encoding]::UTF8)
$rsa = [System.Security.Cryptography.RSA]::Create()
$rsa.ImportFromPem($pem)
$bytes = [System.Text.Encoding]::UTF8.GetBytes($data)
$signature = $rsa.SignData(
    $bytes,
    [System.Security.Cryptography.HashAlgorithmName]::SHA512,
    [System.Security.Cryptography.RSASignaturePadding]::Pkcs1
)
[Convert]::ToBase64String($signature)
PS1;

        try {
            $result = $this->runPowerShell($script, [
                'POS_QZ_KEY_FILE' => $keyFile,
                'POS_QZ_DATA_FILE' => $dataFile,
            ]);
        } finally {
            @unlink($keyFile);
            @unlink($dataFile);
        }

        return $result['ok'] ? trim($result['output']) : '';
    }

    private function runPowerShell(string $script, array $env = []): array
    {
        $scriptFile = tempnam(sys_get_temp_dir(), 'pos_qz_ps_');
        if ($scriptFile === false) {
            return ['ok' => false, 'output' => '', 'error' => 'Unable to create PowerShell script.'];
        }

        $scriptPath = $scriptFile . '.ps1';
        @rename($scriptFile, $scriptPath);
        file_put_contents($scriptPath, $script);

        $descriptorSpec = [
            1 => ['pipe', 'w'],
            2 => ['pipe', 'w'],
        ];

        $process = proc_open(
            [$this->powerShellBinary(), '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $scriptPath],
            $descriptorSpec,
            $pipes,
            null,
            array_merge(getenv() ?: [], $env)
        );

        if (!is_resource($process)) {
            @unlink($scriptPath);
            return ['ok' => false, 'output' => '', 'error' => 'Unable to start PowerShell.'];
        }

        $output = stream_get_contents($pipes[1]) ?: '';
        $error = stream_get_contents($pipes[2]) ?: '';
        fclose($pipes[1]);
        fclose($pipes[2]);

        $exitCode = proc_close($process);
        @unlink($scriptPath);

        if ($exitCode !== 0) {
            @file_put_contents(
                __DIR__ . '/../../logs/qz-signing-powershell-error.log',
                '[' . date('Y-m-d H:i:s') . "] exit={$exitCode}\n" . trim($error . "\n" . $output) . "\n\n",
                FILE_APPEND
            );
        }

        return [
            'ok' => $exitCode === 0,
            'output' => trim($output),
            'error' => trim($error),
        ];
    }

    private function powerShellBinary(): string
    {
        $pwsh = trim((string) shell_exec('where pwsh 2>NUL'));
        if ($pwsh !== '') {
            $first = preg_split('/\r?\n/', $pwsh)[0] ?? 'pwsh';
            return $first !== '' ? $first : 'pwsh';
        }

        return 'powershell.exe';
    }

    private function readConfiguredValue(string $valueKey, string $pathKey, string $defaultPath = ''): string
    {
        $value = (string) env($valueKey, '');
        if (trim($value) !== '') {
            return str_replace(["\\n", "\\r"], ["\n", "\r"], $value);
        }

        $path = (string) env($pathKey, '');
        if ($path === '' && $defaultPath !== '') {
            $path = $defaultPath;
        }

        if ($path === '') {
            return '';
        }

        if (!$this->isAbsolutePath($path)) {
            $path = dirname(__DIR__, 2) . DIRECTORY_SEPARATOR . ltrim($path, "\\/");
        }

        if (!is_file($path) || !is_readable($path)) {
            return '';
        }

        $contents = file_get_contents($path);
        return is_string($contents) ? $contents : '';
    }

    private function isAbsolutePath(string $path): bool
    {
        return str_starts_with($path, '/')
            || (bool) preg_match('/^[A-Za-z]:[\\\\\\/]/', $path)
            || str_starts_with($path, '\\\\');
    }
}
