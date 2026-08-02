<?php
$activeTab = 'gallery';
include 'header.php';
include 'sidebar.php';
?>
<main class="workspace-container">
  <section id="galleryTab" class="workspace active">
    <section class="panel list-panel">
      <div class="list-head" style="margin-bottom: 24px;">
        <div class="list-head-left" style="display: flex; gap: 12px; align-items: center;">
          <select id="galleryFilterCategory" class="filter-select"></select>
          <select id="galleryFilterSubCategory" class="filter-select"></select>
          <input id="gallerySearch" type="search" placeholder="Search gallery images...">
        </div>
        <div class="list-head-right" style="display: flex; gap: 12px; align-items: center; flex-wrap: wrap;">
          <button id="galleryUploadBtn" class="add-btn" type="button">+ Upload Image</button>
          <input type="file" id="galleryFileInput" accept="image/*" multiple style="display: none;">
          <input type="file" id="updateGalleryImageFileInput" accept="image/*" style="display: none;">
        </div>
      </div>
      
      <div id="galleryList" class="gallery-grid" style="display: grid; grid-template-columns: repeat(auto-fill, minmax(180px, 1fr)); gap: 16px; margin-top: 16px;"></div>
    </section>
  </section>
</main>
<?php
include 'modals.php';
include 'footer.php';
?>
