<?php
$activeTab = 'mobile-menu';
include 'header.php';
include 'sidebar.php';

// Locally the admin and the mobile-menu PWA share one docroot, so a relative
// path works. In production they are separate subdomains (admin on
// report.chaychaupal.com, the PWA on menu.chaychaupal.com) with different
// docroots -- so '../mobilemenu/dist/' lands outside the admin's root and 404s.
// Use the live mobile-menu subdomain when not running locally.
$adminHost = strtolower($_SERVER['HTTP_HOST'] ?? '');
$isLocalHost = (strpos($adminHost, 'localhost') !== false)
    || (strpos($adminHost, '127.0.0.1') !== false)
    || (strpos($adminHost, '.local') !== false)
    || (strpos($adminHost, '.test') !== false);
// NOTE: do NOT call App\Models\Client here -- it is not autoloaded on these
// standalone admin pages, so it fatally truncated the page output and the SPA
// could not find .workspace-container (the tab "wouldn't open"). The logged-in
// client is appended to this URL in JS (menu-admin.js, from the admin session).
$mobileMenuUrl = $isLocalHost
    ? '../mobilemenu/dist/index.html'
    : 'https://menu.chaychaupal.com/';
?>
<main class="workspace-container">
  <section id="mobile-menuTab" class="workspace active">
    <section class="mobile-preview-shell panel">
      <div class="mobile-preview-toolbar">
        <div>
          <span class="eyebrow">Live Preview</span>
          <h1>Mobile Menu</h1>
        </div>

        <div class="mobile-preview-actions">
          <label>
            Device
            <select id="mobilePreviewDevice">
              <option value="iphone-se">iPhone SE</option>
              <option value="iphone-12" selected>iPhone 12 / 13</option>
              <option value="iphone-14-pro">iPhone 14 Pro</option>
              <option value="pixel-7">Pixel 7</option>
              <option value="samsung-s22">Samsung S22</option>
            </select>
          </label>
          <button type="button" id="mobilePreviewRefresh" class="ghost">Refresh</button>
          <a class="ghost mobile-preview-open" href="<?= htmlspecialchars($mobileMenuUrl, ENT_QUOTES, 'UTF-8') ?>" target="_blank" rel="noopener">Open</a>
        </div>
      </div>

      <div class="mobile-preview-stage">
        <div id="mobileDeviceScale" class="mobile-device-scale">
          <div id="mobileDeviceFrame" class="mobile-device-frame device-iphone-12">
            <div class="mobile-device-speaker"></div>
            <iframe
              id="mobileMenuPreviewFrame"
              title="Mobile Menu Preview"
              src="<?= htmlspecialchars($mobileMenuUrl, ENT_QUOTES, 'UTF-8') ?>"
              loading="lazy"
            ></iframe>
          </div>
        </div>
      </div>

      <div class="mobile-download-footer">
        <div class="mobile-download-head">
          <div>
            <span class="eyebrow">Download Assets</span>
            <h2>Mobile Header Download Images</h2>
          </div>
          <label id="mobileDownloadUploadBtn" class="add-btn mobile-upload-label">
            <span>+ Upload Images</span>
            <input type="file" id="mobileDownloadFileInput" class="file-input-hidden" accept="image/*" multiple>
          </label>
        </div>

        <label for="mobileDownloadFileInput" id="mobileDownloadDropZone" class="mobile-download-drop">
          <div>
            <strong>Drop multiple menu images here</strong>
            <span>Images upload to Google Drive and link with mobile menu Download button.</span>
          </div>
        </label>

        <div id="mobileDownloadUploadProgress" class="upload-progress" style="display: none;">
          <div class="upload-progress-head">
            <span id="mobileDownloadUploadProgressLabel">Uploading image...</span>
            <span id="mobileDownloadUploadProgressPercent">0%</span>
          </div>
          <div class="upload-progress-track">
            <div id="mobileDownloadUploadProgressBar" class="upload-progress-bar"></div>
          </div>
        </div>

        <div id="mobileDownloadPreviewGrid" class="mobile-download-grid"></div>
      </div>
    </section>
  </section>
</main>
<?php
include 'modals.php';
include 'footer.php';
?>
