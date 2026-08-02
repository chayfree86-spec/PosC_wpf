<?php
$activeTab = 'items';
include 'header.php';
include 'sidebar.php';
?>
<main class="workspace-container">
  <section id="itemsTab" class="workspace active">
    <section class="panel list-panel">
      <div class="list-head">
        <div class="list-head-left">
          <h2>Menu Items</h2>
          <button id="addItemBtn" class="add-btn" type="button">+ Add Menu Item</button>
        </div>
        <div class="list-actions">
          <select id="filterCategory" aria-label="Filter Category" class="filter-select"></select>
          <select id="filterSubCategory" aria-label="Filter Subcategory" class="filter-select"></select>
          <input id="itemSearch" type="search" placeholder="Search item (Press /)">
        </div>
      </div>
      <div id="itemsList" class="data-list"></div>
    </section>
  </section>
</main>
<?php
include 'modals.php';
include 'footer.php';
?>
