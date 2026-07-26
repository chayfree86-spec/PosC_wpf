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

    public function transliterate(): void
    {
        $text = $_GET['text'] ?? '';
        if ($text === '') {
            success_response([]);
            return;
        }

        $url = 'https://inputtools.google.com/request?text=' . urlencode($text) . '&itc=hi-t-i0-und&num=5&cp=0&cs=1&ie=utf-8&oe=utf-8&app=demopage';
        $userAgent = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36';
        
        $output = false;
        if (function_exists('curl_init')) {
            $ch = curl_init();
            curl_setopt($ch, CURLOPT_URL, $url);
            curl_setopt($ch, CURLOPT_RETURNTRANSFER, true);
            curl_setopt($ch, CURLOPT_SSL_VERIFYPEER, false);
            curl_setopt($ch, CURLOPT_USERAGENT, $userAgent);
            $output = curl_exec($ch);
            curl_close($ch);
        }
        if ($output === false) {
            $options = [
                'http' => [
                    'method' => 'GET',
                    'header' => "User-Agent: $userAgent\r\n"
                ]
            ];
            $context = stream_context_create($options);
            $output = @file_get_contents($url, false, $context);
        }

        $result = json_decode($output, true);
        if ($result && $result[0] === 'SUCCESS') {
            $suggestions = $result[1][0][1] ?? [];
            success_response($suggestions);
        } else {
            success_response([]);
        }
    }

    public function uploadImage(): void
    {
        if (empty($_FILES['image'])) {
            error_response('No image file provided.', 400);
        }

        $file = $_FILES['image'];
        if ($file['error'] !== UPLOAD_ERR_OK) {
            error_response('File upload failed with error code ' . $file['error'], 400);
        }

        // Validate file type
        $allowedTypes = ['image/jpeg', 'image/png', 'image/gif', 'image/webp'];
        $fileType = mime_content_type($file['tmp_name']);
        if (!in_array($fileType, $allowedTypes)) {
            error_response('Invalid file type. Only JPEG, PNG, GIF, and WEBP are allowed.', 400);
        }

        // Create target directory in public/uploads
        $targetDir = dirname(__DIR__, 2) . '/public/uploads';
        if (!is_dir($targetDir)) {
            mkdir($targetDir, 0755, true);
        }

        $origExt = pathinfo($file['name'], PATHINFO_EXTENSION) ?: 'jpg';

        // Optimize on upload: resize large images down to a sensible max and
        // re-encode as WebP (high quality, much smaller file). A multi-MB phone
        // photo becomes a sharp ~80-150KB image — perfect for the mobile-menu
        // PWA: fast on mobile data, and one optimized image scales (via CSS
        // object-fit) to fit any card/thumbnail/full view. Falls back to storing
        // the original if GD/WebP is unavailable.
        $useWebp = function_exists('imagewebp');
        $optFilename = uniqid('img_', true) . '.' . ($useWebp ? 'webp' : 'jpg');
        $optPath = $targetDir . '/' . $optFilename;

        // 'download' variant = full menu-card images that customers save/zoom, so
        // keep them larger and crisper. Everything else is a small UI thumbnail.
        $variant = $_POST['variant'] ?? $_GET['variant'] ?? '';
        $isDownload = ($variant === 'download');
        $maxDim  = $isDownload ? 1400 : 800;
        $quality = $isDownload ? 85 : 80;

        if (self::optimizeImage($file['tmp_name'], $optPath, $fileType, $maxDim, $quality, $useWebp)) {
            @unlink($file['tmp_name']);
            $filename = $optFilename;
        } else {
            // Fallback: store the original as-is.
            @unlink($optPath);
            $filename = uniqid('img_', true) . '.' . $origExt;
            if (!move_uploaded_file($file['tmp_name'], $targetDir . '/' . $filename)) {
                error_response('Failed to save uploaded file.', 500);
            }
        }

        $scheme = (!empty($_SERVER['HTTPS']) && strtolower((string) $_SERVER['HTTPS']) !== 'off') ? 'https' : 'http';
        $host = $_SERVER['HTTP_HOST'] ?? 'localhost';
        $url = $scheme . '://' . $host . '/public/uploads/' . $filename;
        success_response(['url' => $url], 'Image uploaded successfully.');
    }

    /**
     * Resize (down only) and compress an image. Outputs WebP when available,
     * else JPEG. Preserves aspect ratio and transparency. Returns false if the
     * source can't be processed so the caller can fall back to the original.
     */
    private static function optimizeImage(string $src, string $dest, string $mime, int $maxDim, int $quality, bool $useWebp): bool
    {
        if (!function_exists('imagecreatetruecolor')) {
            return false; // GD not installed
        }
        switch ($mime) {
            case 'image/jpeg': $img = @imagecreatefromjpeg($src); break;
            case 'image/png':  $img = @imagecreatefrompng($src); break;
            case 'image/gif':  $img = @imagecreatefromgif($src); break;
            case 'image/webp': $img = function_exists('imagecreatefromwebp') ? @imagecreatefromwebp($src) : false; break;
            default: return false;
        }
        if (!$img) {
            return false;
        }
        $w = imagesx($img);
        $h = imagesy($img);
        $scale = min(1.0, $maxDim / max(1, max($w, $h))); // never upscale
        if ($scale < 1.0) {
            $nw = max(1, (int) round($w * $scale));
            $nh = max(1, (int) round($h * $scale));
            $resized = imagecreatetruecolor($nw, $nh);
            imagealphablending($resized, false);
            imagesavealpha($resized, true);
            imagecopyresampled($resized, $img, 0, 0, 0, 0, $nw, $nh, $w, $h);
            imagedestroy($img);
            $img = $resized;
        }

        if ($useWebp && function_exists('imagewebp')) {
            if (function_exists('imagepalettetotruecolor')) {
                imagepalettetotruecolor($img);
            }
            $ok = @imagewebp($img, $dest, $quality);
        } else {
            // JPEG can't hold transparency — flatten onto white first.
            $flat = imagecreatetruecolor(imagesx($img), imagesy($img));
            $white = imagecolorallocate($flat, 255, 255, 255);
            imagefill($flat, 0, 0, $white);
            imagecopy($flat, $img, 0, 0, 0, 0, imagesx($img), imagesy($img));
            imagedestroy($img);
            $img = $flat;
            $ok = @imagejpeg($img, $dest, max(75, $quality));
        }
        imagedestroy($img);
        return (bool) $ok;
    }
}
