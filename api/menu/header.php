<?php
date_default_timezone_set('Asia/Kolkata');
header("Cache-Control: no-store, no-cache, must-revalidate, max-age=0");
header("Cache-Control: post-check=0, pre-check=0", false);
header("Pragma: no-cache");

// The project's own .env. This used to read electronapp/.env, which disappeared with the
// Electron app; both files carry the same API_URL, so the dashboard now reads the one that
// is still here.
$envFile = dirname(__DIR__, 2) . '/.env';
$apiUrl = '';

if (is_file($envFile)) {
    foreach (file($envFile, FILE_IGNORE_NEW_LINES | FILE_SKIP_EMPTY_LINES) ?: [] as $line) {
        $line = trim($line);
        if ($line === '' || str_starts_with($line, '#') || !str_contains($line, '=')) {
            continue;
        }

        [$key, $value] = explode('=', $line, 2);
        if (trim($key) === 'API_URL') {
            $apiUrl = trim($value, " \t\n\r\0\x0B\"'");
            break;
        }
    }
}
?>
<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <?php if ($apiUrl !== ''): ?>
    <meta name="pos-api-url" content="<?= htmlspecialchars($apiUrl, ENT_QUOTES, 'UTF-8') ?>">
    <?php endif; ?>
    <title>POS Menu Admin</title>
    <link rel="icon" type="image/png" href="favicon.png">
    <link rel="manifest" href="manifest.json">
    <meta name="theme-color" content="#ea580c">
    <link rel="apple-touch-icon" href="icon-192.png">
    <script>
      (function() {
        const CURRENT_VERSION = 'v3';
        const savedVersion = localStorage.getItem('pos_pwa_version');
        
        if (savedVersion !== CURRENT_VERSION) {
          // Clear all PWA caches
          if ('caches' in window) {
            caches.keys().then(names => {
              for (let name of names) {
                caches.delete(name);
              }
            });
          }
          
          // Unregister service workers
          if ('serviceWorker' in navigator) {
            navigator.serviceWorker.getRegistrations().then(registrations => {
              let promises = [];
              for (let registration of registrations) {
                promises.push(registration.unregister());
              }
              Promise.all(promises).then(() => {
                localStorage.setItem('pos_pwa_version', CURRENT_VERSION);
                window.location.reload();
              });
            });
          } else {
            localStorage.setItem('pos_pwa_version', CURRENT_VERSION);
            window.location.reload();
          }
        } else {
          // Register the new service worker normally
          if ('serviceWorker' in navigator) {
            window.addEventListener('load', () => {
              navigator.serviceWorker.register('sw.js?v=' + CURRENT_VERSION)
                .then(reg => console.log('Service Worker registered successfully:', reg.scope))
                .catch(err => console.error('Service Worker registration failed:', err));
            });
          }
        }
      })();
    </script>
    <link rel="preconnect" href="https://fonts.googleapis.com">
    <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
    <link href="https://fonts.googleapis.com/css2?family=Outfit:wght@400;500;600;700;800&family=Plus+Jakarta+Sans:wght@400;500;600;700&display=swap" rel="stylesheet">
    <link rel="stylesheet" href="menu-admin.css?v=<?php echo filemtime('menu-admin.css'); ?>">
  </head>
  <body class="logged-out" data-active-tab="<?= htmlspecialchars($activeTab ?? 'items', ENT_QUOTES, 'UTF-8') ?>">
    <script>
      (function() {
        try {
          const session = JSON.parse(localStorage.getItem('pos_menu_admin_session') || '{}');
          if (session && session.token) {
            document.body.classList.remove('logged-out');
            document.body.classList.add('logged-in');
          }
        } catch (e) {}
      })();
    </script>
    <!-- Mobile Fixed Header -->
    <div class="mobile-header">
      <h2>Menu & Table Manager</h2>
      <button type="button" id="mobileMenuToggle" class="mobile-menu-toggle" aria-label="Toggle menu">☰</button>
    </div>

    <div class="app-container">
