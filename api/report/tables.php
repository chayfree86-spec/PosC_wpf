<?php
$activeTab = 'tables';
include 'header.php';
include 'sidebar.php';
?>
<main class="workspace-container">
  <section id="tablesTab" class="workspace active">
    <section class="panel list-panel">
      <div class="list-head" style="margin-bottom: 12px;">
        <div class="list-head-left" style="display: flex; align-items: center; gap: 16px; flex-wrap: wrap;">
          <div class="sub-tabs-pill-container" style="display: flex; gap: 6px; background: rgba(0, 0, 0, 0.2); padding: 4px; border-radius: var(--radius-sm); border: 1px solid rgba(255, 255, 255, 0.05);">
            <button type="button" class="sub-tab active" data-sub-tab="table-list" style="min-height: auto; padding: 6px 16px; font-size: 13px; border-radius: var(--radius-sm); background: transparent; font-weight: 600; box-shadow: none; color: var(--muted); border: none;">Tables</button>
            <button type="button" class="sub-tab" data-sub-tab="area-list" style="min-height: auto; padding: 6px 16px; font-size: 13px; border-radius: var(--radius-sm); background: transparent; font-weight: 600; box-shadow: none; color: var(--muted); border: none;">Dining Areas</button>
          </div>
        </div>
        <div class="list-head-right">
          <button id="addTableBtn" class="add-btn" type="button">+ Add Table</button>
          <button id="addAreaBtn" class="add-btn" type="button" style="display: none;">+ Add Dining Area</button>
        </div>
      </div>

      <!-- Sub Tab Content: Tables List -->
      <div id="subTabTables" class="sub-tab-content">
        <div id="tablesList" class="data-list"></div>
      </div>

      <!-- Sub Tab Content: Areas List -->
      <div id="subTabAreas" class="sub-tab-content" style="display: none;">
        <div id="areasList" class="data-list"></div>
      </div>
    </section>
  </section>
</main>
<?php
include 'modals.php';
include 'footer.php';
?>
