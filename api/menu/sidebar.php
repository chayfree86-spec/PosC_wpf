<?php
$currentPage = basename($_SERVER['SCRIPT_NAME']);
?>
<!-- Left Sidebar Menu -->
<aside class="sidebar">
  <div class="sidebar-brand">
    <p class="eyebrow">POS Admin</p>
    <h2>Menu & Table Manager</h2>
  </div>

  <nav class="sidebar-nav" aria-label="Admin sections">
    <a class="tab <?= $currentPage === 'items.php' || $currentPage === 'index.php' ? 'active' : '' ?>" href="items.php" data-tab="items">
      <span class="tab-icon">
        <svg viewBox="0 0 24 24"><path d="M2 12a10 10 0 0 1 20 0v2H2v-2Z"/><rect width="20" height="2" x="2" y="14" rx="1"/><path d="M12 20a10 10 0 0 0 8-4H4a10 10 0 0 0 8 4Z"/></svg>
      </span>
      <span class="tab-text">Menu Items</span>
    </a>
    <a class="tab <?= $currentPage === 'categories.php' ? 'active' : '' ?>" href="categories.php" data-tab="categories">
      <span class="tab-icon">
        <svg viewBox="0 0 24 24"><path d="M4 20h16a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.93a2 2 0 0 1-1.66-.9l-.82-1.2A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z"/></svg>
      </span>
      <span class="tab-text">Categories</span>
    </a>
    <a class="tab <?= $currentPage === 'tables.php' ? 'active' : '' ?>" href="tables.php" data-tab="tables">
      <span class="tab-icon">
        <svg viewBox="0 0 24 24"><rect x="3" y="3" width="18" height="18" rx="2"/><path d="M3 9h18M9 21V9"/></svg>
      </span>
      <span class="tab-text">Tables & Areas</span>
    </a>
    <a class="tab <?= $currentPage === 'gallery.php' ? 'active' : '' ?>" href="gallery.php" data-tab="gallery">
      <span class="tab-icon">
        <svg viewBox="0 0 24 24"><rect x="3" y="3" width="18" height="18" rx="2" ry="2"/><circle cx="9" cy="9" r="2"/><path d="m21 15-3.086-3.086a2 2 0 0 0-2.828 0L6 21"/></svg>
      </span>
      <span class="tab-text">Gallery</span>
    </a>
    <a class="tab <?= $currentPage === 'reports.php' ? 'active' : '' ?>" href="reports.php" data-tab="reports">
      <span class="tab-icon">
        <svg viewBox="0 0 24 24"><line x1="18" y1="20" x2="18" y2="10"/><line x1="12" y1="20" x2="12" y2="4"/><line x1="6" y1="20" x2="6" y2="14"/></svg>
      </span>
      <span class="tab-text">Reports</span>
    </a>
    <a class="tab <?= $currentPage === 'comparison.php' ? 'active' : '' ?>" href="comparison.php" data-tab="comparison">
      <span class="tab-icon">
        <svg viewBox="0 0 24 24"><rect x="3" y="8" width="4" height="12" rx="1"/><rect x="10" y="4" width="4" height="16" rx="1"/><rect x="17" y="11" width="4" height="9" rx="1"/></svg>
      </span>
      <span class="tab-text">Comparison</span>
    </a>
    <a class="tab <?= $currentPage === 'mobile-menu.php' ? 'active' : '' ?>" href="mobile-menu.php" data-tab="mobile-menu">
      <span class="tab-icon">
        <svg viewBox="0 0 24 24"><rect x="5" y="2" width="14" height="20" rx="2" ry="2"/><line x1="12" y1="18" x2="12" y2="18"/></svg>
      </span>
      <span class="tab-text">Mobile Menu</span>
    </a>
  </nav>

  <div class="sidebar-footer">
    <button id="refreshBtn" class="icon-btn" type="button" title="Refresh data">
      <span class="tab-icon">
        <svg viewBox="0 0 24 24"><path d="M21 12a9 9 0 0 0-9-9 9.75 9.75 0 0 0-6.74 2.74L3 8"/><path d="M3 3v5h5"/><path d="M3 12a9 9 0 0 0 9 9 9.75 9.75 0 0 0 6.74-2.74L21 16"/><path d="M21 21v-5h-5"/></svg>
      </span>
      <span class="tab-text">Refresh</span>
    </button>
    <button id="logoutBtn" type="button" class="danger">
      <span class="tab-icon">
        <svg viewBox="0 0 24 24"><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4M16 17l5-5-5-5M21 12H9"/></svg>
      </span>
      <span class="tab-text">Logout</span>
    </button>
  </div>
</aside>

<!-- Sidebar Overlay for mobile -->
<div id="sidebarOverlay" class="sidebar-close-overlay"></div>

<div class="main-content">
  <!-- Standalone Auth Panel Redesign -->
  <section class="panel auth-panel">
    <div>
      <h2>POS Login</h2>
      <p id="authStatus">Use the same login details you use in the POS app.</p>
    </div>
    <form id="loginForm" class="auth-grid">
      <input type="hidden" id="loginMode" value="mobile">
      <input id="emailInput" type="hidden" placeholder="Email">
      <input id="passwordInput" type="hidden" placeholder="Password">
      <div class="phone-input-container">
        <svg class="phone-icon" viewBox="0 0 24 24" width="18" height="18" stroke="currentColor" stroke-width="2.5" fill="none" stroke-linecap="round" stroke-linejoin="round">
          <rect x="5" y="2" width="14" height="20" rx="2" ry="2"></rect>
          <line x1="12" y1="18" x2="12.01" y2="18"></line>
        </svg>
        <span class="phone-prefix">+91</span>
        <div class="phone-divider"></div>
        <input id="mobileInput" type="tel" placeholder="Enter Mobile Number" maxlength="10">
      </div>
      <div class="pin-input-container">
        <input id="pinInput" type="password" inputmode="numeric" maxlength="4" pattern="[0-9]*" placeholder="PIN" autocomplete="off">
        <div class="pin-boxes">
          <div class="pin-box"></div>
          <div class="pin-box"></div>
          <div class="pin-box"></div>
          <div class="pin-box"></div>
        </div>
      </div>
      <button type="submit">Login</button>
    </form>
  </section>
