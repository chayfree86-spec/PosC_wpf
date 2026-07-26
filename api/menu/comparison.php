<?php
$activeTab = 'comparison';
include 'header.php';
include 'sidebar.php';
?>
<main class="workspace-container">
  <!-- Month-wise Item Comparison Tab -->
  <section id="comparisonTab" class="workspace active">
    <section class="panel list-panel" style="max-width: 100%; width: 100%;">

      <!-- Head: Title and filters -->
      <div class="list-head" style="margin-bottom: 20px;">
        <div class="list-head-left" style="display: flex; flex-direction: column; align-items: flex-start; gap: 2px;">
          <p class="eyebrow" style="color: var(--brand-light); font-weight: 700; margin: 0; font-size: 11px; text-transform: uppercase; letter-spacing: 1px;">Month-wise Analysis</p>
          <h2 style="font-size: 26px; font-weight: 800; color: #fff; margin: 2px 0 0 0; font-family: 'Outfit', sans-serif;">Item Comparison</h2>
          <p style="color: var(--muted); margin: 4px 0 0 0; font-size: 13px;">Compare item quantity and amount across months.</p>
        </div>
        <div class="list-head-right" style="display: flex; gap: 12px; align-items: center; flex-wrap: wrap; background: rgba(255,255,255,0.02); padding: 12px; border-radius: var(--radius-sm); border: 1px solid rgba(255,255,255,0.05);">
          <div style="display: flex; flex-direction: column; gap: 4px;">
            <span style="font-size: 11px; font-weight: 700; color: var(--muted); text-transform: uppercase;">Select Client</span>
            <select id="clientSelect" class="filter-select"></select>
          </div>
        </div>
      </div>

      <!-- Month chips & Filters (All in one single row on desktop) -->
      <div style="background: rgba(255,255,255,0.02); border: 1px solid rgba(255,255,255,0.05); border-radius: var(--radius-sm); padding: 12px; margin-bottom: 20px; display: flex; align-items: center; flex-wrap: wrap; gap: 10px;">
        <!-- Month chips selection -->
        <div style="display: flex; align-items: center; flex-wrap: wrap; gap: 6px;">
          <span style="font-size: 11px; font-weight: 700; color: var(--muted); text-transform: uppercase; white-space: nowrap; margin-right: 2px;">Months:</span>
          <div id="comparisonMonthChips" style="display: flex; flex-wrap: wrap; gap: 4px;"></div>
        </div>
        
        <!-- Separator -->
        <div style="width: 1px; height: 24px; background: rgba(255,255,255,0.08); flex-shrink: 0;" class="mobile-hide"></div>
        
        <!-- Category & Subcategory dropdowns and Search Input -->
        <select id="comparisonFilterCategory" class="filter-select" style="min-width: 110px; min-height: 36px; background: rgba(15, 23, 42, 0.7); border: 1px solid rgba(255, 255, 255, 0.08); color: #fff; font-family: 'Outfit', sans-serif; font-size: 12px; font-weight: 500; padding: 0 8px;"></select>
        <select id="comparisonFilterSubCategory" class="filter-select" style="min-width: 110px; min-height: 36px; background: rgba(15, 23, 42, 0.7); border: 1px solid rgba(255, 255, 255, 0.08); color: #fff; font-family: 'Outfit', sans-serif; font-size: 12px; font-weight: 500; padding: 0 8px;"></select>
        <input id="comparisonSearchInput" type="text" placeholder="Search item... (Press /)" style="min-width: 180px; min-height: 36px; background: rgba(15, 23, 42, 0.7); border: 1px solid rgba(255, 255, 255, 0.08); border-radius: var(--radius-sm); color: #fff; padding: 0 10px; font-family: 'Outfit', sans-serif; font-weight: 600; font-size: 12px; margin-left: auto;">
      </div>

      <!-- Comparison Table -->
      <div style="overflow-x: auto;">
        <table class="report-table" style="width: 100%; border-collapse: collapse; text-align: left;">
          <thead id="comparisonTableHead"></thead>
          <tbody id="comparisonTableBody">
            <tr><td colspan="8" style="text-align: center; color: var(--muted); padding: 24px;">Loading...</td></tr>
          </tbody>
        </table>
        <div id="comparisonPaginationContainer" style="display: flex; justify-content: flex-end; align-items: center; gap: 12px; padding: 12px 8px 4px;"></div>
      </div>

    </section>
  </section>
</main>
<?php include 'footer.php'; ?>
