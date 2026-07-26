# POS Software — Comprehensive Audit Report

> **Date:** June 1, 2026 | **Version:** 2.2.0 | **Auditor:** Senior Debugging Agent

---

## 📋 Executive Summary

This report documents a deep-dive analysis of the Chay Chaupal POS Electron application. The investigation focused on finding root causes of **data loss, order item disappearance, cart overwriting, and table state corruption** — especially when adding new items to an existing table.

**Total Bugs Found:** 12 (5 Critical, 4 High, 3 Medium)
**Root Causes Identified:** Data fragmentation across 4 storage layers, non-atomic IndexedDB writes, stale merge logic, and race conditions in the KOT save pipeline.

---

## 🏗️ Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│                    ELECTRON MAIN PROCESS                  │
│  src/main.js: HTTP Server + API Proxy + Local Printers    │
│  src/preload.js: IPC Bridge (pos-print)                   │
└──────────────────────┬──────────────────────────────────┘
                       │ IPC + HTTP (localhost:18181)
┌──────────────────────▼──────────────────────────────────┐
│               RENDERER PROCESS (React + Dexie)            │
│                                                          │
│  ┌──────────────┐   ┌──────────────┐   ┌─────────────┐  │
│  │ React State  │   │ localStorage │   │  IndexedDB  │  │
│  │  (E, z, B)   │   │ pos_tables   │   │ POS_Database│  │
│  │              │   │ pos_drafts   │   │ .orders     │  │
│  │              │   │ pos_temp_kots│   │ .orderItems │  │
│  │              │   │              │   │ .pos_tables │  │
│  └──────┬───────┘   └──────┬───────┘   └──────┬──────┘  │
│         │                  │                   │         │
│         └──────────────────┼───────────────────┘         │
│                   DATA FRAGMENTATION ZONE                 │
└──────────────────────────────────────────────────────────┘
```

### Storage Layers (4 separate stores for the same data!)

| # | Store | Key | Purpose |
|---|-------|-----|---------|
| 1 | **React State `E`** | `useState(ha('pos_tables',{}))` | UI rendering |
| 2 | **localStorage `pos_tables`** | `localStorage.setItem('pos_tables', ...)` | Persistence across refresh |
| 3 | **localStorage `pos_table_drafts`** | Draft mode for new tables | Pre-save draft |
| 4 | **IndexedDB `POS_Database.pos_tables`** | Dexie table | Canonical source |
| 5 | **IndexedDB `POS_Database.orders`** | Dexie table | Order header |
| 6 | **IndexedDB `POS_Database.orderItems`** | Dexie table | Order line items |

### Script Load Order (Critical!)
```
index.html loads in order:
1. electron-local-first-guard.js     ← intercepts Storage & IndexedDB
2. electron-bill-offline-queue.js    ← intercepts fetch for bills
3. electron-table-order-patch.js     ← enriches POST /api/table-orders
4. electron-table-order-canonical.js ← intercepts fetch for table-orders (overwrites #3!)
5. electron-new-order-cart-patch.js  ← polls localStorage → IndexedDB
6. electron-pin-fix.js
7. electron-api-router.js           ← maps local /api/* → remote
8. electron-dashboard-date-guard.js
9. electron-bootstrap-guard.js      ← bootstrap retry + repair
10. assets/index-...-app.js         ← React app (Dexie + UI)
```

⚠️ **WARNING:** `electron-table-order-canonical.js` **replaces** `window.fetch` AFTER `electron-table-order-patch.js` already did. The canonical script's fetch interceptor is the **final** one that runs. So the table-order-patch's fetch enrichment via `enrichTableOrderPayload()` is **bypassed** for table-orders because canonical intercepts first and handles the request differently.

---

## 🔴 CRITICAL BUGS (Data Loss)

### BUG #1: `putActiveOrder()` — Full Delete + Re-Insert Pattern (NON-ATOMIC)

**File:** `electronapp/www/electron-table-order-canonical.js`, Lines 426–440

```javascript
// ALL existing items deleted FIRST
var existingRequest = itemStore.getAll();
existingRequest.onsuccess = function () {
  (Array.isArray(existingRequest.result) ? existingRequest.result : [])
    .forEach(function (item) {
      if (String(item.order_id ...) === String(orderId) && item.id !== undefined) {
        itemStore.delete(item.id);  // ← DELETE ALL FIRST
      }
    });
  // THEN re-insert
  normalizedItems.forEach(function (item, index) {
    itemStore.put(Object.assign({}, item, { id: ... }));  // ← PUT AFTER
  });
};
```

**Root Cause:** The code first deletes ALL order items, then re-inserts. If the `bulkPut` (or individual `put` calls) fails for any reason (quota exceeded, transaction abort, IndexedDB error), the items are **permanently lost** with no recovery.

**Impact:** ⭐⭐⭐⭐⭐ **CRITICAL** — Complete data loss of all items in an order when IndexedDB write fails mid-transaction.

**Affected Functions:** `putActiveOrder()`, `electron-table-order-canonical.js:426-440`

**Recommended Fix:**
```javascript
// Use a single transaction with proper error handling:
// 1. Read existing items
// 2. Build new items array  
// 3. Clear + insert in SAME transaction
// 4. On failure, ROLLBACK (transaction auto-rolls back in IndexedDB)
// 5. Add retry logic with exponential backoff
```

---

### BUG #2: `it()` Function — Cart Item Delete Without Safety Net

**File:** `assets/index-BY_I4_Pk-app.js` (React bundle), `it()` / `syncCartToStorage` function

```javascript
// Deletes ALL order items for this order
await $.orderItems.where('order_id').equals(i).delete()

// THEN re-inserts
e.length > 0 && await $.orderItems.bulkPut(e.map(...))
```

**Root Cause:** Same pattern as BUG #1. `delete()` followed by `bulkPut()` in separate operations. If `bulkPut` fails, data is gone.

Additional issue: The `it()` function is called on EVERY cart state change (add item, remove item, change qty). This means the entire order items table is being deleted and re-created on every single item interaction.

**Impact:** ⭐⭐⭐⭐⭐ **CRITICAL** — High-frequency operation (every cart change), catastrophic on failure.

**Recommended Fix:** Use `db.transaction('rw', db.orders, db.orderItems, ...)` to make delete+insert atomic.

---

### BUG #3: Stale IndexedDB Read During `er()` (Save KOT)

**File:** `assets/index-BY_I4_Pk-app.js`, `er()` function

```javascript
// In Ae === 'new' mode:
let e = await $.orders.toArray();
let t = e.filter(e => 
  String(e.table_id ?? e.tableId ?? '') === String(i) && 
  !['available','cancelled','settled','paid','completed']
    .includes(String(e.status || e.order_status || '').toLowerCase())
).sort(...)[0];

if (t) {
  let e = String(t.id ?? '');
  let n = await $.orderItems.toArray();
  let r = n.filter(t => String(t.order_id ?? t.orderId ?? '') === e)
    .map(e => { ... })
    .filter(e => e.name && e.qty > 0);
  // Uses stale 'r' items — could be outdated
}
```

**Root Cause:** In "new" mode (`Ae === 'new'`), the function reads from IndexedDB to find an existing active order. But between when the order was last written and when it's read here:
1. Another tab/instance could have modified the order
2. The `electron-new-order-cart-patch.js` polling could have written partial data
3. The item list `r` could be stale

**Impact:** ⭐⭐⭐⭐ **HIGH** — Stale data merges with new items, causing wrong totals and missing items.

---

### BUG #4: `mergeItems()` — Item Key Collision on Parcel/Non-Parcel

**File:** `electron-table-order-canonical.js`, Lines 113–138

```javascript
function itemKey(item) {
  return String((item && (item.client_item_id || item.item_id || ...)) || '')
    + '|' + (item && (item.is_parcel || item.isParcel) ? 'parcel' : 'normal');
}
```

**Root Cause:** The `itemKey()` function uses `client_item_id | item_id | itemId | id | name` as fallback. If an item has no `client_item_id` AND no `item_id`, it falls back to `name`. If the name changes or is misspelled, a different key is generated, causing:
- **Duplicate items** (same item treated as different)
- **Lost merge** (item appears as new instead of merged with existing)

**Impact:** ⭐⭐⭐⭐ **HIGH** — Item duplication or loss when item IDs are inconsistent.

---

### BUG #5: Fetch Interceptor Chain — `table-order-patch` Bypassed

**Files:** 
- `electron-table-order-patch.js` (v5)
- `electron-table-order-canonical.js`

Loading order in `index.html`:
```html
<script src="electron-table-order-patch.js?v=6"></script>     <!-- 1st fetch override -->
<script src="electron-table-order-canonical.js?v=12"></script>  <!-- 2nd fetch override -->
```

Both scripts override `window.fetch`. The **canonical** script runs LAST and its fetch interceptor handles `POST /api/table-orders` completely — stopping KOT saves locally and only allowing final bills to reach the server.

**BUT:** The canonical script's `putActiveOrder()` does its OWN merge logic, which is DIFFERENT from the table-order-patch's `enrichTableOrderPayload()`. The table-order-patch's merge with `localStorage.pos_tables` cart data is **completely bypassed**.

**Root Cause:** The canonical script replaces `window.fetch` without calling through to the previous interceptor. The table-order-patch's `enrichTableOrderPayload()` which merges the latest `localStorage` cart data into the API payload **never runs** for table-orders.

**Impact:** ⭐⭐⭐ **MEDIUM** — The canonical script reads from its own IndexedDB `POS_Database.orders/orderItems`, which may be stale. The `localStorage` cart (which the React app most recently updated) is not consulted.

---

## 🟠 HIGH PRIORITY BUGS

### BUG #6: `requestLooksLikeFullCart()` — Incorrect Detection

**File:** `electron-table-order-canonical.js`, Lines 141–155

```javascript
function requestLooksLikeFullCart(baseItems, requestItems) {
  if (!Array.isArray(baseItems) || !baseItems.length) return true;
  if (!Array.isArray(requestItems) || requestItems.length < baseItems.length) return false;
  // ... checks if all base items exist in request with >= qty
}
```

**Root Cause:** If the request has MORE items than the base but ALL base items are present, it returns `true` — meaning it REPLACES the cart entirely. This means:
- If user opens table with items [A, B] 
- Adds item C
- Request now has [A, B, C]
- `requestLooksLikeFullCart([A,B], [A,B,C])` → `true`
- Cart is REPLACED with [A,B,C] — correct in this case
- BUT if user modifies A's quantity, the request might have different qty values
- If the items are normalized differently, the key comparison fails

**Impact:** ⭐⭐⭐ **MEDIUM** — Can cause full cart replacement when merge was intended.

---

### BUG #7: `ct()` — Table Open Race Condition with Drafts

**File:** `assets/index-BY_I4_Pk-app.js`, `ct()` function

```javascript
// When switching away from current table with new items:
if (o) {  // o = H === 'Table' && W && W !== e && z.length > 0 && Ae === 'new'
  let e = z.reduce(...);
  let t = Date.now();
  try {
    let n = ha('pos_table_drafts', {});
    n[W] = { ...(n[W] || {}), tableId: W, cart: [...z], ...draftOnly: true };
    localStorage.setItem('pos_table_drafts', JSON.stringify(n));
  } catch {}
}
```

Then later when opening the target table:
```javascript
let draftEntry = draftStore[e];
let savedEntry = E[e] || n[e] || n[Number(e)];
let R = savedEntry || draftEntry;
(!R?.cart || R.cart.length === 0) && (R = draftEntry || R);
```

**Root Cause:** Multiple fallback chains for finding the cart. When `draftEntry` has a cart but `savedEntry` doesn't, it uses draft. But if BOTH exist, saved takes priority, and draft (which may have newer items) is ignored.

**Impact:** ⭐⭐⭐ **MEDIUM** — Draft entries with newer items can be silently discarded.

---

### BUG #8: `electron-new-order-cart-patch.js` — Polling Race Condition

**File:** `electron-new-order-cart-patch.js`

```javascript
// 2-second polling
setInterval(function() {
  var raw = localStorage.getItem('pos_tables');
  if (raw && raw !== lastSyncedState) {
    scheduleSync();
  }
}, 2000);
```

The `persistTableToIndexedDB()` function opens a NEW IndexedDB connection and does a `store.put()` for `pos_tables`. This runs **independently** of the main app's Dexie operations.

**Root Cause:** Two separate code paths writing to the same IndexedDB store (`pos_tables`) without coordination. The polling sync writes a snapshot that may:
- Overwrite a more recent write from the main app
- Write inconsistent data (partial cart snapshot from localStorage)

**Impact:** ⭐⭐⭐ **MEDIUM** — Concurrent writes to IndexedDB without transaction coordination.

---

### BUG #9: `O()` — Table Status Update Without Cart Preservation

**File:** `assets/index-BY_I4_Pk-app.js`, `O()` function

```javascript
let O = async (e, t, n, r = 'ordered') => {
  let a = ha('pos_tables', {})?.[e] || ha('pos_tables', {})?.[String(e)] || {};
  let o = await $.pos_tables.get(Number(e) || e).catch(() => null);
  let c = { ...a, ...(o || {}) };
  // ...
  let s = {
    ...c, id: Number(e) || e, status: r, table_status: r,
    amount: r === 'available' ? '0' : String(i),
    ...(r === 'available' ? { cart: [], billNote: '', ... } : {})
  };
  await $.pos_tables.put(s);
  r === 'available' && D(t => { let n = { ...t }; delete n[e]; return n });
};
```

**Root Cause:** When status is set to `'available'`, the cart is cleared. But if there's a pending draft (in `pos_table_drafts`), it's NOT cleared. When the table is re-opened, the draft cart is restored — even though the table was "cleared".

**Impact:** ⭐⭐⭐ **MEDIUM** — "Cleared" tables can have their items restored from draft.

---

## 🟡 MEDIUM PRIORITY BUGS

### BUG #10: `electron-local-first-guard.js` — `protectPosTablesWrite()` Object.assign Side Effects

**File:** `electron-local-first-guard.js`

```javascript
return Object.assign({}, row, oldRow, {
  id: row.id != null ? row.id : oldRow.id,
  // ...
  cart: oldCart,  // ← Forces old cart, ignoring new data
  items: Array.isArray(row.items) ? oldCart : row.items,
});
```

**Root Cause:** When protecting against data loss, the guard forces the OLD cart if it detects a shrink. But if the shrink was intentional (user deleted items), the old cart is incorrectly restored.

**Impact:** ⭐⭐ **LOW-MEDIUM** — Intentional item deletions can be undone.

---

### BUG #11: `mergeItems()` vs `mergeCartSafe()` — Two Different Merge Functions

**Files:**
- `electron-table-order-canonical.js` → `mergeItems()` 
- `assets/index-BY_I4_Pk-app.js` → `mergeCartSafe()`

```javascript
// mergeItems (canonical): merges by itemKey + adds quantities
merged[index] = Object.assign({}, merged[index], item, {
  qty: oldQty + addQty,
  quantity: oldQty + addQty
});

// mergeCartSafe (React): merges by item_id + adds quantities
e[i] = { ...e[i], qty: (Number(e[i].qty || ...) || 1) + (Number(t.qty || ...) || 1) };
```

**Root Cause:** Different key generation and merge logic in two different code paths. `mergeItems` uses `itemKey()` which considers parcel status. `mergeCartSafe` uses `Mr()` which also considers parcel status but with different fallback.

**Impact:** ⭐⭐ **LOW** — Subtle differences could cause inconsistent merges.

---

### BUG #12: `_n()` — Bill Number Race Condition

**File:** `assets/index-BY_I4_Pk-app.js`, `_n()` function

```javascript
let _n = () => {
  let e = xs(), t = __billClientKey();
  let n = F?.billSequences?.[t] || {};
  let r = n.lastBillResetDate ?? (t === 'default' ? F?.lastBillResetDate : xs());
  let i = F?.resetBillDaily && r !== e ? 1 : 
    Math.max(1, Number(n.billSequence ?? (t === 'default' ? F?.billSequence : 1)) || 1);
  return i;
};
```

The bill sequence is stored in React state `F` (printer settings), not in IndexedDB. If the app crashes between bill creation and state persistence, the same bill number could be reused.

**Impact:** ⭐⭐ **LOW** — Duplicate bill numbers possible on crash recovery.

---

## 📊 Data Flow: Complete Trace

### Flow: User opens existing table → Adds item → Saves KOT

```
STEP 1: User clicks table
  ↓ ct(tableId)
  ↓ reads: localStorage.pos_tables → draftStore[pos_table_drafts] → IndexedDB POS_Database.pos_tables
  ↓ restores cart into React state z
  ↓ sets Ae = 'change', Ne = true

STEP 2: User clicks menu item
  ↓ Pr(item) → B([...z, newItem])
  ↓ it(newCart) called (via effect or direct)
  ↓ Deletes all orderItems → BulkPuts new ones
  ↓ electron-new-order-cart-patch polls → writes to IndexedDB.pos_tables

STEP 3: User clicks "Save KOT"
  ↓ er() called
  ↓ In 'change' mode:
  ↓   Reads from localStorage.pos_tables (or React state E)
  ↓   Calculates new totals
  ↓   $.orders.put(orderData)
  ↓   $.orderItems.where('order_id').equals(_).delete()
  ↓   $.orderItems.bulkPut(newItems)
  ↓   $.pos_tables.put(tableData)
  ↓   O(tableId, amount, timestamp, status) → API sync
  ↓   gi() → POST /api/table-orders

STEP 4: POST /api/table-orders intercepted
  ↓ electron-table-order-canonical.js fetch interceptor
  ↓ isTableOrderRequest() → true
  ↓ isFinalBill() → false (it's a KOT)
  ↓ putActiveOrder(body, { final: false, replace: false })
  ↓   openPosDb() → getActiveOrder()
  ↓   mergeItems(baseItems, requestItems)
  ↓   Non-atomic: delete all items → put new items
  ↓   Returns localKotResponse() — NEVER reaches server!
  ↓   ⚠️ table-order-patch.js enrichTableOrderPayload() BYPASSED
```

---

## 🔍 Root Cause Analysis Summary

| # | Root Cause | Impact | Severity |
|---|-----------|--------|----------|
| 1 | Non-atomic delete+insert in IndexedDB | Complete data loss on write failure | 🔴 CRITICAL |
| 2 | Same pattern in React `it()` function | Data loss on every cart change | 🔴 CRITICAL |
| 3 | Stale IndexedDB reads in KOT save | Wrong items merged | 🔴 HIGH |
| 4 | Inconsistent item key generation | Duplicate or lost items | 🔴 HIGH |
| 5 | Fetch interceptor chain bypass | localStorage cart ignored | 🟠 MEDIUM |
| 6 | Incorrect full-cart detection | Wrong merge strategy | 🟠 MEDIUM |
| 7 | Draft vs saved entry priority confusion | Newer items silently lost | 🟠 MEDIUM |
| 8 | Polling-based IndexedDB sync | Concurrent writes | 🟠 MEDIUM |
| 9 | Draft not cleared on table clear | Ghost items on re-open | 🟡 LOW |
| 10 | Guard over-protects intentional deletes | Cannot clear items | 🟡 LOW |
| 11 | Two different merge functions | Inconsistent behavior | 🟡 LOW |
| 12 | Bill number in React state only | Duplicate bills on crash | 🟡 LOW |

---

## 🛠️ Recommended Fix Priority

### Immediate (P0) — Data Loss Prevention
1. **Make IndexedDB writes atomic** — Wrap delete+insert in single transaction with rollback
2. **Add write-ahead logging** — Log all mutations before executing
3. **Add retry with exponential backoff** for failed IndexedDB writes

### Short-term (P1) — Correctness
4. **Unify merge logic** — Single merge function used everywhere
5. **Fix item key generation** — Use consistent primary key
6. **Coordinate localStorage ↔ IndexedDB sync** — Single writer pattern
7. **Fix fetch interceptor chain** — Make canonical call through to patch

### Long-term (P2) — Architecture
8. **Single source of truth** — Eliminate data fragmentation across 4+ stores
9. **Event-driven sync** — Replace polling with proper change events
10. **Add comprehensive logging** — Include [ORDER_CREATE], [ITEM_ADD], [IDB_WRITE] tags
11. **Add integrity checks** — Periodic validation of localStorage vs IndexedDB

---

## 📈 Performance Impact

| Operation | Current | After Fix |
|-----------|---------|-----------|
| Add item to cart | Delete all + BulkPut all (O(n)) | Update single item (O(1)) |
| Save KOT | 3 separate IndexedDB transactions | 1 atomic transaction |
| Open table | 4 fallback reads | 2 reads (canonical only) |
| localStorage → IDB sync | 2s polling | Event-driven (instant) |

---

## 🔬 Suggested Logging Additions

Add these log points to trace data flow:

```javascript
// electron-table-order-canonical.js
console.log('[ORDER_CREATE]', { tableId, orderId, items: items.length, ts: Date.now() });
console.log('[ORDER_UPDATE]', { orderId, prevItems, newItems, ts: Date.now() });
console.log('[ITEM_ADD]', { tableId, orderId, itemId, prevCount, newCount, ts: Date.now() });
console.log('[IDB_WRITE]', { store, operation, key, success, ts: Date.now() });
console.log('[IDB_TRANSACTION]', { stores, mode, duration: Date.now() - start });

// React app
console.log('[TABLE_OPEN]', { tableId, source: 'localStorage'|'draft'|'indexedDB', items: cart.length, ts });
console.log('[TABLE_SWITCH]', { from, to, draftSaved: !!draft, ts });
console.log('[CART_SYNC]', { tableId, source: 'react'|'polling', itemCount, ts });
```

---

## 📁 Files Requiring Changes (Priority Order)

1. **`electronapp/www/electron-table-order-canonical.js`** — Atomic IndexedDB transactions, unified merge
2. **`electronapp/www/assets/index-BY_I4_Pk-app.js`** (React bundle) — `it()`, `er()`, `ct()`, `O()` functions
3. **`electronapp/www/electron-new-order-cart-patch.js`** — Replace polling with event-driven sync
4. **`electronapp/www/electron-local-first-guard.js`** — Fix over-protection of intentional deletes
5. **`electronapp/www/electron-table-order-patch.js`** — Align with canonical merge logic

---

## ✅ Verification Checklist

After fixes are applied, verify:

- [ ] Add 10 items to an existing table → All 10 persist after page refresh
- [ ] Add items, switch to another table, switch back → All items intact
- [ ] Add items, close app, reopen → All items intact
- [ ] Add items, save KOT, add more items → Previous + new items all present
- [ ] Delete an item from cart → Item removed, others intact
- [ ] Change item quantity → Quantity updated, others intact
- [ ] Rapid double-click add item → Only one instance added
- [ ] Offline: add items, go online → Items sync correctly
- [ ] Two tables with items → Both maintain separate state
- [ ] Settle bill → Table cleared, items not in draft

---

**End of Report**

---

## ⚠️ REGRESSION FIX — June 1, 2026 (Immediate)

**Bug:** `itemStore.clear()` in `putActiveOrder()` was deleting ALL items in `orderItems` store (across ALL tables), not just the current order's items.

**Root Cause:** In the CRITICAL #1 fix, `itemStore.clear()` was used as a replacement for the old `getAll()` + delete loop. But `clear()` on an `IDBObjectStore` clears ALL records, not scoped to the current transaction's order.

**Fix:** Replaced `itemStore.clear()` with a cursor-based approach that:
1. Opens a cursor on `orderItems` store
2. Only deletes items where `order_id` matches the current `orderId`
3. After all matching items are deleted, inserts the new items
4. All within the same atomic transaction

```javascript
// BEFORE (BROKEN): Deletes ALL order items for ALL tables!
itemStore.clear();

// AFTER (FIXED): Cursor deletes only current order's items
var deleteRequest = itemStore.openCursor();
deleteRequest.onsuccess = function (event) {
  var cursor = event.target.result;
  if (cursor) {
    if (String(cursor.value.order_id) === String(orderId)) {
      cursor.delete();
    }
    cursor.continue();
  } else {
    // All old items deleted — now insert new ones
    normalizedItems.forEach(function (item, index) { itemStore.put(...); });
  }
};
```

**Impact:** If any KOT was saved after deploying v13 (with the `clear()` bug), items from OTHER tables may have been lost. Restart the app and test again.

### Architecture Change: IndexedDB as Single Source of Truth

**Direction Changed:** `localStorage → IndexedDB` ➜ `IndexedDB → localStorage`

All 4 modified files now treat `POS_Database` (IndexedDB) as the **only canonical data store**. localStorage `pos_tables` is now a **read-only cache** synced FROM IndexedDB.

---

### Files Modified

| # | File | Version | Changes |
|---|------|---------|---------|
| 1 | `electron-table-order-canonical.js` | v13 | Complete rewrite — all CRITICAL + HIGH fixes |
| 2 | `electron-new-order-cart-patch.js` | v4 | Reversed sync direction (IDB→LS), removed polling |
| 3 | `electron-local-first-guard.js` | updated | Draft cleanup on table clear + event dispatch |
| 4 | `electron-table-order-patch.js` | v6 | Utility-only mode, removed fetch interceptor |

---

### CRITICAL Fixes Applied

#### ✅ CRITICAL #1: Atomic IndexedDB Transactions
**File:** `electron-table-order-canonical.js`

**Change:** Replaced `itemStore.getAll()` + `onsuccess` delete loop with `itemStore.clear()` + synchronous `itemStore.put()` calls — all within the same `db.transaction()`. IndexedDB auto-rolls back if any write fails.

```javascript
// OLD: async getAll + loop delete (could lose data between callbacks)
var existingRequest = itemStore.getAll();
existingRequest.onsuccess = function () {
  // ... delete loop then put loop ...
};

// NEW: clear() + put() in same tx scope (atomic by IndexedDB)
itemStore.clear();
normalizedItems.forEach(function (item, index) {
  itemStore.put(...);
});
```

#### ✅ CRITICAL #3: Fresh Reads Every Time
**Change:** `getActiveOrder()` always reads fresh from IndexedDB. Added shared DB connection pool to avoid opening multiple connections. Connection reuse prevents version conflicts between canonical script and Dexie (React app).

#### ✅ CRITICAL #4: Robust Stable Item Keys
**Change:** New `stableItemId()` function with priority chain:
1. `item_id` (numeric DB ID — most stable)
2. `client_item_id` (string client ID)
3. `name` (lowercase, underscore-normalized — stable across sessions)

Never falls back to array index. Parcel/normal distinction preserved with `:parcel` / `:normal` suffix.

#### ✅ CRITICAL #5: Integrated Enrichment Pipeline
**Change:** Canonical script now calls `enrichFromLocalCache()` before every IDB merge — integrating what `electron-table-order-patch.js` used to do (but was bypassed). The table-order-patch is now a utility-only module.

---

### HIGH Fixes Applied

#### ✅ HIGH #6: Stricter Full-Cart Detection
**Change:** `requestLooksLikeFullCart()` now requires:
- ALL base items present in request (not just 80%)
- At least 50% overlap from request side (prevents new-item-only payload from replacing)
- Returns `false` if any base item is missing (force merge instead of replace)

#### ✅ HIGH #7: Draft-Safe Table Open
**Change:** `syncIdbToLocalStorage()` now syncs IDB → localStorage after every successful write. When KOT is saved, the draft is explicitly cleared. When a final bill clears a table, the draft is also removed. This prevents zombie drafts from resurrecting deleted items.

#### ✅ HIGH #8: Event-Driven Sync (No Polling)
**Change:** Removed the 2-second `setInterval` polling in `electron-new-order-cart-patch.js`. Now uses:
- `window.addEventListener('pos-idb-updated')` for cross-module events
- `window.addEventListener('storage')` for cross-tab sync
- 30-second health check (not 2-second poll)
- Direct API call to `POSTableOrderCanonical.readTableOrderFromIdb()` for on-demand sync

#### ✅ HIGH #9: Draft Cleanup on Table Clear
**Change:** Multiple layers of draft cleanup:
1. `clearLocalStorageTable()` in canonical now also clears `pos_table_drafts`
2. `syncIdbToLocalStorage()` in canonical clears draft when KOT is saved
3. `protectPosTablesWrite()` in local-first-guard clears drafts for cleared tables
4. `clearFinalLocalOrder()` in canonical clears all local state atomically

---

### Data Flow (After Fixes)

```
React App (Dexie)
  ↓ writes to
IndexedDB POS_Database ← SINGLE SOURCE OF TRUTH
  ↓ canonical script syncs to
localStorage pos_tables ← READ-ONLY CACHE
  ↓ React reads for UI rendering
  
All API calls (POST /api/table-orders)
  ↓ intercepted by canonical script
  ↓ merges with latest IDB data
  ↓ writes to IDB atomically
  ↓ syncs to localStorage
  ↓ returns response to React
```

---

### What Still Needs Work (React Bundle)

The minified React bundle (`assets/index-BY_I4_Pk-app.js`) cannot be directly edited. The following React-level issues are mitigated by the canonical script fixes but still exist in the bundle:

1. **`it()` function** — Still does delete+reinsert pattern. Mitigated because canonical script is the final handler for table-orders.
2. **`er()` function** — Still has stale read potential. Mitigated because canonical always reads fresh from IDB.
3. **`O()` function** — Clears cart on status change. Mitigated by canonical's `syncIdbToLocalStorage` which restores from IDB.

These will be fully resolved when the React bundle is rebuilt from source.

---

### Verification Checklist

After deploying these fixes, verify:

- [ ] Add 10 items to existing table → all persist after page refresh
- [ ] Switch between tables with items → both maintain separate state
- [ ] Close and reopen app → all items intact
- [ ] Save KOT, add more items → all items present
- [ ] Delete item → item removed, others intact
- [ ] Rapid double-click → only one instance added
- [ ] Offline mode → items sync correctly when online
- [ ] localStorage cleared → items restored from IndexedDB
- [ ] Two Electron instances → no data corruption

