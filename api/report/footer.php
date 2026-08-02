    </div> <!-- Close .main-content -->
  </div> <!-- Close .app-container -->

  <div id="toast" class="toast" role="status" aria-live="polite"></div>
  <div id="globalUploadProgress" class="global-upload-progress" style="display: none;" role="status" aria-live="polite">
    <div class="upload-progress-head">
      <span id="globalUploadProgressLabel">Uploading image...</span>
      <span id="globalUploadProgressPercent">0%</span>
    </div>
    <div class="upload-progress-track">
      <div id="globalUploadProgressBar" class="upload-progress-bar"></div>
    </div>
  </div>
  <input type="file" id="directItemImageFileInput" accept="image/*" style="display: none;">
  <script>
    // Safe, direct closing of all modals
    document.body.addEventListener('click', function(e) {
      var closeBtn = e.target.closest ? e.target.closest('.modal-overlay .close-modal-btn') : null;
      var isOverlay = e.target.classList && e.target.classList.contains('modal-overlay');
      if (closeBtn || isOverlay) {
        var modal = closeBtn ? closeBtn.closest('.modal-overlay') : e.target;
        if (modal) {
          modal.style.setProperty('display', 'none', 'important');
          modal.style.setProperty('opacity', '0', 'important');
          modal.classList.remove('active');
          document.body.style.overflow = '';
        }
      }
    });
  </script>
  <script src="menu-admin.js?v=<?php echo filemtime('menu-admin.js'); ?>"></script>
</body>
</html>
