<?php
$activeTab = 'reports';
include 'header.php';
include 'sidebar.php';
?>
<main class="workspace-container">
  <!-- Reports Workspace Tab -->
  <section id="reportsTab" class="workspace active">
    <section class="panel list-panel" style="max-width: 100%; width: 100%;">
      <!-- Head: Title and main filters -->
      <div class="list-head" style="margin-bottom: 24px;">
        <div class="list-head-left" style="display: flex; flex-direction: column; align-items: flex-start; gap: 2px;">
          <p class="eyebrow" style="color: var(--brand-light); font-weight: 700; margin: 0; font-size: 11px; text-transform: uppercase; letter-spacing: 1px;">Dashboard Overview</p>
          <h2 style="font-size: 26px; font-weight: 800; color: #fff; margin: 2px 0 0 0; font-family: 'Outfit', sans-serif;">Business Summary</h2>
          <p style="color: var(--muted); margin: 4px 0 0 0; font-size: 13px;">Sales, billing trends, and item performance.</p>
        </div>
        <div class="list-head-right" style="display: flex; gap: 12px; align-items: center; flex-wrap: wrap; background: rgba(255,255,255,0.02); padding: 12px; border-radius: var(--radius-sm); border: 1px solid rgba(255,255,255,0.05);">
          <div style="display: flex; flex-direction: column; gap: 4px;">
            <span style="font-size: 11px; font-weight: 700; color: var(--muted); text-transform: uppercase;">Select Client</span>
            <select id="clientSelect" class="filter-select"></select>
          </div>
          <div style="display: flex; flex-direction: column; gap: 4px;">
            <span style="font-size: 11px; font-weight: 700; color: var(--muted); text-transform: uppercase;">Filter By</span>
            <select id="reportRangeType" class="filter-select">
              <option value="day">Daily View</option>
              <option value="week">Weekly View</option>
              <option value="month">Monthly View</option>
            </select>
          </div>
          <div style="display: flex; flex-direction: column; gap: 4px; position: relative;">
            <span id="reportDateLabel" style="font-size: 11px; font-weight: 700; color: var(--muted); text-transform: uppercase;">Date</span>
            <!-- Custom dark-theme calendar (native <input type="date"> popup is
                 OS/browser-rendered and cannot be restyled with CSS). The hidden
                 input keeps id="reportDate" and fires the same 'change' event so
                 all existing JS (loadReportsData, .value reads) needs no changes. -->
            <input type="hidden" id="reportDate" value="<?php echo date('Y-m-d'); ?>">
            <button type="button" id="reportDateTrigger" class="report-date-trigger">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="flex-shrink:0;"><rect x="3" y="4" width="18" height="18" rx="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/></svg>
              <span id="reportDateTriggerLabel"></span>
            </button>
            <div id="reportDateCalendar" class="report-date-calendar" style="display:none;"></div>
          </div>
        </div>
      </div>

      <!-- KPI Cards Grid -->
      <div class="reports-kpi-grid" style="display: grid; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr)); gap: 20px; margin-bottom: 24px;">
        <!-- Cards will be populated dynamically -->
      </div>

      <!-- Growth Wave (Chart) & High & Low Cards Row -->
      <div style="display: grid; grid-template-columns: repeat(auto-fit, minmax(320px, 1fr)); gap: 20px; margin-bottom: 24px;">
        <!-- Left: Growth Wave Chart Panel -->
        <div class="panel" style="background: rgba(22, 28, 45, 0.3); border: 1px solid rgba(255,255,255,0.05); padding: 20px; border-radius: var(--radius-md);">
          <p class="eyebrow" style="color: var(--brand-light); font-weight: 700; margin: 0 0 12px 0; font-size: 11px; text-transform: uppercase; letter-spacing: 1px;">Growth Wave</p>
          <div id="growthWaveChartContainer" style="height: 240px; display: flex; align-items: flex-end; justify-content: space-between; padding-bottom: 10px; border-bottom: 1px solid rgba(255,255,255,0.08); position: relative;">
            <!-- CSS Bar charts rendered here -->
          </div>
          <div id="growthWaveLabels" style="display: flex; justify-content: space-between; padding-top: 8px; font-size: 11px; font-weight: 700; color: var(--muted);">
            <!-- Axis labels -->
          </div>
        </div>

        <!-- Right: High & Low Panel -->
        <div class="panel" style="background: rgba(22, 28, 45, 0.3); border: 1px solid rgba(255,255,255,0.05); padding: 20px; border-radius: var(--radius-md); display: flex; flex-direction: column; justify-content: space-between; gap: 16px;">
          <div style="display: flex; align-items: center; justify-content: space-between;">
            <p class="eyebrow" style="color: var(--brand-light); font-weight: 700; margin: 0; font-size: 11px; text-transform: uppercase; letter-spacing: 1px;">High & Low</p>
            <div class="sub-tabs-pill-container" style="display: flex; gap: 4px; background: rgba(0, 0, 0, 0.2); padding: 3px; border-radius: var(--radius-sm); border: 1px solid rgba(255, 255, 255, 0.05);">
              <button type="button" class="sub-tab active" data-highlow-tab="week" style="min-height: auto; padding: 4px 10px; font-size: 11px; border-radius: var(--radius-sm); background: transparent; font-weight: 700; color: var(--muted); border: none; cursor: pointer;">Weekly</button>
              <button type="button" class="sub-tab" data-highlow-tab="month" style="min-height: auto; padding: 4px 10px; font-size: 11px; border-radius: var(--radius-sm); background: transparent; font-weight: 700; color: var(--muted); border: none; cursor: pointer;">Monthly</button>
              <button type="button" class="sub-tab" data-highlow-tab="year" style="min-height: auto; padding: 4px 10px; font-size: 11px; border-radius: var(--radius-sm); background: transparent; font-weight: 700; color: var(--muted); border: none; cursor: pointer;">Yearly</button>
            </div>
          </div>

          <div style="display: flex; flex-direction: column; gap: 12px;">
            <!-- High Sales Card -->
            <div style="background: rgba(16, 185, 129, 0.05); border: 1px solid rgba(16, 185, 129, 0.15); border-radius: var(--radius-sm); padding: 12px 16px; display: flex; align-items: center; justify-content: space-between;">
              <div style="display: flex; align-items: center; gap: 12px;">
                <div style="background: rgba(16, 185, 129, 0.15); color: #10b981; width: 36px; height: 36px; border-radius: 50%; display: flex; align-items: center; justify-content: center; font-size: 18px;">↗</div>
                <div>
                  <div style="font-size: 12px; font-weight: 700; color: var(--muted); text-transform: uppercase;">High Sales</div>
                  <div id="highSalesDate" style="font-size: 11px; color: var(--muted); opacity: 0.8; margin-top: 2px;">--</div>
                </div>
              </div>
              <div id="highSalesValue" style="font-size: 18px; font-weight: 800; color: #10b981; font-family: 'Outfit', sans-serif;">Rs. 0</div>
            </div>

            <!-- Low Sales Card -->
            <div style="background: rgba(239, 68, 68, 0.05); border: 1px solid rgba(239, 68, 68, 0.15); border-radius: var(--radius-sm); padding: 12px 16px; display: flex; align-items: center; justify-content: space-between;">
              <div style="display: flex; align-items: center; gap: 12px;">
                <div style="background: rgba(239, 68, 68, 0.15); color: #ef4444; width: 36px; height: 36px; border-radius: 50%; display: flex; align-items: center; justify-content: center; font-size: 18px;">↘</div>
                <div>
                  <div style="font-size: 12px; font-weight: 700; color: var(--muted); text-transform: uppercase;">Low Sales</div>
                  <div id="lowSalesDate" style="font-size: 11px; color: var(--muted); opacity: 0.8; margin-top: 2px;">--</div>
                </div>
              </div>
              <div id="lowSalesValue" style="font-size: 18px; font-weight: 800; color: #ef4444; font-family: 'Outfit', sans-serif;">Rs. 0</div>
            </div>
          </div>

          <!-- Previous Period Comparison -->
          <div style="border-top: 1px solid rgba(255,255,255,0.06); padding-top: 14px;">
            <p class="eyebrow" style="color: var(--muted); font-weight: 700; margin: 0 0 8px 0; font-size: 10px; text-transform: uppercase; letter-spacing: 0.5px;">Previous Period Comparison</p>
            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px;">
              <div style="background: rgba(255,255,255,0.02); border: 1px solid rgba(255,255,255,0.05); border-radius: var(--radius-sm); padding: 8px 12px;">
                <span style="font-size: 9px; font-weight: 700; color: var(--muted); text-transform: uppercase; display: block;">↗ High</span>
                <span id="prevPeriodHigh" style="font-size: 13px; font-weight: 700; color: #fff; font-family: 'Outfit', sans-serif; display: block; margin-top: 2px;">Rs. 0</span>
              </div>
              <div style="background: rgba(255,255,255,0.02); border: 1px solid rgba(255,255,255,0.05); border-radius: var(--radius-sm); padding: 8px 12px;">
                <span style="font-size: 9px; font-weight: 700; color: var(--muted); text-transform: uppercase; display: block;">↘ Low</span>
                <span id="prevPeriodLow" style="font-size: 13px; font-weight: 700; color: #fff; font-family: 'Outfit', sans-serif; display: block; margin-top: 2px;">Rs. 0</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- Monthly Sales Breakdown Table -->
      <div class="panel" style="background: rgba(22, 28, 45, 0.3); border: 1px solid rgba(255,255,255,0.05); padding: 20px; border-radius: var(--radius-md); margin-bottom: 24px;">
        <p class="eyebrow" style="color: var(--brand-light); font-weight: 700; margin: 0 0 4px 0; font-size: 11px; text-transform: uppercase; letter-spacing: 1px;">Monthly Sales Breakdown</p>
        <p style="color: var(--muted); margin: 0 0 16px 0; font-size: 13px;">Detailed overview of sales performance by month.</p>
        <div style="overflow-x: auto;">
          <table class="report-table" style="width: 100%; border-collapse: collapse; text-align: left;">
            <thead>
              <tr style="border-bottom: 1px solid rgba(255,255,255,0.08); font-size: 11px; font-weight: 700; color: var(--muted); text-transform: uppercase;">
                <th style="padding: 12px 8px;">Month</th>
                <th style="padding: 12px 8px;">Total Amount</th>
                <th style="padding: 12px 8px;">Avg. Daily</th>
                <th style="padding: 12px 8px;">Avg. Weekly</th>
              </tr>
            </thead>
            <tbody id="monthlyBreakdownTableBody">
              <!-- Dynamic rows -->
            </tbody>
          </table>
        </div>
      </div>

      <!-- Detailed Analysis Section (Sub-tabs: BILL, SALES, ITEMS) -->
      <div class="panel" style="background: rgba(22, 28, 45, 0.3); border: 1px solid rgba(255,255,255,0.05); padding: 20px; border-radius: var(--radius-md);">
        <div style="display: flex; align-items: center; gap: 12px; margin-bottom: 20px; border-bottom: 1px solid rgba(255,255,255,0.06); padding-bottom: 12px;">
          <div class="sub-tabs-pill-container" style="display: flex; gap: 6px; background: rgba(0, 0, 0, 0.2); padding: 4px; border-radius: var(--radius-sm); border: 1px solid rgba(255, 255, 255, 0.05);">
            <button type="button" class="sub-tab" id="detailsTabBill" style="min-height: auto; padding: 6px 18px; font-size: 13px; border-radius: var(--radius-sm); background: transparent; font-weight: 700; color: var(--muted); border: none; cursor: pointer;">BILL</button>
            <button type="button" class="sub-tab" id="detailsTabSales" style="min-height: auto; padding: 6px 18px; font-size: 13px; border-radius: var(--radius-sm); background: transparent; font-weight: 700; color: var(--muted); border: none; cursor: pointer;">SALES</button>
            <button type="button" class="sub-tab active" id="detailsTabItems" style="min-height: auto; padding: 6px 18px; font-size: 13px; border-radius: var(--radius-sm); background: transparent; font-weight: 700; color: var(--muted); border: none; cursor: pointer;">ITEMS</button>
          </div>
        </div>

        <!-- Content BILL Tab -->
        <div id="detailsContentBill" class="details-tab-content" style="display: none;">
          <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 16px; flex-wrap: wrap; gap: 12px;">
            <h3 style="font-size: 18px; font-weight: 700; color: #fff; margin: 0; font-family: 'Outfit', sans-serif;">All Bills / Transactions</h3>
            <div style="display: flex; gap: 8px; align-items: center;">
              <input type="search" id="billSearchInput" placeholder="Search by amount, table, customer..." style="min-width: 250px; min-height: 38px; background: rgba(15, 23, 42, 0.7); border: 1px solid rgba(255, 255, 255, 0.08); border-radius: var(--radius-sm); color: #fff; padding: 0 14px; font-family: 'Outfit', sans-serif; font-size: 13px; font-weight: 500;">
            </div>
          </div>
          <div style="overflow-x: auto;">
            <table class="report-table" style="width: 100%; border-collapse: collapse; text-align: left;">
              <thead>
                <tr style="border-bottom: 1px solid rgba(255,255,255,0.08); font-size: 11px; font-weight: 700; color: var(--muted); text-transform: uppercase;">
                  <th style="padding: 12px 8px;">Bill No</th>
                  <th style="padding: 12px 8px;">Date</th>
                  <th style="padding: 12px 8px;">Customer</th>
                  <th style="padding: 12px 8px;">Table</th>
                  <th style="padding: 12px 8px; text-align: right;">Amount</th>
                </tr>
              </thead>
              <tbody id="detailsBillTableBody">
                <!-- Dynamic bill rows -->
              </tbody>
            </table>
          </div>
          <!-- Pagination for BILL tab -->
          <div id="billPaginationContainer" style="display: flex; justify-content: space-between; align-items: center; margin-top: 16px; padding-top: 12px; border-top: 1px solid rgba(255,255,255,0.05); font-size: 13px; color: var(--muted);"></div>
        </div>

        <!-- Content SALES Tab (Sales Timeline) -->
        <div id="detailsContentSales" class="details-tab-content" style="display: none;">
          <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 16px; flex-wrap: wrap; gap: 12px;">
            <div>
              <h3 style="font-size: 18px; font-weight: 700; color: #fff; margin: 0; font-family: 'Outfit', sans-serif;">Sales Timeline</h3>
            </div>
            <div class="sub-tabs-pill-container" style="display: flex; gap: 4px; background: rgba(0, 0, 0, 0.2); padding: 3px; border-radius: var(--radius-sm); border: 1px solid rgba(255, 255, 255, 0.05);">
              <button type="button" class="sub-tab active" data-timeline-range="day" style="min-height: auto; padding: 4px 10px; font-size: 11px; border-radius: var(--radius-sm); background: transparent; font-weight: 700; color: var(--muted); border: none; cursor: pointer;">DAILY</button>
              <button type="button" class="sub-tab" data-timeline-range="week" style="min-height: auto; padding: 4px 10px; font-size: 11px; border-radius: var(--radius-sm); background: transparent; font-weight: 700; color: var(--muted); border: none; cursor: pointer;">WEEKLY</button>
              <button type="button" class="sub-tab" data-timeline-range="month" style="min-height: auto; padding: 4px 10px; font-size: 11px; border-radius: var(--radius-sm); background: transparent; font-weight: 700; color: var(--muted); border: none; cursor: pointer;">MONTHLY</button>
            </div>
          </div>
          <div style="overflow-x: auto;">
            <table class="report-table" style="width: 100%; border-collapse: collapse; text-align: left;">
              <thead>
                <tr style="border-bottom: 1px solid rgba(255,255,255,0.08); font-size: 11px; font-weight: 700; color: var(--muted); text-transform: uppercase;">
                  <th style="padding: 12px 8px;">Time Period</th>
                  <th style="padding: 12px 8px; text-align: center;">Orders</th>
                  <th style="padding: 12px 8px; text-align: right;">Revenue</th>
                  <th style="padding: 12px 8px; text-align: center;">Traffic</th>
                </tr>
              </thead>
              <tbody id="detailsSalesTimelineBody">
                <!-- Dynamic timeline rows -->
              </tbody>
            </table>
          </div>
        </div>

        <!-- Content ITEMS Tab (Most Selling Item) -->
        <div id="detailsContentItems" class="details-tab-content">
          <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 16px; flex-wrap: wrap; gap: 12px;">
            <div>
              <h3 style="font-size: 18px; font-weight: 700; color: #fff; margin: 0; font-family: 'Outfit', sans-serif;">Most Selling Item</h3>
              <p style="color: var(--muted); margin: 2px 0 0 0; font-size: 12px;">Category-wise top items with quantity and amount.</p>
            </div>
            <div style="display: flex; align-items: center; gap: 12px; flex-wrap: wrap;">
              <div class="sub-tabs-pill-container" style="display: flex; gap: 4px; background: rgba(0, 0, 0, 0.2); padding: 3px; border-radius: var(--radius-sm); border: 1px solid rgba(255, 255, 255, 0.05);">
                <button type="button" class="sub-tab active" data-items-range="day" style="min-height: auto; padding: 4px 10px; font-size: 11px; border-radius: var(--radius-sm); background: transparent; font-weight: 700; color: var(--muted); border: none; cursor: pointer;">DAILY</button>
                <button type="button" class="sub-tab" data-items-range="week" style="min-height: auto; padding: 4px 10px; font-size: 11px; border-radius: var(--radius-sm); background: transparent; font-weight: 700; color: var(--muted); border: none; cursor: pointer;">WEEKLY</button>
                <button type="button" class="sub-tab" data-items-range="month" style="min-height: auto; padding: 4px 10px; font-size: 11px; border-radius: var(--radius-sm); background: transparent; font-weight: 700; color: var(--muted); border: none; cursor: pointer;">MONTHLY</button>
              </div>
            </div>
          </div>

          <!-- Filters Row for Items -->
          <div style="display: flex; flex-direction: column; gap: 10px; margin-bottom: 16px; background: rgba(255,255,255,0.02); padding: 12px; border-radius: var(--radius-sm); border: 1px solid rgba(255,255,255,0.05);">
            <div id="reportItemCategoryContainer" style="display: flex; flex-wrap: wrap; gap: 8px;">
              <!-- Category pills will be rendered dynamically -->
            </div>
            <div id="reportItemSubCategoryContainer" style="display: flex; flex-wrap: wrap; gap: 8px;">
              <!-- Subcategory pills will be rendered dynamically -->
            </div>
            <div style="display: flex; justify-content: flex-end; gap: 12px; margin-top: 6px; font-size: 12px; font-weight: 700;">
              <span style="background: rgba(99, 102, 241, 0.15); color: var(--brand-light); padding: 4px 10px; border-radius: var(--radius-sm);">Total: <span id="reportItemTotalAmt">Rs. 0</span></span>
              <span style="background: rgba(249, 115, 22, 0.15); color: #f97316; padding: 4px 10px; border-radius: var(--radius-sm);">Subtotal: <span id="reportItemSubtotalAmt">Rs. 0</span></span>
            </div>
          </div>

          <div style="overflow-x: auto;">
            <table class="report-table" style="width: 100%; border-collapse: collapse; text-align: left;">
              <thead>
                <tr style="border-bottom: 1px solid rgba(255,255,255,0.08); font-size: 11px; font-weight: 700; color: var(--muted); text-transform: uppercase;">
                  <th style="padding: 12px 8px;">Item Name</th>
                  <th style="padding: 12px 8px;">Category</th>
                  <th style="padding: 12px 8px; text-align: right;">Rate</th>
                  <th style="padding: 12px 8px; text-align: center;">Qty</th>
                  <th style="padding: 12px 8px; text-align: right;">Amount</th>
                </tr>
              </thead>
              <tbody id="detailsItemsTableBody">
                <!-- Dynamic top item rows -->
              </tbody>
            </table>
          </div>
          <!-- Pagination for ITEMS tab -->
          <div id="itemsPaginationContainer" style="display: flex; justify-content: space-between; align-items: center; margin-top: 16px; padding-top: 12px; border-top: 1px solid rgba(255,255,255,0.05); font-size: 13px; color: var(--muted);"></div>
        </div>
      </div>

      <!-- Festival Selling Panel -->
      <div class="panel" style="background: rgba(22, 28, 45, 0.3); border: 1px solid rgba(255,255,255,0.05); padding: 20px; border-radius: var(--radius-md); margin-bottom: 24px; margin-top: 24px;">
        <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 16px; flex-wrap: wrap; gap: 12px;">
          <div>
            <p class="eyebrow" style="color: var(--brand-light); font-weight: 700; margin: 0; font-size: 11px; text-transform: uppercase; letter-spacing: 1px;">Festival Selling</p>
          </div>
          <div id="festivalYearContainer" class="sub-tabs-pill-container" style="display: flex; gap: 4px; background: rgba(0, 0, 0, 0.2); padding: 3px; border-radius: var(--radius-sm); border: 1px solid rgba(255, 255, 255, 0.05);">
            <button type="button" class="sub-tab active" data-fest-year="2026" style="min-height: auto; padding: 4px 10px; font-size: 11px; border-radius: var(--radius-sm); background: transparent; font-weight: 700; color: var(--muted); border: none; cursor: pointer;">2026</button>
            <button type="button" class="sub-tab" data-fest-year="2025" style="min-height: auto; padding: 4px 10px; font-size: 11px; border-radius: var(--radius-sm); background: transparent; font-weight: 700; color: var(--muted); border: none; cursor: pointer;">2025</button>
            <button type="button" class="sub-tab" data-fest-year="2024" style="min-height: auto; padding: 4px 10px; font-size: 11px; border-radius: var(--radius-sm); background: transparent; font-weight: 700; color: var(--muted); border: none; cursor: pointer;">2024</button>
            <button type="button" class="sub-tab" data-fest-year="2023" style="min-height: auto; padding: 4px 10px; font-size: 11px; border-radius: var(--radius-sm); background: transparent; font-weight: 700; color: var(--muted); border: none; cursor: pointer;">2023</button>
          </div>
        </div>
        <div style="overflow-x: auto;">
          <table class="report-table" style="width: 100%; border-collapse: collapse; text-align: left;">
            <thead>
              <tr style="border-bottom: 1px solid rgba(255,255,255,0.08); font-size: 11px; font-weight: 700; color: var(--muted); text-transform: uppercase;">
                <th style="padding: 12px 8px;">Festival</th>
                <th style="padding: 12px 8px; text-align: right;">2026</th>
                <th style="padding: 12px 8px; text-align: right;">2025</th>
                <th style="padding: 12px 8px; text-align: right;">2024</th>
                <th style="padding: 12px 8px; text-align: right;">2023</th>
              </tr>
            </thead>
            <tbody id="festivalSellingTableBody">
              <!-- Dynamic rows -->
            </tbody>
          </table>
          <div id="festivalPaginationContainer" style="display: flex; justify-content: flex-end; align-items: center; gap: 12px; padding: 12px 8px 4px;"></div>
        </div>
      </div>
    </section>
  </section>
</main>
<?php
include 'modals.php';
include 'footer.php';
?>
