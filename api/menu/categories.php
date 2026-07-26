<?php
$activeTab = 'categories';
include 'header.php';
include 'sidebar.php';
?>
<main class="workspace-container">
  <section id="categoriesTab" class="workspace active">
    <section class="panel list-panel">
      <div class="list-head">
        <div class="list-head-left">
          <h2>Categories</h2>
          <button id="addCategoryBtn" class="add-btn" type="button">+ Add Category</button>
        </div>
      </div>
      <div id="categoriesList" class="data-list"></div>
    </section>
  </section>
</main>
<?php
include 'modals.php';
include 'footer.php';
?>
