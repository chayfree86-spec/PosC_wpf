(function () {
  // Override HTMLSelectElement value property to dispatch dynamic sync events
  try {
    const originalValueDescriptor = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value');
    Object.defineProperty(HTMLSelectElement.prototype, 'value', {
      get() {
        return originalValueDescriptor.get.call(this);
      },
      set(val) {
        originalValueDescriptor.set.call(this, val);
        this.dispatchEvent(new CustomEvent('select-value-synced'));
      }
    });
  } catch (e) {
    console.error('Failed to intercept select value property:', e);
  }

  const storageKey = 'pos_menu_admin_session';
   const state = {
    clients: [],
    token: '',
    client: '',
    categories: [],
    items: [],
    tables: [],
    areas: [],
    gallery: [],
    currentSuggestions: [],
    itemImages: [], // Active images array for the currently open Menu Item form
    returningToTableForm: false,
    tempTableNumber: '',
    tempTableId: '',
    activeAssignItemId: null,
    directUploadItemId: null,
    filePickerOpen: false,
    billSearchQuery: '',
    billCurrentPage: 1,
    itemsCurrentPage: 1,
    reportsDetailsTab: 'items',
    mobileDownloadImages: [],
    needsForceReload: false
  };

  const $ = (id) => {
    const el = document.getElementById(id);
    if (el) return el;
    
    const dummy = document.createElement('div');
    dummy.isMock = true;
    dummy.value = '';
    dummy.checked = false;
    dummy.options = [];
    dummy.addEventListener = () => {};
    dummy.dispatchEvent = () => {};
    dummy.removeAttribute = () => {};
    dummy.setAttribute = () => {};
    dummy.getAttribute = () => null;
    
    return dummy;
  };
  const listen = (id, event, callback) => {
    const el = document.getElementById(id);
    if (el && !el.isMock) el.addEventListener(event, callback);
  };

  const bindSafe = (id, event, callback) => {
    const el = document.getElementById(id);
    if (!el || el.isMock) return;
    const prop = `has_${event}_listener`;
    if (!el[prop]) {
      el.addEventListener(event, callback);
      el[prop] = true;
    }
  };

  function getCachedData(key) {
    try {
      const cacheKey = `pos_cache_${state.client}_${key}`;
      const cached = localStorage.getItem(cacheKey);
      return cached ? JSON.parse(cached) : null;
    } catch (e) {
      return null;
    }
  }

  function setCachedData(key, data) {
    try {
      const cacheKey = `pos_cache_${state.client}_${key}`;
      localStorage.setItem(cacheKey, JSON.stringify(data));
    } catch (e) {}
  }
  const configuredApiBase = resolveApiBase();
  const baseMenuUrl = (() => {
    let path = window.location.pathname;
    if (path.endsWith('/')) {
      path = path.slice(0, -1);
    } else if (path.endsWith('.php')) {
      path = path.substring(0, path.lastIndexOf('/'));
    }
    return window.location.origin + path;
  })();

  function endpoint(path) {
    const [pathPart, queryPart] = String(path || '').split('?');
    const cleanPath = ('/api/' + pathPart.replace(/^\/+/, '')).replace(/\/+$/, '');
    const url = new URL(configuredApiBase.toString());

    if (url.searchParams.has('__api')) {
      // Send the endpoint's own parameters as TOP-LEVEL query params instead
      // of packing them inside __path: PHP only populates $_GET from the real
      // query string, so parameters hidden inside __path never reached the
      // server (date/range/report_client were silently dropped -> reports
      // always showed today / logged-in client).
      url.searchParams.set('__path', cleanPath);
      if (queryPart) {
        new URLSearchParams(queryPart).forEach((val, key) => {
          url.searchParams.set(key, val);
        });
      }
      return url.toString();
    }

    url.pathname = (url.pathname.replace(/\/+$/, '') + '/' + cleanPath.replace(/^\/api\/?/, '')).replace(/\/{2,}/g, '/');
    if (queryPart) {
      const searchParams = new URLSearchParams(queryPart);
      searchParams.forEach((val, key) => {
        url.searchParams.set(key, val);
      });
    }
    return url.toString();
  }

  function resolveApiBase() {
    const params = new URLSearchParams(window.location.search);
    const metaApiUrl = document.querySelector('meta[name="pos-api-url"]')?.content || '';
    const localProxy = new URL('proxy.php?__api=1', window.location.href).toString();
    const candidates = [
      params.get('api_url'),
      params.get('api'),
      window.POS_API_URL,
      localProxy,
      metaApiUrl,
      localStorage.getItem('pos_api_base')
    ].filter(Boolean);

    for (const candidate of candidates) {
      try {
        const url = new URL(candidate, window.location.href);
        localStorage.setItem('pos_api_base', url.toString());
        return url;
      } catch {
        // Ignore invalid saved/typed values and continue to the next candidate.
      }
    }

    if (/friendpos\.com$/i.test(window.location.hostname)) {
      return new URL('/index.php?__api=1', window.location.origin);
    }

    return new URL('../', window.location.href);
  }

  function headers() {
    const result = { 'Content-Type': 'application/json' };
    if (state.client) {
      result['X-POS-Client'] = state.client;
    }
    if (state.token) {
      result.Authorization = 'Bearer ' + state.token;
    }
    return result;
  }

  async function api(path, options = {}) {
    const method = (options.method || 'GET').toUpperCase();
    if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(method)) {
      state.needsForceReload = true;
    }
    const response = await fetch(endpoint(path), {
      ...options,
      headers: { ...headers(), ...(options.headers || {}) }
    });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok || payload.success === false) {
      if (response.status === 401 || response.status === 403) {
        state.token = '';
        saveSession();
      }
      throw new Error(payload.message || 'Request failed.');
    }
    return payload.data;
  }


  function saveSession() {
    localStorage.setItem(storageKey, JSON.stringify({
      token: state.token,
      client: state.client
    }));
  }

  function loadSession() {
    try {
      const saved = JSON.parse(localStorage.getItem(storageKey) || '{}');
      state.token = saved.token || '';
      state.client = saved.client || '';
    } catch {
      state.token = '';
      state.client = '';
    }
  }

  function toast(message) {
    const node = $('toast');
    node.textContent = message;
    node.classList.add('show');
    window.clearTimeout(toast.timer);
    toast.timer = window.setTimeout(() => node.classList.remove('show'), 2600);
  }

  function showConfirm(title, message, confirmText = 'Yes, Delete', cancelText = 'Cancel') {
    return new Promise((resolve) => {
      const overlay = document.createElement('div');
      overlay.className = 'modal-overlay';
      overlay.style.setProperty('display', 'flex', 'important');
      overlay.style.setProperty('opacity', '0', 'important');
      overlay.style.zIndex = '99999';
      
      const content = document.createElement('div');
      content.className = 'modal-content compact';
      content.style.padding = '24px';
      content.style.maxWidth = '380px';
      content.style.textAlign = 'center';
      content.style.background = 'rgba(22, 28, 45, 0.96)';
      content.style.backdropFilter = 'blur(24px)';
      content.style.border = '1px solid rgba(255, 255, 255, 0.1)';
      content.style.borderRadius = '16px';
      
      const header = document.createElement('h3');
      header.style.color = '#fff';
      header.style.fontSize = '16px';
      header.style.fontWeight = '700';
      header.style.marginBottom = '12px';
      header.style.display = 'flex';
      header.style.alignItems = 'center';
      header.style.justifyContent = 'center';
      header.style.gap = '8px';
      header.innerHTML = `⚠️ ${escapeHtml(title)}`;
      
      const body = document.createElement('p');
      body.style.fontSize = '13px';
      body.style.color = 'var(--muted)';
      body.style.lineHeight = '1.5';
      body.style.marginBottom = '20px';
      body.style.fontWeight = '500';
      body.textContent = message;
      
      const btnGroup = document.createElement('div');
      btnGroup.style.display = 'flex';
      btnGroup.style.gap = '10px';
      btnGroup.style.justifyContent = 'center';
      
      const cancelBtn = document.createElement('button');
      cancelBtn.type = 'button';
      cancelBtn.style.flex = '1';
      cancelBtn.style.padding = '10px 16px';
      cancelBtn.style.fontSize = '12px';
      cancelBtn.style.fontWeight = '600';
      cancelBtn.style.borderRadius = 'var(--radius-sm)';
      cancelBtn.style.border = '1px solid rgba(255, 255, 255, 0.08)';
      cancelBtn.style.background = 'rgba(255, 255, 255, 0.04)';
      cancelBtn.style.color = 'var(--ink)';
      cancelBtn.style.cursor = 'pointer';
      cancelBtn.style.transition = 'all 0.2s ease';
      cancelBtn.textContent = cancelText;
      cancelBtn.addEventListener('mouseenter', () => {
        cancelBtn.style.background = 'rgba(255, 255, 255, 0.08)';
      });
      cancelBtn.addEventListener('mouseleave', () => {
        cancelBtn.style.background = 'rgba(255, 255, 255, 0.04)';
      });
      
      const confirmBtn = document.createElement('button');
      confirmBtn.type = 'button';
      confirmBtn.style.flex = '1';
      confirmBtn.style.padding = '10px 16px';
      confirmBtn.style.fontSize = '12px';
      confirmBtn.style.fontWeight = '600';
      confirmBtn.style.borderRadius = 'var(--radius-sm)';
      confirmBtn.style.border = 'none';
      confirmBtn.style.background = 'linear-gradient(135deg, #ef4444 0%, #dc2626 100%)';
      confirmBtn.style.color = '#fff';
      confirmBtn.style.cursor = 'pointer';
      confirmBtn.style.transition = 'all 0.2s ease';
      confirmBtn.style.boxShadow = '0 4px 12px rgba(239, 68, 68, 0.25)';
      confirmBtn.textContent = confirmText;
      confirmBtn.addEventListener('mouseenter', () => {
        confirmBtn.style.transform = 'translateY(-1px)';
        confirmBtn.style.boxShadow = '0 6px 16px rgba(239, 68, 68, 0.35)';
      });
      confirmBtn.addEventListener('mouseleave', () => {
        confirmBtn.style.transform = 'translateY(0)';
        confirmBtn.style.boxShadow = '0 4px 12px rgba(239, 68, 68, 0.25)';
      });
      
      btnGroup.appendChild(cancelBtn);
      btnGroup.appendChild(confirmBtn);
      content.appendChild(header);
      content.appendChild(body);
      content.appendChild(btnGroup);
      overlay.appendChild(content);
      document.body.appendChild(overlay);
      
      setTimeout(() => {
        overlay.style.opacity = '1';
        overlay.classList.add('active');
      }, 20);
      
      cancelBtn.addEventListener('click', () => {
        close();
        resolve(false);
      });
      
      confirmBtn.addEventListener('click', () => {
        close();
        resolve(true);
      });
      
      overlay.addEventListener('click', (e) => {
        if (e.target === overlay) {
          close();
          resolve(false);
        }
      });
      
      function close() {
        overlay.style.opacity = '0';
        overlay.classList.remove('active');
        setTimeout(() => {
          overlay.remove();
        }, 300);
      }
    });
  }

  function showAlert(title, message, btnText = 'OK') {
    return new Promise((resolve) => {
      const overlay = document.createElement('div');
      overlay.className = 'modal-overlay';
      overlay.style.setProperty('display', 'flex', 'important');
      overlay.style.setProperty('opacity', '0', 'important');
      overlay.style.zIndex = '99999';
      
      const content = document.createElement('div');
      content.className = 'modal-content compact';
      content.style.padding = '24px';
      content.style.maxWidth = '380px';
      content.style.textAlign = 'center';
      content.style.background = 'rgba(22, 28, 45, 0.96)';
      content.style.backdropFilter = 'blur(24px)';
      content.style.border = '1px solid rgba(255, 255, 255, 0.1)';
      content.style.borderRadius = '16px';
      
      const header = document.createElement('h3');
      header.style.color = '#fff';
      header.style.fontSize = '16px';
      header.style.fontWeight = '700';
      header.style.marginBottom = '12px';
      header.style.display = 'flex';
      header.style.alignItems = 'center';
      header.style.justifyContent = 'center';
      header.style.gap = '8px';
      header.innerHTML = `⚠️ ${escapeHtml(title)}`;
      
      const body = document.createElement('p');
      body.style.fontSize = '13px';
      body.style.color = 'var(--muted)';
      body.style.lineHeight = '1.5';
      body.style.marginBottom = '20px';
      body.style.fontWeight = '500';
      body.style.whiteSpace = 'pre-line';
      body.textContent = message;
      
      const btnGroup = document.createElement('div');
      btnGroup.style.display = 'flex';
      btnGroup.style.justifyContent = 'center';
      
      const okBtn = document.createElement('button');
      okBtn.type = 'button';
      okBtn.style.padding = '10px 24px';
      okBtn.style.fontSize = '12px';
      okBtn.style.fontWeight = '600';
      okBtn.style.borderRadius = 'var(--radius-sm)';
      okBtn.style.border = 'none';
      okBtn.style.background = 'linear-gradient(135deg, var(--brand, #3b82f6) 0%, var(--brand-dark, #1d4ed8) 100%)';
      okBtn.style.color = '#fff';
      okBtn.style.cursor = 'pointer';
      okBtn.style.transition = 'all 0.2s ease';
      okBtn.style.boxShadow = '0 4px 12px rgba(59, 130, 246, 0.25)';
      okBtn.textContent = btnText;
      okBtn.addEventListener('mouseenter', () => {
        okBtn.style.transform = 'translateY(-1px)';
        okBtn.style.boxShadow = '0 6px 16px rgba(59, 130, 246, 0.35)';
      });
      okBtn.addEventListener('mouseleave', () => {
        okBtn.style.transform = 'translateY(0)';
        okBtn.style.boxShadow = '0 4px 12px rgba(59, 130, 246, 0.25)';
      });
      
      btnGroup.appendChild(okBtn);
      content.appendChild(header);
      content.appendChild(body);
      content.appendChild(btnGroup);
      overlay.appendChild(content);
      document.body.appendChild(overlay);
      
      setTimeout(() => {
        overlay.style.opacity = '1';
        overlay.classList.add('active');
      }, 20);
      
      okBtn.addEventListener('click', () => {
        close();
        resolve(true);
      });
      
      overlay.addEventListener('click', (e) => {
        if (e.target === overlay) {
          close();
          resolve(true);
        }
      });
      
      function close() {
        overlay.style.opacity = '0';
        overlay.classList.remove('active');
        setTimeout(() => {
          overlay.remove();
        }, 300);
      }
    });
  }

  function openModal(id) {
    const modal = $(id);
    if (modal) {
      modal.style.setProperty('display', 'flex', 'important');
      modal.style.setProperty('opacity', '1', 'important');
      modal.classList.add('active');
      document.body.style.overflow = 'hidden';
    }
  }

  function closeModal(id) {
    const modal = $(id);
    if (modal) {
      modal.style.setProperty('display', 'none', 'important');
      modal.style.setProperty('opacity', '0', 'important');
      modal.classList.remove('active');
      document.body.style.overflow = '';
      if (id === 'gallerySelectModal') {
        state.activeAssignItemId = null;
      }
    }
  }

  function escapeHtml(value) {
    return String(value ?? '').replace(/[&<>"']/g, (char) => ({
      '&': '&amp;',
      '<': '&lt;',
      '>': '&gt;',
      '"': '&quot;',
      "'": '&#039;'
    }[char]));
  }

  function getCleanImageUrl(url) {
    if (!url) return '';
    // Match drive.google.com or lh3.googleusercontent.com/d/ URLs and extract File ID
    const driveRegex = /(?:drive\.google\.com\/(?:uc\?(?:export=view&)?id=|open\?id=|file\/d\/)|lh3\.googleusercontent\.com\/d\/)([a-zA-Z0-9_-]{25,})/;
    const match = String(url).match(driveRegex);
    if (match && match[1]) {
      return `https://lh3.googleusercontent.com/d/${match[1]}`;
    }
    return url;
  }

  function showQrModal(qrUrl, tableNumber, areaName) {
    const qrImgUrl = `https://api.qrserver.com/v1/create-qr-code/?size=500x500&data=${encodeURIComponent(qrUrl)}`;
    
    $('qrModalTitle').textContent = `QR Code: ${tableNumber} (${areaName})`;
    $('qrModalImage').src = qrImgUrl;
    $('qrModalLink').value = qrUrl;

    $('downloadQrBtn').onclick = async (e) => {
      e.preventDefault();
      try {
        const response = await fetch(qrImgUrl);
        const blob = await response.blob();
        const blobUrl = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = blobUrl;
        a.download = `QR_${tableNumber.replace(/\s+/g, '_')}_${areaName.replace(/\s+/g, '_')}.png`;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(blobUrl);
      } catch (err) {
        window.open(qrImgUrl, '_blank');
      }
    };

    $('printQrBtn').onclick = (e) => {
      e.preventDefault();
      const printWindow = window.open('', '_blank', 'width=600,height=600');
      if (!printWindow) {
        toast('Popup blocked! Please allow popups to print.');
        return;
      }
      printWindow.document.write(`
        <!doctype html>
        <html>
          <head>
            <title>Print QR - ${escapeHtml(tableNumber)}</title>
            <style>
              body {
                margin: 0;
                display: flex;
                flex-direction: column;
                align-items: center;
                justify-content: center;
                height: 100vh;
                font-family: 'Outfit', sans-serif;
                text-align: center;
                background: white;
                color: black;
              }
              .qr-box {
                border: 2px solid #000;
                padding: 30px;
                border-radius: 12px;
                background: white;
                box-shadow: 0 4px 10px rgba(0,0,0,0.1);
              }
              h1 {
                margin: 0 0 10px 0;
                font-size: 28px;
              }
              p {
                margin: 0 0 20px 0;
                font-size: 18px;
                color: #555;
              }
              img {
                width: 350px;
                height: 350px;
              }
            </style>
          </head>
          <body>
            <div class="qr-box">
              <h1>\${escapeHtml(tableNumber)}</h1>
              <p>\${escapeHtml(areaName)}</p>
              <img src="\${escapeHtml(qrImgUrl)}" alt="QR Code">
            </div>
            <script>
              window.onload = function() {
                window.print();
                setTimeout(function() { window.close(); }, 500);
              };
            <\/script>
          </body>
        </html>
      `);
      printWindow.document.close();
    };

    $('copyQrLinkBtn').onclick = (e) => {
      e.preventDefault();
      const linkInput = $('qrModalLink');
      linkInput.select();
      navigator.clipboard.writeText(linkInput.value)
        .then(() => {
          toast('Link copied to clipboard!');
        })
        .catch(() => {
          toast('Failed to copy link.');
        });
    };

    openModal('qrModal');
  }

  function getImageAssignments(imageUrl) {
    const assignments = [];
    
    state.items.forEach(item => {
      if (item.image) {
        try {
          let imgs = [];
          if (item.image.trim().startsWith('[')) {
            imgs = JSON.parse(item.image);
          } else {
            imgs = [item.image];
          }
          if (imgs.includes(imageUrl)) {
            assignments.push({
              type: 'item',
              name: item.name,
              categoryName: item.category_name || categoryName(item.category_id)
            });
          }
        } catch (e) {
          if (item.image === imageUrl) {
            assignments.push({
              type: 'item',
              name: item.name,
              categoryName: item.category_name || categoryName(item.category_id)
            });
          }
        }
      }
    });

    state.categories.forEach(cat => {
      if (cat.image === imageUrl) {
        assignments.push({
          type: 'category',
          name: cat.name,
          categoryName: 'Category'
        });
      }
    });

    return assignments;
  }

  function renderGallery() {
    const listNode = $('galleryList');
    if (!listNode || listNode.isMock) return;
    
    if (!state.gallery || !state.gallery.length) {
      listNode.innerHTML = '<div class="empty" style="grid-column: 1 / -1;">No gallery images found. Upload some images to get started!</div>';
      return;
    }

    const catFilter = $('galleryFilterCategory') ? $('galleryFilterCategory').value : '';
    const subCatFilter = $('galleryFilterSubCategory') ? $('galleryFilterSubCategory').value : '';
    const searchVal = $('gallerySearch') ? $('gallerySearch').value.toLowerCase().trim() : '';

    const filtered = state.gallery.filter(image => {
      const matchesCategory = !catFilter || Number(image.category_id) === Number(catFilter);
      const matchesSubCategory = !subCatFilter || Number(image.sub_category_id) === Number(subCatFilter);
      const matchesSearch = !searchVal || (image.filename && image.filename.toLowerCase().includes(searchVal));
      return matchesCategory && matchesSubCategory && matchesSearch;
    });

    if (!filtered.length) {
      listNode.innerHTML = '<div class="empty" style="grid-column: 1 / -1;">No matching gallery images found.</div>';
      return;
    }

    listNode.innerHTML = filtered.map((image) => {
      const assignments = getImageAssignments(image.url);
      const isVisible = asBool(image.is_visible);
      
      const catObj = state.categories.find(c => Number(c.id) === Number(image.category_id));
      const subCatObj = state.categories.find(c => Number(c.id) === Number(image.sub_category_id));

      const isAssigned = assignments.length > 0;
      const cardStyle = isAssigned 
        ? 'border: 1px solid rgba(16, 185, 129, 0.35); box-shadow: 0 0 12px rgba(16, 185, 129, 0.1);' 
        : 'border: 1px solid rgba(255, 255, 255, 0.08);';

      return `
        <div class="gallery-item-card" style="background: rgba(22, 28, 45, 0.6); ${cardStyle} border-radius: var(--radius-sm); padding: 12px; display: flex; flex-direction: column; gap: 8px; position: relative;">
          <div style="position: relative; width: 100%; aspect-ratio: 1; border-radius: var(--radius-sm); overflow: hidden; background: rgba(0, 0, 0, 0.25); display: flex; align-items: center; justify-content: center;">
            <img src="${escapeHtml(getCleanImageUrl(image.url))}" class="gallery-image-clickable" data-full-url="${escapeHtml(image.url)}" data-filename="${escapeHtml(image.filename || 'Image')}" style="width: 100%; height: 100%; object-fit: contain; background: rgba(0, 0, 0, 0.18); cursor: pointer; ${!isVisible ? 'opacity: 0.3;' : ''}">
            <div style="position: absolute; top: 6px; left: 6px; display: flex; flex-direction: column; gap: 4px; z-index: 5; pointer-events: none;">
              ${catObj ? `<span class="badge" style="font-size: 9px; background: rgba(34, 197, 94, 0.9); color: #fff; border: 1px solid rgba(34, 197, 94, 0.2); padding: 2px 5px; border-radius: 4px; box-shadow: 0 2px 4px rgba(0,0,0,0.25); text-transform: none; font-weight: 600; width: fit-content; margin: 0;">📁 ${escapeHtml(catObj.name)}</span>` : ''}
              ${subCatObj ? `<span class="badge" style="font-size: 9px; background: rgba(59, 130, 246, 0.9); color: #fff; border: 1px solid rgba(59, 130, 246, 0.2); padding: 2px 5px; border-radius: 4px; box-shadow: 0 2px 4px rgba(0,0,0,0.25); text-transform: none; font-weight: 600; width: fit-content; margin: 0;">📄 ${escapeHtml(subCatObj.name)}</span>` : ''}
            </div>
            ${isAssigned ? `
              <div style="position: absolute; top: 6px; right: 6px; z-index: 5; pointer-events: none;">
                <span class="badge" style="font-size: 10px; background: linear-gradient(135deg, #10b981 0%, #059669 100%); color: #fff; border: 1px solid rgba(16, 185, 129, 0.2); padding: 4px 8px; border-radius: 20px; box-shadow: 0 2px 8px rgba(16, 185, 129, 0.4); text-transform: none; font-weight: 700; display: inline-flex; align-items: center; gap: 4px; letter-spacing: 0.02em;">
                  ✓ Assigned
                </span>
              </div>
            ` : ''}
            ${!isVisible ? `
              <div style="position: absolute; top: 0; left: 0; right: 0; bottom: 0; background: rgba(0,0,0,0.6); display: flex; align-items: center; justify-content: center; color: #fff; font-size: 11px; font-weight: 700; text-transform: uppercase; letter-spacing: 0.05em; z-index: 2;">Hidden</div>
            ` : ''}
          </div>
          
          <div style="flex: 1; display: flex; flex-direction: column; gap: 4px; min-width: 0;">
            <!-- Assigned Tags/Marks -->
            <div class="assigned-tags" style="display: flex; flex-direction: column; gap: 4px;">
              ${assignments.length ? assignments.map(a => `
                <span class="badge" style="font-size: 10px; padding: 4px 8px; background: linear-gradient(135deg, rgba(99, 102, 241, 0.25) 0%, rgba(99, 102, 241, 0.45) 100%); color: #e0e7ff; border: 1px solid rgba(99, 102, 241, 0.7); border-radius: 6px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; display: block; font-weight: 700; box-shadow: 0 2px 6px rgba(99, 102, 241, 0.3); text-shadow: 0 1px 2px rgba(0,0,0,0.5);" title="${escapeHtml(a.name)} (${escapeHtml(a.categoryName)})">
                  📍 ${a.type === 'category' ? '📁' : '🍔'} ${escapeHtml(a.name)}
                </span>
              `).join('') : `
                <span class="badge bad" style="font-size: 10px; padding: 2px 6px; display: inline-block; width: fit-content; margin: 0;">Unassigned</span>
              `}
            </div>
          </div>

          <div style="display: flex; gap: 6px; margin-top: auto; align-items: center; position: relative;">
            ${isAssigned ? `
              <button type="button" class="gallery-reset-all-btn" data-reset-gallery-url="${escapeHtml(image.url)}" style="flex: 1; padding: 6px 12px; font-size: 12px; min-height: 34px; font-weight: 600; width: auto; margin: 0; background: linear-gradient(135deg, rgba(239, 68, 68, 0.15) 0%, rgba(239, 68, 68, 0.28) 100%); border: 1px solid rgba(239, 68, 68, 0.35); color: #f87171; cursor: pointer; border-radius: var(--radius-sm); transition: all 0.2s ease;">Reset</button>
            ` : `
              <button type="button" class="gallery-assign-action-btn" data-assign-gallery-url="${escapeHtml(image.url)}" style="flex: 1; padding: 6px 12px; font-size: 12px; min-height: 34px; font-weight: 600; width: auto; margin: 0;">Assign</button>
            `}
            
            <div class="gallery-card-menu-container" style="position: relative; display: inline-block;">
              <button type="button" class="gallery-card-menu-btn" style="width: 34px; height: 34px; border-radius: var(--radius-sm); background: rgba(255,255,255,0.06); border: 1px solid rgba(255,255,255,0.1); color: #fff; display: flex; align-items: center; justify-content: center; font-size: 18px; cursor: pointer; transition: all 0.2s ease; padding: 0;" title="Actions">⋮</button>
              <div class="gallery-card-menu-dropdown" style="display: none; position: absolute; bottom: calc(100% + 6px); right: 0; background: rgba(18, 24, 38, 0.96); backdrop-filter: blur(16px); -webkit-backdrop-filter: blur(16px); border: 1px solid rgba(255, 255, 255, 0.08); border-radius: var(--radius-sm); box-shadow: 0 4px 20px rgba(0,0,0,0.5); z-index: 1000; min-width: 130px; overflow: hidden; padding: 4px 0;">
                <button type="button" class="gallery-dropdown-item" data-toggle-gallery-id="${image.id}" data-toggle-gallery-status="${isVisible ? 0 : 1}" style="width: 100%; padding: 8px 12px; text-align: left; background: transparent; border: none; color: #fff; font-size: 12px; font-weight: 500; cursor: pointer; display: flex; align-items: center; gap: 6px; transition: background 0.2s;">
                  ${isVisible ? '👁️‍🗨️ Hide' : '👁️ Show'}
                </button>
                <button type="button" class="gallery-dropdown-item" data-edit-gallery-id="${image.id}" data-edit-gallery-name="${escapeHtml(image.filename || '')}" data-edit-gallery-visible="${image.is_visible}" data-edit-gallery-url="${escapeHtml(image.url)}" data-edit-gallery-category="${image.category_id || ''}" data-edit-gallery-subcategory="${image.sub_category_id || ''}" style="width: 100%; padding: 8px 12px; text-align: left; background: transparent; border: none; color: #fff; font-size: 12px; font-weight: 500; cursor: pointer; display: flex; align-items: center; gap: 6px; transition: background 0.2s;">
                  ✏️ Rename
                </button>
                <button type="button" class="gallery-dropdown-item" data-update-gallery-image-id="${image.id}" style="width: 100%; padding: 8px 12px; text-align: left; background: transparent; border: none; color: #fff; font-size: 12px; font-weight: 500; cursor: pointer; display: flex; align-items: center; gap: 6px; transition: background 0.2s;">
                  🔄 Update Image
                </button>
                <button type="button" class="gallery-dropdown-item danger" data-delete-gallery-id="${image.id}" style="width: 100%; padding: 8px 12px; text-align: left; background: rgba(239, 68, 68, 0.05); border: none; color: #ef4444; font-size: 12px; font-weight: 600; cursor: pointer; display: flex; align-items: center; gap: 6px; border-top: 1px solid rgba(255,255,255,0.05); transition: background 0.2s;">
                  🗑️ Delete
                </button>
              </div>
            </div>
          </div>
        </div>
      `;
    }).join('');
  }

  function renderGallerySelectGrid() {
    const grid = $('gallerySelectGrid');
    if (!grid) return;

    if (!state.gallery || !state.gallery.length) {
      grid.innerHTML = '<div style="color: var(--muted); text-align: center; padding: 20px; font-size: 13px; grid-column: 1/-1;">No images in gallery.</div>';
      return;
    }

    const catFilter = $('gallerySelectFilterCategory') ? $('gallerySelectFilterCategory').value : '';
    const searchVal = $('gallerySelectSearch') ? $('gallerySelectSearch').value.toLowerCase().trim() : '';

    const filtered = state.gallery.filter(image => {
      const matchesCategory = !catFilter || Number(image.category_id) === Number(catFilter);
      const matchesSearch = !searchVal || (image.filename && image.filename.toLowerCase().includes(searchVal));
      return matchesCategory && matchesSearch;
    });

    if (!filtered.length) {
      grid.innerHTML = '<div style="color: var(--muted); text-align: center; padding: 20px; font-size: 13px; grid-column: 1/-1;">No matching images.</div>';
      return;
    }

    let selectedImageUrls = [];
    if (state.activeAssignItemId) {
      const item = state.items.find(i => Number(i.id) === Number(state.activeAssignItemId));
      if (item && item.image) {
        try {
          if (item.image.trim().startsWith('[')) {
            selectedImageUrls = JSON.parse(item.image);
          } else {
            selectedImageUrls = [item.image];
          }
        } catch (e) {
          selectedImageUrls = [item.image];
        }
      }
    } else {
      selectedImageUrls = state.itemImages || [];
    }

    grid.innerHTML = filtered.map(image => {
      const isSelected = selectedImageUrls.includes(image.url);
      const selectedBorder = isSelected ? 'border: 2px solid var(--brand); box-shadow: 0 0 8px var(--brand-glow);' : 'border: 1px solid rgba(255,255,255,0.08);';
      return `
        <div class="gallery-select-card" data-url="${escapeHtml(image.url)}" style="aspect-ratio: 1; border-radius: var(--radius-sm); overflow: hidden; background: rgba(0, 0, 0, 0.25); display: flex; align-items: center; justify-content: center; position: relative; cursor: pointer; ${selectedBorder}">
          <img src="${escapeHtml(getCleanImageUrl(image.url))}" style="width: 100%; height: 100%; object-fit: contain; background: rgba(0, 0, 0, 0.18);">
          ${isSelected ? `
            <div style="position: absolute; top: 4px; right: 4px; background: var(--brand); color: #fff; width: 18px; height: 18px; border-radius: 50%; display: flex; align-items: center; justify-content: center; font-size: 11px; font-weight: 700;">✓</div>
          ` : ''}
        </div>
      `;
    }).join('');
  }

  let activeAssignTab = 'items';

  function switchAssignTab(tab) {
    activeAssignTab = tab;
    const tabItems = $('assignTabItems');
    const tabCats = $('assignTabCategories');
    if (!tabItems || !tabCats) return;

    if (tab === 'items') {
      tabItems.classList.add('active');
      tabItems.style.background = 'linear-gradient(135deg, var(--brand) 0%, var(--brand-dark) 100%)';
      tabItems.style.color = '#fff';
      tabCats.classList.remove('active');
      tabCats.style.background = 'transparent';
      tabCats.style.color = 'var(--muted)';
      
      $('assignItemsFilterRow').style.display = 'flex';
      if ($('assignFilterSelectsContainer')) {
        $('assignFilterSelectsContainer').style.display = 'flex';
      }
      $('assignItemSearch').placeholder = '🔍 Search menu items...';
    } else {
      tabCats.classList.add('active');
      tabCats.style.background = 'linear-gradient(135deg, var(--brand) 0%, var(--brand-dark) 100%)';
      tabCats.style.color = '#fff';
      tabItems.classList.remove('active');
      tabItems.style.background = 'transparent';
      tabItems.style.color = 'var(--muted)';
      
      $('assignItemsFilterRow').style.display = 'flex';
      if ($('assignFilterSelectsContainer')) {
        $('assignFilterSelectsContainer').style.display = 'none';
      }
      $('assignItemSearch').placeholder = '🔍 Search categories...';
    }
    renderAssignList();
  }

  function renderAssignList() {
    const listNode = $('assignItemsList');
    if (!listNode) return;
    
    const query = $('assignItemSearch').value.toLowerCase().trim();
    
    if (activeAssignTab === 'items') {
      const catVal = $('assignFilterCategory').value;
      const subCatVal = $('assignFilterSubCategory').value;
      
      const filteredItems = state.items.filter(item => {
        const shortcode = getItemShortcode(item).toLowerCase();
        const matchesQuery = !query || item.name.toLowerCase().includes(query) || shortcode.includes(query);
        const matchesCat = !catVal || Number(item.category_id) === Number(catVal);
        const matchesSubCat = !subCatVal || Number(item.sub_category_id) === Number(subCatVal);
        return matchesQuery && matchesCat && matchesSubCat;
      });
      
      if (!filteredItems.length) {
        listNode.innerHTML = '<div style="color: var(--muted); text-align: center; padding: 20px; font-size: 13px;">No items found.</div>';
        return;
      }
      
      listNode.innerHTML = filteredItems.map(item => {
        const catObj = state.categories.find(c => Number(c.id) === Number(item.category_id));
        const subCatObj = state.categories.find(c => Number(c.id) === Number(item.sub_category_id));
        const catText = catObj ? catObj.name : '';
        const subCatText = subCatObj ? ` > ${subCatObj.name}` : '';
        const pathText = catText ? `(${catText}${subCatText})` : '';
        
        let hasImage = false;
        let imgPreviewHtml = '';
        if (item.image) {
          try {
            let imgs = JSON.parse(item.image);
            if (imgs && imgs.length > 0) {
              imgPreviewHtml = `<img src="${escapeHtml(getCleanImageUrl(imgs[0]))}" style="width: 28px; height: 28px; object-fit: contain; border-radius: 4px; border: 1px solid rgba(255,255,255,0.1); background: rgba(0, 0, 0, 0.2);">`;
              hasImage = true;
            }
          } catch(ex) {
            imgPreviewHtml = `<img src="${escapeHtml(getCleanImageUrl(item.image))}" style="width: 28px; height: 28px; object-fit: contain; border-radius: 4px; border: 1px solid rgba(255,255,255,0.1); background: rgba(0, 0, 0, 0.2);">`;
            hasImage = true;
          }
        }
        if (!hasImage) {
          imgPreviewHtml = `<div style="width: 28px; height: 28px; background: rgba(255,255,255,0.05); display: flex; align-items: center; justify-content: center; font-size: 11px; color: var(--muted); border-radius: 4px;">🍽️</div>`;
        }

        const activeImageUrl = $('assignImageUrl') ? $('assignImageUrl').value : '';
        let isCurrentImgAssigned = false;
        if (activeImageUrl) {
          let imgs = [];
          if (item.image) {
            try {
              if (item.image.trim().startsWith('[')) {
                imgs = JSON.parse(item.image);
              } else {
                imgs = [item.image];
              }
            } catch (ex) {
              imgs = [item.image];
            }
          }
          isCurrentImgAssigned = imgs.includes(activeImageUrl);
        }

        const assignBtnHtml = isCurrentImgAssigned
          ? `<button type="button" class="gallery-item-unassign-btn" data-unassign-item-id="${item.id}" style="padding: 4px 10px; min-height: auto; font-size: 11px; font-weight: 600; width: auto; margin: 0; background: rgba(239, 68, 68, 0.15); border: 1px solid rgba(239, 68, 68, 0.3); color: #f87171; border-radius: var(--radius-sm);">Reset</button>`
          : `<button type="button" class="gallery-assign-action-btn" data-assign-item-action-id="${item.id}" style="padding: 4px 10px; min-height: auto; font-size: 11px; font-weight: 600; width: auto; margin: 0; background: var(--brand);">Assign</button>`;

        return `
          <div style="display: flex; align-items: center; justify-content: space-between; gap: 10px; padding: 8px; border-bottom: 1px solid rgba(255,255,255,0.05);">
            <div style="display: flex; align-items: center; gap: 8px; min-width: 0; flex: 1;">
              ${imgPreviewHtml}
              <div style="min-width: 0; flex: 1;">
                <div style="font-size: 13px; font-weight: 600; color: #fff; white-space: nowrap; overflow: hidden; text-overflow: ellipsis;">${escapeHtml(item.name)}</div>
                <div style="font-size: 11px; color: var(--muted); white-space: nowrap; overflow: hidden; text-overflow: ellipsis;">₹${item.price} <span style="color: var(--brand-light);">${escapeHtml(pathText)}</span></div>
              </div>
            </div>
            ${assignBtnHtml}
          </div>
        `;
      }).join('');
    } else {
      const filteredCats = state.categories.filter(cat => {
        return !query || cat.name.toLowerCase().includes(query);
      });
      
      if (!filteredCats.length) {
        listNode.innerHTML = '<div style="color: var(--muted); text-align: center; padding: 20px; font-size: 13px;">No categories found.</div>';
        return;
      }

      // Sort categories hierarchically (subcategories under their parent categories)
      const mainCats = filteredCats.filter(c => !c.parent_id);
      const subCats = filteredCats.filter(c => c.parent_id);
      
      mainCats.sort((a, b) => Number(a.sort_order || 0) - Number(b.sort_order || 0) || a.name.localeCompare(b.name));
      
      const sortedCats = [];
      mainCats.forEach(main => {
        sortedCats.push(main);
        const subs = subCats.filter(sub => Number(sub.parent_id) === Number(main.id));
        subs.sort((a, b) => Number(a.sort_order || 0) - Number(b.sort_order || 0) || a.name.localeCompare(b.name));
        sortedCats.push(...subs);
      });
      
      // Add any orphaned subcategories at the end
      subCats.forEach(sub => {
        if (!sortedCats.some(c => Number(c.id) === Number(sub.id))) {
          sortedCats.push(sub);
        }
      });
      
      listNode.innerHTML = sortedCats.map(cat => {
        const indentStyle = cat.parent_id ? 'padding-left: 28px;' : '';
        const branchLineHtml = cat.parent_id ? '<span style="color: var(--muted); font-size: 11px; margin-right: 4px;">↳</span>' : '';
        const subLabel = cat.parent_id ? 'Sub Category' : 'Main Category';

        let imgPreviewHtml = '';
        if (cat.image) {
          imgPreviewHtml = `<img src="${escapeHtml(getCleanImageUrl(cat.image))}" style="width: 28px; height: 28px; object-fit: contain; border-radius: 4px; border: 1px solid rgba(255,255,255,0.1); background: rgba(0, 0, 0, 0.2);">`;
        } else {
          imgPreviewHtml = `<div style="width: 28px; height: 28px; background: rgba(255,255,255,0.05); display: flex; align-items: center; justify-content: center; font-size: 11px; color: var(--muted); border-radius: 4px;">${cat.parent_id ? '📄' : '📁'}</div>`;
        }
        
        const activeImageUrl = $('assignImageUrl') ? $('assignImageUrl').value : '';
        const isCurrentImgAssigned = activeImageUrl && cat.image === activeImageUrl;
        const assignBtnHtml = isCurrentImgAssigned
          ? `<button type="button" class="gallery-category-unassign-btn" data-unassign-category-id="${cat.id}" style="padding: 4px 10px; min-height: auto; font-size: 11px; font-weight: 600; width: auto; margin: 0; background: rgba(239, 68, 68, 0.15); border: 1px solid rgba(239, 68, 68, 0.3); color: #f87171; border-radius: var(--radius-sm);">Reset</button>`
          : `<button type="button" class="gallery-assign-action-btn" data-assign-category-action-id="${cat.id}" style="padding: 4px 10px; min-height: auto; font-size: 11px; font-weight: 600; width: auto; margin: 0; background: var(--brand);">Assign</button>`;

        return `
          <div style="display: flex; align-items: center; justify-content: space-between; gap: 10px; padding: 8px; border-bottom: 1px solid rgba(255,255,255,0.05); ${indentStyle}">
            <div style="display: flex; align-items: center; gap: 8px; min-width: 0; flex: 1;">
              ${branchLineHtml}
              ${imgPreviewHtml}
              <div style="min-width: 0; flex: 1;">
                <div style="font-size: 13px; font-weight: 600; color: #fff; white-space: nowrap; overflow: hidden; text-overflow: ellipsis;">${escapeHtml(cat.name)}</div>
                <div style="font-size: 11px; color: var(--muted); white-space: nowrap; overflow: hidden; text-overflow: ellipsis;">${escapeHtml(subLabel)}</div>
              </div>
            </div>
            ${assignBtnHtml}
          </div>
        `;
      }).join('');
    }
  }

  function openAssignModal(url) {
    $('assignImageUrl').value = url;
    $('assignImagePreview').src = getCleanImageUrl(url);
    
    const filename = String(url || '').split('/').pop() || 'image';
    if ($('assignImageNameText')) {
      $('assignImageNameText').textContent = filename;
    }

    $('assignItemSearch').value = '';
    
    // Set up category and subcategory selects for filters
    setOptions($('assignFilterCategory'), state.categories.filter(c => !c.parent_id), { placeholder: 'All Categories' });
    setOptions($('assignFilterSubCategory'), [], { placeholder: 'All Subcategories' });
    
    $('assignFilterCategory').dispatchEvent(new Event('change', { bubbles: true }));
    $('assignFilterSubCategory').dispatchEvent(new Event('change', { bubbles: true }));
    
    switchAssignTab('items');
    
    openModal('assignImageModal');
  }

  function openEditGalleryModal(id, name, visible, url, categoryId = '', subCategoryId = '') {
    $('editGalleryImageId').value = id;
    $('editGalleryImageUrl').value = url;
    $('editGalleryImageName').value = name;
    $('editGalleryImageVisible').checked = asBool(visible);
    $('editGalleryImagePreview').src = getCleanImageUrl(url);
    
    $('editGalleryImageCategory').value = categoryId || '';
    setOptions($('editGalleryImageSubCategory'), categoryId ? subCategories(categoryId) : [], { placeholder: 'No subcategory' });
    $('editGalleryImageSubCategory').value = subCategoryId || '';
    
    // Trigger dynamic sync events
    $('editGalleryImageCategory').dispatchEvent(new Event('change', { bubbles: true }));
    $('editGalleryImageSubCategory').dispatchEvent(new Event('change', { bubbles: true }));

    openModal('editGalleryImageModal');
  }


  function initCustomSelects() {
    const selects = document.querySelectorAll('select:not(.hidden-select)');
    selects.forEach(setupCustomSelect);
  }

  function setupCustomSelect(select) {
    if (select.closest('.custom-select-wrapper') || select.classList.contains('hidden-select')) {
      return;
    }

    const wrapper = document.createElement('div');
    wrapper.className = 'custom-select-wrapper';
    if (select.className) {
      wrapper.className += ' ' + select.className;
    }

    select.parentNode.insertBefore(wrapper, select);
    wrapper.appendChild(select);
    select.classList.add('hidden-select');

    const trigger = document.createElement('button');
    trigger.type = 'button';
    trigger.className = 'custom-select-trigger';
    
    const label = document.createElement('span');
    label.className = 'custom-select-label';
    trigger.appendChild(label);
    
    const optionsContainer = document.createElement('div');
    optionsContainer.className = 'custom-select-options';
    
    wrapper.appendChild(trigger);
    wrapper.appendChild(optionsContainer);

    function syncUi() {
      const options = select.options;
      const selectedIndex = select.selectedIndex;
      const selectedOption = selectedIndex >= 0 ? options[selectedIndex] : null;
      label.textContent = selectedOption ? selectedOption.text : 'Select...';

      optionsContainer.innerHTML = Array.from(options).map((opt, idx) => {
        const isSel = idx === selectedIndex;
        return `<div class="custom-select-option${isSel ? ' selected' : ''}" data-value="${escapeHtml(opt.value)}">${escapeHtml(opt.text)}</div>`;
      }).join('');

      optionsContainer.querySelectorAll('.custom-select-option').forEach((el) => {
        el.addEventListener('click', (e) => {
          e.stopPropagation();
          const val = el.dataset.value;
          select.value = val;
          select.dispatchEvent(new Event('change', { bubbles: true }));
          wrapper.classList.remove('open');
        });
      });
    }

    syncUi();

    trigger.addEventListener('click', (e) => {
      e.stopPropagation();
      const isOpen = wrapper.classList.contains('open');
      document.querySelectorAll('.custom-select-wrapper').forEach((w) => w.classList.remove('open'));
      if (!isOpen) {
        wrapper.classList.add('open');
      }
    });

    select.addEventListener('change', syncUi);
    select.addEventListener('select-value-synced', syncUi);

    const observer = new MutationObserver(syncUi);
    observer.observe(select, { childList: true, subtree: true, characterData: true });
  }

  document.addEventListener('click', () => {
    document.querySelectorAll('.custom-select-wrapper').forEach((w) => w.classList.remove('open'));
    document.querySelectorAll('.gallery-card-menu-dropdown').forEach(d => d.style.display = 'none');
  });

  function asBool(value) {
    return Number(value) === 1 || value === true || value === '1';
  }

  function setOptions(select, rows, { placeholder = 'None', selected = '', includeBlank = true, filter = null } = {}) {
    const filtered = filter ? rows.filter(filter) : rows;
    select.innerHTML = [
      includeBlank ? `<option value="">${escapeHtml(placeholder)}</option>` : '',
      ...filtered.map((row) => `<option value="${row.id}">${escapeHtml(row.name || row.table_number)}</option>`)
    ].join('');
    
    // Check if the selected value actually exists in the options
    const optionValues = Array.from(select.options).map(opt => opt.value);
    if (selected && optionValues.includes(String(selected))) {
      select.value = String(selected);
    } else {
      select.value = includeBlank ? '' : (optionValues[0] || '');
    }
  }

  function categoryName(id) {
    return state.categories.find((row) => Number(row.id) === Number(id))?.name || 'No category';
  }

  function areaName(id) {
    return state.areas.find((row) => Number(row.id) === Number(id))?.name || 'No area';
  }

  function parentCategories() {
    return state.categories.filter((row) => !row.parent_id);
  }

  function subCategories(parentId) {
    return state.categories.filter((row) => Number(row.parent_id || 0) === Number(parentId || 0));
  }

  function syncSelects() {
    setOptions($('itemCategory'), parentCategories(), { placeholder: 'Select category', includeBlank: true });
    setOptions($('itemSubCategory'), [], { placeholder: 'No subcategory' });
    setOptions($('categoryParent'), parentCategories(), { placeholder: 'Main category' });
    setOptions($('tableArea'), state.areas, { placeholder: 'No area' });

    // Sync gallery filters
    setOptions($('galleryFilterCategory'), parentCategories(), { placeholder: 'All Categories', includeBlank: true });
    setOptions($('galleryFilterSubCategory'), [], { placeholder: 'All Subcategories', includeBlank: true });

    // Sync gallery upload modal
    setOptions($('galleryUploadCategory'), parentCategories(), { placeholder: 'Select category', includeBlank: true });
    setOptions($('galleryUploadSubCategory'), [], { placeholder: 'No subcategory' });

    // Sync gallery edit modal
    setOptions($('editGalleryImageCategory'), parentCategories(), { placeholder: 'Select category', includeBlank: true });
    setOptions($('editGalleryImageSubCategory'), [], { placeholder: 'No subcategory' });

    // Sync gallery selection modal filter
    setOptions($('gallerySelectFilterCategory'), parentCategories(), { placeholder: 'All Categories', includeBlank: true });
  }

  function getItemShortcode(item) {
    let code = '';
    if (item.description) {
      try {
        const parsed = JSON.parse(item.description);
        if (parsed && parsed.code) {
          code = String(parsed.code).trim();
        }
      } catch (e) {
        // Not a JSON string, ignore
      }
    }
    if (!code) {
      const words = item.name ? item.name.trim().split(/\s+/) : [];
      code = words.map(w => w.charAt(0)).join('');
    }
    return code;
  }

  function renderItems() {
    if (!$('itemsList') || $('itemsList').isMock) return;
    const query = $('itemSearch').value.trim().toLowerCase();
    const catFilter = $('filterCategory').value;
    const subCatFilter = $('filterSubCategory').value;
    const cleanQuery = query.replace(/\s+/g, '');

    const rows = state.items.filter((item) => {
      const shortcode = getItemShortcode(item).toLowerCase();
      const nameLower = (item.name || '').toLowerCase();
      
      const matchesQuery = !query || 
                           nameLower.includes(query) || 
                           shortcode.startsWith(cleanQuery) || 
                           shortcode.includes(cleanQuery);
                           
      const matchesCategory = !catFilter || Number(item.category_id) === Number(catFilter);
      const matchesSubCategory = !subCatFilter || Number(item.sub_category_id) === Number(subCatFilter);
      return matchesQuery && matchesCategory && matchesSubCategory;
    });

    $('itemsList').innerHTML = rows.length ? rows.map((item) => {
      const shortcode = getItemShortcode(item).toUpperCase();
      let firstImg = '';
      if (item.image) {
        try {
          const parsed = JSON.parse(item.image);
          if (Array.isArray(parsed) && parsed.length > 0) {
            firstImg = parsed[0];
          } else if (typeof parsed === 'string') {
            firstImg = parsed;
          }
        } catch (e) {
          firstImg = item.image;
        }
      }

      return `
        <article class="row" style="padding: 0; overflow: hidden; display: flex; align-items: stretch; justify-content: space-between;">
          <div class="menu-item-image-wrapper" data-assign-image-item-id="${item.id}" style="width: 100px; min-width: 100px; position: relative; cursor: pointer; background: rgba(255,255,255,0.02); display: flex; align-items: center; justify-content: center; border-right: 1px solid rgba(255,255,255,0.05); overflow: hidden;">
            ${firstImg 
              ? `<img src="${escapeHtml(getCleanImageUrl(firstImg))}" alt="${escapeHtml(item.name)}">`
              : `<div style="display: flex; flex-direction: column; align-items: center; justify-content: center; width: calc(100% - 16px); height: calc(100% - 16px); color: var(--muted); font-size: 16px; background: rgba(255,255,255,0.01); border: 2px dashed rgba(255,255,255,0.1); border-radius: 6px; margin: 8px;">+<span style="font-size: 8px; margin-top: 2px; color: var(--muted); opacity: 0.7;">Add Image</span></div>`
            }
            <div class="image-overlay" style="position: absolute; inset: 0; background: rgba(0,0,0,0.65); display: flex; align-items: center; justify-content: center; opacity: 0; transition: opacity 0.2s; color: #fff; font-size: 11px; font-weight: 600;">
              ${firstImg ? 'Change Image' : 'Assign Image'}
            </div>
          </div>
          <div class="row-body" style="flex: 1; display: flex; align-items: center; justify-content: space-between; padding: 16px; gap: 16px; min-width: 0;">
            <div style="min-width: 0;">
              <div class="row-title">
                <span class="badge badge-shortcode">${escapeHtml(shortcode)}</span>
                ${escapeHtml(item.name)}
                <span class="badge">${escapeHtml(item.category_name || categoryName(item.category_id))}</span>
                ${item.sub_category_name ? `<span class="badge">${escapeHtml(item.sub_category_name)}</span>` : ''}
              </div>
              <div class="row-meta">
                <span>Rs. ${Number(item.price || 0).toFixed(2)}</span>
              </div>
            </div>
            <div class="row-actions">
              <button type="button" data-edit-item="${item.id}">Edit</button>
              <button type="button" class="danger" data-delete-item="${item.id}">Delete</button>
            </div>
          </div>
        </article>
      `;
    }).join('') : '<div class="empty">No menu items found.</div>';
  }

  function renderCategories() {
    if (!$('categoriesList') || $('categoriesList').isMock) return;
    const allCats = state.categories;
    if (!allCats.length) {
      $('categoriesList').innerHTML = '<div class="empty">No categories found.</div>';
      return;
    }

    // Main categories are those without a parent_id, or whose parent_id doesn't exist in allCats
    const mainCats = allCats.filter(cat => !cat.parent_id || !allCats.some(p => Number(p.id) === Number(cat.parent_id)));
    
    // Subcategories are those with a valid parent_id that exists in allCats
    const subCatsByParent = {};
    allCats.forEach(cat => {
      if (cat.parent_id && allCats.some(p => Number(p.id) === Number(cat.parent_id))) {
        const pId = Number(cat.parent_id);
        if (!subCatsByParent[pId]) subCatsByParent[pId] = [];
        subCatsByParent[pId].push(cat);
      }
    });

    // Sort both main and sub categories
    mainCats.sort((a, b) => Number(a.sort_order || 0) - Number(b.sort_order || 0));
    Object.keys(subCatsByParent).forEach(pId => {
      subCatsByParent[pId].sort((a, b) => Number(a.sort_order || 0) - Number(b.sort_order || 0));
    });

    let html = '';
    mainCats.forEach(mainCat => {
      const subs = subCatsByParent[Number(mainCat.id)] || [];
      const mainImgHtml = mainCat.image 
        ? `<img src="${escapeHtml(getCleanImageUrl(mainCat.image))}" class="category-list-img menu-item-image-clickable" data-full-url="${escapeHtml(mainCat.image)}" data-filename="${escapeHtml(mainCat.name)}" style="cursor: pointer;" alt="${escapeHtml(mainCat.name)}">`
        : '';
      const mainMetaMargin = mainCat.image ? '30px' : '0px';
      
      html += `
        <div class="category-tree-node">
          <article class="row main-category-row">
            <div>
              <div class="row-title">
                ${mainImgHtml}
                ${escapeHtml(mainCat.name)}
                <span class="badge good">Main</span>
                ${asBool(mainCat.is_active) ? '<span class="badge good">Active</span>' : '<span class="badge bad">Inactive</span>'}
              </div>
              <div class="row-meta" style="margin-left: ${mainMetaMargin};">
                <span>Sort Order: ${Number(mainCat.sort_order || 0)}</span>
                ${subs.length ? `<span>(${subs.length} Subcategories)</span>` : ''}
              </div>
            </div>
            <div class="row-actions">
              <button type="button" class="add-sub-btn" data-add-subcategory="${mainCat.id}">+ Subcategory</button>
              <button type="button" data-edit-category="${mainCat.id}">Edit</button>
              <button type="button" class="danger" data-delete-category="${mainCat.id}">Delete</button>
            </div>
          </article>
      `;

      if (subs.length > 0) {
        html += `<div class="subcategory-list-container">`;
        subs.forEach(sub => {
          const subImgHtml = sub.image 
            ? `<img src="${escapeHtml(getCleanImageUrl(sub.image))}" class="category-list-img menu-item-image-clickable" data-full-url="${escapeHtml(sub.image)}" data-filename="${escapeHtml(sub.name)}" style="cursor: pointer;" alt="${escapeHtml(sub.name)}">`
            : '';
          const subMetaMargin = sub.image ? '58px' : '28px';
          
          html += `
            <article class="row sub-category-row">
              <div class="sub-category-indent">
                <div class="row-title">
                  <span class="subcategory-branch-line">└─</span>
                  ${subImgHtml}
                  ${escapeHtml(sub.name)}
                  <span class="badge">Subcategory</span>
                  ${asBool(sub.is_active) ? '<span class="badge good">Active</span>' : '<span class="badge bad">Inactive</span>'}
                </div>
                <div class="row-meta" style="margin-left: ${subMetaMargin};">
                  <span>Sort Order: ${Number(sub.sort_order || 0)}</span>
                </div>
              </div>
              <div class="row-actions">
                <button type="button" data-edit-category="${sub.id}">Edit</button>
                <button type="button" class="danger" data-delete-category="${sub.id}">Delete</button>
              </div>
            </article>
          `;
        });
        html += `</div>`;
      }

      html += `</div>`; // Close category-tree-node
    });

    $('categoriesList').innerHTML = html;
  }

  function renderTables() {
    if (!$('tablesList') || $('tablesList').isMock) return;
    $('tablesList').innerHTML = state.tables.length ? state.tables.map((table) => {
      const isLocalHost = window.location.hostname === 'localhost' || 
                          window.location.hostname === '127.0.0.1' || 
                          window.location.hostname.endsWith('.local') || 
                          window.location.hostname.endsWith('.test') ||
                          /^192\.168\./.test(window.location.hostname) ||
                          /^10\./.test(window.location.hostname) ||
                          /^172\./.test(window.location.hostname);
      
      let pwaBaseUrl = 'https://menu.chaychaupal.com/';
      if (isLocalHost) {
        let path = window.location.pathname;
        const apiMenuIndex = path.toLowerCase().indexOf('/api/menu');
        if (apiMenuIndex >= 0) {
          pwaBaseUrl = window.location.origin + path.slice(0, apiMenuIndex) + '/api/mobilemenu/dist/index.html';
        } else {
          pwaBaseUrl = window.location.origin + '/possoftware-final/api/mobilemenu/dist/index.html';
        }
      }
      
      const qrUrl = `${pwaBaseUrl}?client=${encodeURIComponent(state.client || '')}&table=${table.qr_token || ''}`;
      
      const qrImgUrl = `https://api.qrserver.com/v1/create-qr-code/?size=150x150&data=${encodeURIComponent(qrUrl)}`;

      return `
        <article class="row" style="padding: 0; overflow: hidden; display: flex; align-items: stretch; justify-content: space-between;">
          ${table.qr_token ? `
            <div class="table-qr-image-wrapper view-qr-action" data-qr-url="${escapeHtml(qrUrl)}" data-table-number="${escapeHtml(table.table_number)}" data-area-name="${escapeHtml(table.area_name || areaName(table.area_id))}" title="View QR Code" style="width: 80px; min-width: 80px; position: relative; cursor: pointer; background: #fff; display: flex; align-items: center; justify-content: center; border-right: 1px solid rgba(255,255,255,0.05); overflow: hidden; padding: 8px;">
              <img src="${qrImgUrl}" style="width: 100%; height: 100%; object-fit: contain;" alt="QR">
              <div class="image-overlay" style="position: absolute; inset: 0; background: rgba(0,0,0,0.65); display: flex; align-items: center; justify-content: center; opacity: 0; transition: opacity 0.2s; color: #fff; font-size: 11px; font-weight: 600; text-align: center;">
                🔍 View
              </div>
            </div>
          ` : `
            <div style="width: 80px; min-width: 80px; background: rgba(255,255,255,0.02); display: flex; align-items: center; justify-content: center; border-right: 1px solid rgba(255,255,255,0.05); color: var(--muted); font-size: 18px;">
              🚫
            </div>
          `}
          <div style="flex: 1; display: flex; align-items: center; justify-content: space-between; padding: 16px; gap: 16px; min-width: 0;">
            <div style="min-width: 0;">
              <div class="row-title">
                ${escapeHtml(table.table_number)}
                <span class="badge">${escapeHtml(table.area_name || areaName(table.area_id))}</span>
                ${asBool(table.is_active) ? '<span class="badge good">Active</span>' : '<span class="badge bad">Inactive</span>'}
              </div>
              <div class="row-meta" style="display: flex; align-items: center; gap: 12px; flex-wrap: wrap; margin-top: 6px;">
                <span>Status: ${escapeHtml(table.status || table.table_status || 'available')}</span>
                ${table.qr_token ? `<span>QR token: ${escapeHtml(String(table.qr_token).slice(0, 8))}...</span>` : ''}
              </div>
            </div>
            <div class="row-actions">
              <button type="button" data-edit-table="${table.id}">Edit</button>
              <button type="button" class="danger" data-delete-table="${table.id}">Delete</button>
            </div>
          </div>
        </article>
      `;
    }).join('') : '<div class="empty">No tables found.</div>';
  }


  function renderAreas() {
    if (!$('areasList') || $('areasList').isMock) return;
    $('areasList').innerHTML = state.areas.length ? state.areas.map((area) => `
      <article class="row">
        <div>
          <div class="row-title">
            ${escapeHtml(area.name)}
            ${asBool(area.is_active) ? '<span class="badge good">Active</span>' : '<span class="badge bad">Inactive</span>'}
          </div>
          <div class="row-meta"><span>Sort ${Number(area.sort_order || 0)}</span></div>
        </div>
        <div class="row-actions">
          <button type="button" data-edit-area="${area.id}">Edit</button>
          <button type="button" class="danger" data-delete-area="${area.id}">Delete</button>
        </div>
      </article>
    `).join('') : '<div class="empty">No dining areas found.</div>';
  }

  const filterStorageKey = 'pos_menu_admin_filters';

  function syncFilters() {
    let saved = {};
    try {
      saved = JSON.parse(localStorage.getItem(filterStorageKey) || '{}');
    } catch {
      saved = {};
    }
    const savedCat = saved.category || '';
    const savedSubCat = saved.subcategory || '';

    setOptions($('filterCategory'), parentCategories(), { 
      placeholder: 'All Categories', 
      includeBlank: true,
      selected: savedCat
    });

    updateSubCategoryFilter(savedCat, savedSubCat);
  }

  function updateSubCategoryFilter(parentCatId, selectedSubCatId = '') {
    const subCats = parentCatId ? subCategories(parentCatId) : [];
    setOptions($('filterSubCategory'), subCats, { 
      placeholder: 'All Subcategories', 
      includeBlank: true, 
      selected: selectedSubCatId 
    });
  }

  function renderAll() {
    syncSelects();
    syncFilters();
    initCustomSelects();
    renderItems();
    renderCategories();
    renderTables();
    renderAreas();
    renderGallery();
    renderReports();
    
    if (state.token) {
      document.body.classList.add('logged-in');
      document.body.classList.remove('logged-out');
    } else {
      document.body.classList.add('logged-out');
      document.body.classList.remove('logged-in');
    }

    $('authStatus').textContent = state.token
      ? 'Logged in for ' + (state.client || 'selected client') + '.'
      : 'Use the same POS login details before making changes.';
  }

  async function loadClients() {
    const clients = await api('/auth/clients');
    state.clients = clients || [];

    const clientSelectEl = $('clientSelect');
    if (clientSelectEl && !clientSelectEl.isMock) {
      clientSelectEl.innerHTML = state.clients.map((client) => (
        `<option value="${escapeHtml(client.slug)}">${escapeHtml(client.name)}</option>`
      )).join('');
    }

    if (!state.client && state.clients[0]) {
      const cc = state.clients.find(c => c.slug === 'chaychaupal');
      state.client = cc ? cc.slug : state.clients[0].slug;
    }

    if (clientSelectEl && !clientSelectEl.isMock) {
      clientSelectEl.value = state.client;
      state.client = clientSelectEl.value;
      // Force the custom-select UI to rebuild from the freshly-added options,
      // in case the MutationObserver didn't pick up the async innerHTML change.
      clientSelectEl.dispatchEvent(new CustomEvent('select-value-synced'));
    }
  }

  async function loadItemsPageData(force = false) {
    let cachedCats = null;
    let cachedItems = null;
    if (!force) {
      cachedCats = getCachedData('categories');
      cachedItems = getCachedData('items');
      if (cachedCats && cachedItems) {
        state.categories = cachedCats;
        state.items = cachedItems;
        renderAll();
      }
    }

    try {
      const [categories, items] = await Promise.all([
        api('/categories'),
        api('/menu-items')
      ]);
      const catsData = categories || [];
      const itemsData = items || [];
      
      const catsChanged = JSON.stringify(catsData) !== JSON.stringify(cachedCats);
      const itemsChanged = JSON.stringify(itemsData) !== JSON.stringify(cachedItems);
      
      setCachedData('categories', catsData);
      setCachedData('items', itemsData);
      
      if (catsChanged || itemsChanged || !cachedCats || !cachedItems || force) {
        state.categories = catsData;
        state.items = itemsData;
        renderAll();
      }
    } catch (error) {
      console.error("Background sync items failed:", error);
      if (!cachedCats || !cachedItems) {
        throw error;
      }
    }
  }

  async function loadCategoriesPageData(force = false) {
    let cachedCats = null;
    if (!force) {
      cachedCats = getCachedData('categories');
      if (cachedCats) {
        state.categories = cachedCats;
        renderAll();
      }
    }

    try {
      const categories = await api('/categories');
      const catsData = categories || [];
      
      const catsChanged = JSON.stringify(catsData) !== JSON.stringify(cachedCats);
      setCachedData('categories', catsData);
      
      if (catsChanged || !cachedCats || force) {
        state.categories = catsData;
        renderAll();
      }
    } catch (error) {
      console.error("Background sync categories failed:", error);
      if (!cachedCats) throw error;
    }
  }

  async function loadTablesPageData(force = false) {
    let cachedTables = null;
    let cachedAreas = null;
    if (!force) {
      cachedTables = getCachedData('tables');
      cachedAreas = getCachedData('areas');
      if (cachedTables && cachedAreas) {
        state.tables = cachedTables;
        state.areas = cachedAreas;
        renderAll();
      }
    }

    try {
      const [tables, areas] = await Promise.all([
        api('/tables'),
        api('/dining-areas')
      ]);
      const tablesData = tables || [];
      const areasData = areas || [];
      
      const tablesChanged = JSON.stringify(tablesData) !== JSON.stringify(cachedTables);
      const areasChanged = JSON.stringify(areasData) !== JSON.stringify(cachedAreas);
      
      setCachedData('tables', tablesData);
      setCachedData('areas', areasData);
      
      if (tablesChanged || areasChanged || !cachedTables || !cachedAreas || force) {
        state.tables = tablesData;
        state.areas = areasData;
        renderAll();
      }
    } catch (error) {
      console.error("Background sync tables failed:", error);
      if (!cachedTables || !cachedAreas) throw error;
    }
  }

  async function loadGalleryPageData(force = false) {
    let cachedGallery = null;
    let cachedCats = null;
    let cachedItems = null;
    if (!force) {
      cachedGallery = getCachedData('gallery');
      cachedCats = getCachedData('categories');
      cachedItems = getCachedData('items');
      if (cachedGallery && cachedCats && cachedItems) {
        state.gallery = cachedGallery;
        state.categories = cachedCats;
        state.items = cachedItems;
        renderAll();
      }
    }

    try {
      const [gallery, categories, items] = await Promise.all([
        api('/gallery').catch(() => []),
        api('/categories'),
        api('/menu-items')
      ]);
      const galleryData = gallery || [];
      const catsData = categories || [];
      const itemsData = items || [];
      
      const galleryChanged = JSON.stringify(galleryData) !== JSON.stringify(cachedGallery);
      const catsChanged = JSON.stringify(catsData) !== JSON.stringify(cachedCats);
      const itemsChanged = JSON.stringify(itemsData) !== JSON.stringify(cachedItems);
      
      setCachedData('gallery', galleryData);
      setCachedData('categories', catsData);
      setCachedData('items', itemsData);
      
      if (galleryChanged || catsChanged || itemsChanged || !cachedGallery || !cachedCats || !cachedItems || force) {
        state.gallery = galleryData;
        state.categories = catsData;
        state.items = itemsData;
        renderAll();
      }
    } catch (error) {
      console.error("Background sync gallery failed:", error);
      if (!cachedGallery || !cachedCats || !cachedItems) throw error;
    }
  }

  async function loadActiveTab(tab, force = false) {
    switch (tab) {
      case 'items':
        await loadItemsPageData(force);
        break;
      case 'categories':
        await loadCategoriesPageData(force);
        break;
      case 'tables':
        await loadTablesPageData(force);
        break;
      case 'gallery':
        await loadGalleryPageData(force);
        break;
      case 'reports':
        await loadReportsData(force);
        break;
      case 'comparison':
        await loadComparisonData();
        break;
      case 'mobile-menu':
        await loadMobileMenuPageData(force);
        break;
      default:
        await loadItemsPageData(force);
        break;
    }

  }

  async function loadData(force = false) {
    const activeTab = localStorage.getItem('pos_menu_active_tab') || 'items';
    const shouldForce = force || state.needsForceReload;
    state.needsForceReload = false;
    await loadActiveTab(activeTab, shouldForce);
  }

  async function loadMobileMenuPageData() {
    try {
      const settings = await api('/settings');
      const setting = settings.find(row => row.key === 'mobile_menu_download_images');
      state.mobileDownloadImages = Array.isArray(setting?.value) ? setting.value.filter(image => image && image.url) : [];
      renderMobileDownloadImages();
    } catch (error) {
      state.mobileDownloadImages = [];
      renderMobileDownloadImages();
      console.warn('Failed to load mobile menu download images:', error);
    }
  }

  async function saveMobileDownloadImages() {
    await api('/settings/mobile_menu_download_images', {
      method: 'PUT',
      body: JSON.stringify({ value: state.mobileDownloadImages })
    });
  }

  async function refresh() {
    try {
      await loadClients();
      await loadData(true);
      saveSession();
      toast('Data refreshed.');
    } catch (error) {
      toast(error.message);
      renderAll();
    }
  }

  function initMobilePreview() {
    const select = $('mobilePreviewDevice');
    const frame = $('mobileDeviceFrame');
    const scaleWrap = $('mobileDeviceScale');
    const iframe = $('mobileMenuPreviewFrame');
    const refreshBtn = $('mobilePreviewRefresh');
    const stage = document.querySelector('.mobile-preview-stage');
    if (!select || select.isMock || !frame || frame.isMock || !scaleWrap || scaleWrap.isMock || !stage) return;

    const devices = {
      'iphone-se': { className: 'device-iphone-se', width: 375, height: 667 },
      'iphone-12': { className: 'device-iphone-12', width: 390, height: 844 },
      'iphone-14-pro': { className: 'device-iphone-14-pro', width: 393, height: 852 },
      'pixel-7': { className: 'device-pixel-7', width: 412, height: 915 },
      'samsung-s22': { className: 'device-samsung-s22', width: 360, height: 780 }
    };
    const deviceClasses = Object.values(devices).map(device => device.className);

    const fitDevice = () => {
      const device = devices[select.value] || devices['iphone-12'];
      const stageBox = stage.getBoundingClientRect();
      const chromePadding = 28;
      const maxWidth = Math.max(240, stageBox.width - 24);
      const maxHeight = Math.max(360, stageBox.height - 24);
      const scale = Math.min(1, maxWidth / (device.width + chromePadding), maxHeight / (device.height + chromePadding));

      scaleWrap.style.setProperty('--device-width', `${device.width}px`);
      scaleWrap.style.setProperty('--device-height', `${device.height}px`);
      scaleWrap.style.setProperty('--preview-scale', String(Math.max(0.45, scale)));
      frame.classList.remove(...deviceClasses);
      frame.classList.add(device.className);
    };

    if (!select.hasMobilePreviewListener) {
      select.addEventListener('change', () => {
        fitDevice();
      });
      select.hasMobilePreviewListener = true;
    }

    fitDevice();

    if (!frame.hasMobilePreviewResizeListener) {
      window.addEventListener('resize', fitDevice);
      frame.hasMobilePreviewResizeListener = true;
    }

    if (refreshBtn && !refreshBtn.isMock && iframe && !iframe.isMock && !refreshBtn.hasMobilePreviewListener) {
      refreshBtn.addEventListener('click', () => {
        const baseSrc = (iframe.getAttribute('src') || '../mobilemenu/dist/index.html').split('#')[0].split('?')[0];
        // Cache-bust param is '_cb' (NOT 't'): the mobile-menu PWA reads ?t= / ?table=
        // as a QR TABLE TOKEN, so a Date.now() value in 't' made it try to validate a
        // bogus table on every preview refresh -> "Failed to validate QR token" alert.
        iframe.src = `${baseSrc}?client=${encodeURIComponent(state.client)}&_cb=${Date.now()}`;
      });
      refreshBtn.hasMobilePreviewListener = true;
    }

    bindMobileDownloadUpload();
    renderMobileDownloadImages();
  }

  function renderMobileDownloadImages() {
    const grid = $('mobileDownloadPreviewGrid');
    if (!grid || grid.isMock) return;

    if (!state.mobileDownloadImages.length) {
      grid.innerHTML = '<div class="empty" style="grid-column: 1 / -1;">No download images uploaded yet.</div>';
      return;
    }

    grid.innerHTML = state.mobileDownloadImages.map((image, index) => `
      <div class="mobile-download-card">
        <img src="${escapeHtml(getCleanImageUrl(image.url))}" alt="${escapeHtml(image.filename || 'Menu image')}">
        <div class="mobile-download-card-footer">
          <span class="mobile-download-card-name" title="${escapeHtml(image.filename || 'Menu image')}">${escapeHtml(image.filename || `Image ${index + 1}`)}</span>
          <a class="ghost" href="${escapeHtml(image.url)}" target="_blank" rel="noopener" title="Open image">↗</a>
          <button type="button" class="danger" data-remove-mobile-download-index="${index}" title="Remove image">×</button>
        </div>
      </div>
    `).join('');
  }

  function refreshMobilePreviewFrame() {
    const iframe = $('mobileMenuPreviewFrame');
    if (!iframe || iframe.isMock) return;
    const baseSrc = (iframe.getAttribute('src') || '../mobilemenu/dist/index.html').split('#')[0].split('?')[0];
    // Cache-bust param is '_cb' (NOT 't'): the mobile-menu PWA reads ?t= / ?table=
    // as a QR TABLE TOKEN, so a Date.now() value in 't' made it try to validate a
    // bogus table on every preview refresh -> "Failed to validate QR token" alert.
    iframe.src = `${baseSrc}?client=${encodeURIComponent(state.client)}&_cb=${Date.now()}`;
  }

  function bindMobileDownloadUpload() {
    const input = $('mobileDownloadFileInput');
    const uploadBtn = $('mobileDownloadUploadBtn');
    const dropZone = $('mobileDownloadDropZone');
    if (!input || input.isMock) return;

    const uploadFiles = async (files) => {
      const imageFiles = Array.from(files || []).filter(file => file.type.startsWith('image/'));
      if (!imageFiles.length) {
        toast('Please select valid image files.');
        return;
      }

      setUploadProgress('mobileDownload', 1, `Preparing ${imageFiles.length} image${imageFiles.length > 1 ? 's' : ''}...`);
      const uploadStartedAt = Date.now();

      try {
        for (let index = 0; index < imageFiles.length; index++) {
          const file = imageFiles[index];
          const data = await uploadImageFile(file, (percent) => {
            const totalPercent = ((index + (percent / 100)) / imageFiles.length) * 100;
            setUploadProgress('mobileDownload', totalPercent, uploadEtaLabel(`Uploading ${index + 1} of ${imageFiles.length}: ${file.name}`, totalPercent, uploadStartedAt));
          }, 'download');
          state.mobileDownloadImages.push({
            url: data.url,
            filename: file.name,
            uploaded_at: new Date().toISOString()
          });
          renderMobileDownloadImages();
        }

        await saveMobileDownloadImages();
        finishUploadProgress('mobileDownload');
        refreshMobilePreviewFrame();
        toast('Mobile download images uploaded.');
      } catch (error) {
        resetUploadProgress('mobileDownload');
        toast('Mobile download image upload failed: ' + error.message);
      } finally {
        input.value = '';
      }
    };

    if (dropZone && !dropZone.isMock && !dropZone.hasMobileDownloadListener) {
      dropZone.addEventListener('dragover', (event) => {
        event.preventDefault();
        dropZone.classList.add('dragover');
      });
      dropZone.addEventListener('dragleave', () => dropZone.classList.remove('dragover'));
      dropZone.addEventListener('drop', async (event) => {
        event.preventDefault();
        dropZone.classList.remove('dragover');
        await uploadFiles(event.dataTransfer.files);
      });
      dropZone.hasMobileDownloadListener = true;
    }

    if (!input.hasMobileDownloadListener) {
      input.addEventListener('change', async () => {
        state.filePickerOpen = false;
        await uploadFiles(input.files);
      });

      input.hasMobileDownloadListener = true;
    }
  }

  // SPA Page Loader
  async function navigateToPage(tab, url) {
    try {
      document.body.classList.add('page-loading');
      
      const response = await fetch(url);
      if (!response.ok) throw new Error('Failed to load page.');
      const htmlText = await response.text();
      
      const parser = new DOMParser();
      const doc = parser.parseFromString(htmlText, 'text/html');
      const newWorkspace = doc.querySelector('.workspace-container');
      
      if (newWorkspace) {
        const currentWorkspace = document.querySelector('.workspace-container');
        if (currentWorkspace) {
          currentWorkspace.innerHTML = newWorkspace.innerHTML;
        }
        
        document.body.dataset.activeTab = tab;
        localStorage.setItem('pos_menu_active_tab', tab);
        
        history.pushState(null, '', url);
        
        document.querySelectorAll('.tab').forEach((btn) => {
          if (btn.dataset.tab === tab) {
            btn.classList.add('active');
          } else {
            btn.classList.remove('active');
          }
        });
        
        bindEvents();
        // The workspace innerHTML was replaced, so any <select> lost its custom
        // dropdown wrapper and the client dropdown lost its options. Re-populate
        // the client list and re-wrap the new selects, else the SELECT CLIENT
        // dropdown shows empty and won't open after switching pages.
        if (tab === 'reports') {
          await loadClients();
        }
        initCustomSelects();
        await loadActiveTab(tab);
      }
    } catch (e) {
      console.error(e);
      window.location.href = url;
    } finally {
      document.body.classList.remove('page-loading');
    }
  }

  // Handle popstate for browser back/forward buttons
  window.addEventListener('popstate', async () => {
    const path = window.location.pathname;
    const page = path.substring(path.lastIndexOf('/') + 1) || 'items.php';
    let tab = 'items';
    if (page.startsWith('categories')) tab = 'categories';
    else if (page.startsWith('tables')) tab = 'tables';
    else if (page.startsWith('gallery')) tab = 'gallery';
    else if (page.startsWith('reports')) tab = 'reports';
    else if (page.startsWith('mobile-menu')) tab = 'mobile-menu';
    else if (page.startsWith('items') || page.startsWith('index')) tab = 'items';
    
    try {
      document.body.classList.add('page-loading');
      const response = await fetch(page === 'index.php' || page === '' ? 'items.php' : page);
      if (!response.ok) throw new Error();
      const htmlText = await response.text();
      
      const parser = new DOMParser();
      const doc = parser.parseFromString(htmlText, 'text/html');
      const newWorkspace = doc.querySelector('.workspace-container');
      if (newWorkspace) {
        document.querySelector('.workspace-container').innerHTML = newWorkspace.innerHTML;
        document.body.dataset.activeTab = tab;
        localStorage.setItem('pos_menu_active_tab', tab);
        
        document.querySelectorAll('.tab').forEach((btn) => {
          if (btn.dataset.tab === tab) btn.classList.add('active');
          else btn.classList.remove('active');
        });
        
        bindEvents();
        if (tab === 'reports') {
          await loadClients();
        }
        initCustomSelects();
        await loadActiveTab(tab);
      }
    } catch (e) {
      window.location.reload();
    } finally {
      document.body.classList.remove('page-loading');
    }
  });

  async function fetchTransliteration(text) {
    if (!text.trim()) return [];
    
    // Only transliterate English words (alphabetic characters)
    if (!/^[a-zA-Z]+$/.test(text)) {
      return [];
    }

    // Determine the local API base URL (relative to the current page location)
    const localBase = new URL('../', window.location.href);
    const isLocalhost = window.location.hostname === 'localhost' || window.location.hostname === '127.0.0.1';
    
    // If we are running locally, we can always use the local server for transliteration 
    // to bypass any missing endpoints on a remote production API.
    if (isLocalhost) {
      try {
        const localUrl = new URL(localBase.toString());
        localUrl.pathname = (localUrl.pathname.replace(/\/+$/, '') + '/transliterate').replace(/\/{2,}/g, '/');
        localUrl.searchParams.set('text', text);
        
        const response = await fetch(localUrl.toString());
        if (response.ok) {
          const payload = await response.json().catch(() => ({}));
          if (payload && payload.success && Array.isArray(payload.data)) {
            return payload.data;
          }
        }
      } catch (e) {
        console.warn('Local transliteration fetch failed, falling back to configured API:', e);
      }
    }

    // Default/Fallback to configured API base
    try {
      return await api('/transliterate?text=' + encodeURIComponent(text));
    } catch (e) {
      console.error('Transliteration failed:', e);
    }
    return [];
  }

  function selectSuggestion(suggestion) {
    const val = $('itemName').value;
    const selectionEnd = $('itemName').selectionEnd;
    const textBeforeCursor = val.substring(0, selectionEnd);
    const textAfterCursor = val.substring(selectionEnd);

    // split using capture group to preserve whitespace tokens
    const words = textBeforeCursor.split(/(\s+)/);
    
    // Find the last non-empty word token
    let replaced = false;
    for (let i = words.length - 1; i >= 0; i--) {
      if (words[i].trim() !== '') {
        words[i] = suggestion;
        replaced = true;
        break;
      }
    }

    const newTextBeforeCursor = words.join('') + ' ';
    $('itemName').value = newTextBeforeCursor + textAfterCursor;
    
    const newCursorPos = newTextBeforeCursor.length;
    $('itemName').setSelectionRange(newCursorPos, newCursorPos);
    $('itemName').focus();
    
    $('translitSuggestions').innerHTML = '<span class="placeholder-suggestion">Type to see suggestions...</span>';
    state.currentSuggestions = [];
  }

  function setUploadProgress(scope, percent, label = 'Uploading image...') {
    const box = $(`${scope}UploadProgress`);
    const bar = $(`${scope}UploadProgressBar`);
    const pct = $(`${scope}UploadProgressPercent`);
    const text = $(`${scope}UploadProgressLabel`);
    const cleanPercent = Math.max(0, Math.min(100, Math.round(percent || 0)));

    if (box && !box.isMock) {
      box.style.display = 'block';
      if (bar && !bar.isMock) bar.style.width = cleanPercent + '%';
      if (pct && !pct.isMock) pct.textContent = cleanPercent + '%';
      if (text && !text.isMock) text.textContent = label;
    }

    const globalBox = $('globalUploadProgress');
    const globalBar = $('globalUploadProgressBar');
    const globalPct = $('globalUploadProgressPercent');
    const globalText = $('globalUploadProgressLabel');
    if (globalBox && !globalBox.isMock) {
      globalBox.style.display = 'block';
      if (globalBar && !globalBar.isMock) globalBar.style.width = cleanPercent + '%';
      if (globalPct && !globalPct.isMock) globalPct.textContent = cleanPercent + '%';
      if (globalText && !globalText.isMock) globalText.textContent = label;
    }
  }

  function resetUploadProgress(scope) {
    const box = $(`${scope}UploadProgress`);
    const bar = $(`${scope}UploadProgressBar`);
    if (box && !box.isMock) box.style.display = 'none';
    if (bar && !bar.isMock) bar.style.width = '0%';
    const globalBox = $('globalUploadProgress');
    const globalBar = $('globalUploadProgressBar');
    if (globalBox && !globalBox.isMock) globalBox.style.display = 'none';
    if (globalBar && !globalBar.isMock) globalBar.style.width = '0%';
  }

  function uploadEtaLabel(baseLabel, totalPercent, startedAt) {
    if (!startedAt || totalPercent <= 0 || totalPercent >= 100) return baseLabel;
    const elapsedMs = Date.now() - startedAt;
    const remainingMs = (elapsedMs / totalPercent) * (100 - totalPercent);
    const remainingSeconds = Math.max(1, Math.ceil(remainingMs / 1000));
    return `${baseLabel} • about ${remainingSeconds}s left`;
  }

  function setItemUploadBusy(isBusy) {
    const zone = $('imageUploadZone');
    const placeholderText = document.querySelector('#uploadPlaceholder .upload-text');
    const placeholderIcon = document.querySelector('#uploadPlaceholder .upload-icon');
    if (zone && !zone.isMock) zone.classList.toggle('is-uploading', Boolean(isBusy));
    if (placeholderText) {
      placeholderText.textContent = isBusy ? 'Uploading product image...' : 'Upload Product Images (Drag & Drop)';
    }
    if (placeholderIcon) {
      placeholderIcon.textContent = isBusy ? '⏳' : '📁';
    }
  }

  function finishItemUploadProgress() {
    setUploadProgress('item', 100, 'Upload complete');
    setTimeout(() => {
      resetUploadProgress('item');
      setItemUploadBusy(false);
    }, 650);
  }

  function finishUploadProgress(scope, label = 'Upload complete') {
    setUploadProgress(scope, 100, label);
    setTimeout(() => resetUploadProgress(scope), 650);
  }

  function openFilePickerOnce(fileInput) {
    if (!fileInput || fileInput.isMock || state.filePickerOpen) return false;
    state.filePickerOpen = true;

    const unlock = () => {
      window.setTimeout(() => {
        state.filePickerOpen = false;
      }, 450);
    };

    window.addEventListener('focus', unlock, { once: true });
    fileInput.click();
    window.setTimeout(unlock, 1200);
    return true;
  }

  function itemImageList(item) {
    if (!item || !item.image) return [];
    try {
      if (String(item.image).trim().startsWith('[')) {
        const parsed = JSON.parse(item.image);
        return Array.isArray(parsed) ? parsed.filter(Boolean) : [];
      }
    } catch (_) {
      return [];
    }
    return [item.image].filter(Boolean);
  }

  async function uploadAndAssignImageToItem(itemId, file, onProgress = null) {
    const item = state.items.find(i => Number(i.id) === Number(itemId));
    if (!item) throw new Error('Menu item not found.');

    toast('Uploading item image...');
    const uploadRes = await uploadImageFile(file, onProgress);
    const imageUrl = uploadRes.url;

    await api('/gallery', {
      method: 'POST',
      body: JSON.stringify({
        url: imageUrl,
        filename: file.name,
        is_visible: 1,
        category_id: item.category_id || null,
        sub_category_id: item.sub_category_id || null
      })
    });

    let descObj = {};
    try {
      descObj = JSON.parse(item.description || '{}');
    } catch (_) {}

    await api('/menu-items/' + itemId, {
      method: 'PUT',
      body: JSON.stringify({
        name: item.name,
        category_id: item.category_id,
        sub_category_id: item.sub_category_id,
        price: item.price,
        image: JSON.stringify([imageUrl]),
        description: JSON.stringify(descObj),
        is_veg: Number(item.is_veg) ? 1 : 0,
        is_available: Number(item.is_available) ? 1 : 0
      })
    });

    return imageUrl;
  }

  async function ensureLatestGallery() {
    try {
      const latest = await api('/gallery');
      state.gallery = latest || [];
    } catch (e) {
      console.warn('Failed to update gallery state:', e);
    }
  }

  async function uploadImageFile(file, onProgress = null, variant = null) {
    const formData = new FormData();
    formData.append('image', file);
    // 'variant' lets the server pick the right max size: 'download' = full menu
    // cards customers save/zoom (keep higher res), default = small UI thumbnails.
    if (variant) {
      formData.append('variant', variant);
    }

    const reqHeaders = {};
    if (state.client) {
      reqHeaders['X-POS-Client'] = state.client;
    }
    if (state.token) {
      reqHeaders.Authorization = 'Bearer ' + state.token;
    }

    return await new Promise((resolve, reject) => {
      const xhr = new XMLHttpRequest();
      xhr.open('POST', endpoint('/upload'));
      Object.entries(reqHeaders).forEach(([key, value]) => xhr.setRequestHeader(key, value));

      xhr.upload.onprogress = (event) => {
        if (event.lengthComputable && typeof onProgress === 'function') {
          onProgress(Math.round((event.loaded / event.total) * 100));
        }
      };

      xhr.onload = () => {
        const payload = (() => {
          try {
            return JSON.parse(xhr.responseText || '{}');
          } catch (_) {
            return {};
          }
        })();

        if (xhr.status < 200 || xhr.status >= 300 || payload.success === false) {
          reject(new Error(payload.message || 'Upload failed.'));
          return;
        }

        if (typeof onProgress === 'function') onProgress(100);
        resolve(payload.data);
      };

      xhr.onerror = () => reject(new Error('Upload failed. Please check your connection.'));
      xhr.onabort = () => reject(new Error('Upload cancelled.'));
      xhr.send(formData);
    });
  }

  function renderUploadedImagesGrid() {
    const grid = $('itemImagesGrid');
    if (!grid) return;
    
    if (!state.itemImages || !state.itemImages.length) {
      grid.innerHTML = '';
      grid.style.display = 'none';
      return;
    }
    
    grid.innerHTML = state.itemImages.map((url, index) => {
      const isPrimary = index === 0;
      return `
        <div class="image-preview-card" data-index="${index}">
          <img src="${escapeHtml(getCleanImageUrl(url))}" alt="Preview ${index + 1}">
          <button type="button" class="remove-preview-btn" data-remove-index="${index}" title="Remove image">&times;</button>
          ${isPrimary ? '<span class="primary-badge">Main</span>' : ''}
        </div>
      `;
    }).join('');
    
    // Bind click events on remove buttons
    grid.querySelectorAll('.remove-preview-btn').forEach((btn) => {
      btn.addEventListener('click', (e) => {
        e.preventDefault();
        e.stopPropagation();
        const index = Number(btn.dataset.removeIndex);
        state.itemImages.splice(index, 1);
        renderUploadedImagesGrid();
        toast('Image removed.');
      });
    });
    
    grid.style.display = 'grid';
  }

  function resetItemForm() {
    $('itemForm').reset();
    $('itemId').value = '';
    $('itemDescription').value = '';
    $('itemImage').value = '';
    $('itemCode').value = '';
    $('itemVeg').checked = true;
    $('itemAvailable').checked = true;
    $('itemFavorite').checked = false;
    $('itemFormTitle').textContent = 'Add New Menu Item';
    setOptions($('itemSubCategory'), [], { placeholder: 'No subcategory' });
    
    // Reset multiple image uploader elements
    state.itemImages = [];
    $('itemImageFile').value = '';
    renderUploadedImagesGrid();
    resetUploadProgress('item');
    setItemUploadBusy(false);
    
    // Hide last added container when starting clear
    $('lastAddedContainer').style.display = 'none';

    // Set Hindi as default active language
    document.querySelectorAll('.lang-btn').forEach(b => b.classList.remove('active'));
    const hiBtn = document.querySelector('.lang-btn[data-lang="hindi"]');
    if (hiBtn) hiBtn.classList.add('active');
    $('itemName').placeholder = 'Type in English for Hindi';
    $('translitContainer').style.display = 'flex';
    $('translitSuggestions').innerHTML = '<span class="placeholder-suggestion">Type to see suggestions...</span>';
  }

  function resetGalleryUploadForm() {
    $('galleryUploadCategory').value = '';
    setOptions($('galleryUploadSubCategory'), [], { placeholder: 'No subcategory' });
    $('galleryUploadCategory').dispatchEvent(new Event('change', { bubbles: true }));
    $('galleryUploadSubCategory').dispatchEvent(new Event('change', { bubbles: true }));

    $('galleryUploadFileInput').value = '';
    const fileList = $('galleryUploadFilesList');
    if (fileList) {
      fileList.innerHTML = '';
      fileList.style.display = 'none';
    }
    resetUploadProgress('gallery');
  }

  function resetCategoryForm() {
    $('categoryForm').reset();
    $('categoryId').value = '';
    $('categoryImage').value = '';
    $('categorySort').value = '0';
    $('categoryActive').checked = true;
    $('categoryFormTitle').textContent = 'Add Category';
    setOptions($('categoryParent'), parentCategories(), { placeholder: 'Main category' });

    // Reset category image upload UI elements
    $('categoryImageFile').value = '';
    resetUploadProgress('category');
    $('categoryUploadPreview').src = '';
    $('categoryUploadPreviewContainer').style.display = 'none';
    $('categoryUploadPlaceholder').style.display = 'flex';
  }

  function resetTableForm() {
    $('tableForm').reset();
    $('tableId').value = '';
    $('tableFormTitle').textContent = 'Add New Table';
    const submitBtn = $('tableForm').querySelector('button[type="submit"]');
    if (submitBtn) submitBtn.textContent = 'Create Table';
  }

  function resetAreaForm() {
    $('areaForm').reset();
    $('areaId').value = '';
    $('areaSort').value = '0';
    $('areaActive').checked = true;
    $('areaFormTitle').textContent = 'Add Dining Area';
  }

  function editItem(id) {
    const item = state.items.find((row) => Number(row.id) === Number(id));
    if (!item) return;
    $('itemId').value = item.id;
    $('itemName').value = item.name || '';
    $('itemCategory').value = item.category_id || '';
    
    // Trigger dynamic select wrapper updates
    $('itemCategory').dispatchEvent(new Event('change', { bubbles: true }));
    
    setOptions($('itemSubCategory'), subCategories(item.category_id), { placeholder: 'No subcategory', selected: item.sub_category_id });
    $('itemSubCategory').dispatchEvent(new Event('change', { bubbles: true }));
    
    $('itemPrice').value = item.price || '';
    $('itemImage').value = item.image || '';
    $('itemDescription').value = item.description || '';
    
    // Parse multiple images safely
    let imgs = [];
    if (item.image) {
      try {
        const parsed = JSON.parse(item.image);
        if (Array.isArray(parsed)) {
          imgs = parsed;
        } else if (typeof parsed === 'string') {
          imgs = [parsed];
        }
      } catch (e) {
        // Legacy single image path
        imgs = [item.image];
      }
    }
    state.itemImages = imgs;
    renderUploadedImagesGrid();
    
    // Parse description JSON to extract item code
    let code = '';
    if (item.description) {
      try {
        const parsed = JSON.parse(item.description);
        if (parsed && parsed.code) {
          code = parsed.code;
        }
      } catch (e) {}
    }
    $('itemCode').value = code;

    $('itemVeg').checked = asBool(item.is_veg);
    $('itemAvailable').checked = asBool(item.is_available);
    $('itemFavorite').checked = asBool(item.isFavorite || item.is_favorite);
    
    // Hide last added container on edit start
    $('lastAddedContainer').style.display = 'none';
    
    // Set English as active language for edits
    document.querySelectorAll('.lang-btn').forEach(b => b.classList.remove('active'));
    const enBtn = document.querySelector('.lang-btn[data-lang="english"]');
    if (enBtn) enBtn.classList.add('active');
    $('itemName').placeholder = 'Enter item name in English';
    $('translitContainer').style.display = 'none';

    $('itemFormTitle').textContent = 'Edit Menu Item';
    openModal('itemModal');
  }

  function editCategory(id) {
    const category = state.categories.find((row) => Number(row.id) === Number(id));
    if (!category) return;
    $('categoryId').value = category.id;
    $('categoryName').value = category.name || '';
    setOptions($('categoryParent'), parentCategories(), {
      placeholder: 'Main category',
      selected: category.parent_id,
      filter: (row) => Number(row.id) !== Number(category.id)
    });
    $('categorySort').value = category.sort_order || 0;
    $('categoryActive').checked = asBool(category.is_active);

    // Sync category image in edit Mode
    $('categoryImage').value = category.image || '';
    if (category.image) {
      $('categoryUploadPreview').src = getCleanImageUrl(category.image);
      $('categoryUploadPreviewContainer').style.display = 'block';
      $('categoryUploadPlaceholder').style.display = 'none';
    } else {
      $('categoryImageFile').value = '';
      $('categoryUploadPreview').src = '';
      $('categoryUploadPreviewContainer').style.display = 'none';
      $('categoryUploadPlaceholder').style.display = 'flex';
    }

    $('categoryFormTitle').textContent = 'Edit Category';
    openModal('categoryModal');
  }

  function editTable(id) {
    const table = state.tables.find((row) => Number(row.id) === Number(id));
    if (!table) return;
    $('tableId').value = table.id;
    $('tableNumber').value = table.table_number || '';
    $('tableArea').value = table.area_id || '';
    
    // Sync custom dropdown wrappers
    $('tableArea').dispatchEvent(new Event('change', { bubbles: true }));

    $('tableFormTitle').textContent = 'Edit Table';
    const submitBtn = $('tableForm').querySelector('button[type="submit"]');
    if (submitBtn) submitBtn.textContent = 'Save Changes';
    openModal('tableModal');
  }

  function editArea(id) {
    const area = state.areas.find((row) => Number(row.id) === Number(id));
    if (!area) return;
    $('areaId').value = area.id;
    $('areaName').value = area.name || '';
    $('areaSort').value = area.sort_order || 0;
    $('areaActive').checked = asBool(area.is_active);
    $('areaFormTitle').textContent = 'Edit Dining Area';
    openModal('areaModal');
  }

  async function remove(path, message) {
    const confirmed = await showConfirm('Confirm Delete', message, 'Yes, Delete', 'Cancel');
    if (!confirmed) return;
    await api(path, { method: 'DELETE' });
    await loadData();
    toast('Successfully deleted.');
  }

  function bindEvents() {
    initMobilePreview();
    
    // Global Search Hotkey listener (press '/' to focus search input, 'Escape' to blur)
    document.addEventListener('keydown', (e) => {
      const activeEl = document.activeElement;
      if (activeEl && ['INPUT', 'TEXTAREA', 'SELECT'].includes(activeEl.tagName)) {
        if (e.key === 'Escape' && activeEl.id.toLowerCase().includes('search')) {
          activeEl.blur();
        }
        return;
      }
      if (e.key === '/') {
        const searchInputs = [
          'itemSearch',
          'comparisonSearchInput',
          'billSearchInput',
          'gallerySearch',
          'assignItemSearch',
          'gallerySelectSearch'
        ];
        for (const id of searchInputs) {
          const el = $(id);
          if (el && !el.isMock && el.offsetWidth > 0 && el.offsetHeight > 0) {
            e.preventDefault();
            el.focus();
            el.select();
            break;
          }
        }
      }
    });

    bindSafe('refreshBtn', 'click', refresh);
    bindSafe('clientSelect', 'change', async () => {
      state.client = $('clientSelect').value;
      saveSession();
      await loadData();
    });
    bindSafe('comparisonMonthChips', 'click', (e) => {
      const btn = e.target.closest('[data-comp-month]');
      if (!btn) return;
      e.preventDefault();
      const key = btn.dataset.compMonth;
      const selected = state.comparisonMonths || [];
      if (selected.includes(key)) {
        if (selected.length <= 1) return; // keep at least one month selected
        state.comparisonMonths = selected.filter(m => m !== key);
      } else {
        if (selected.length >= 4) {
          toast('Max 4 months compare kar sakte hain.');
          return;
        }
        state.comparisonMonths = [...selected, key];
      }
      loadComparisonData();
    });
    bindSafe('comparisonSearchInput', 'input', (e) => {
      state.comparisonSearch = e.target.value;
      state.comparisonPage = 1;
      renderComparisonTable();
    });
    bindSafe('comparisonFilterCategory', 'change', () => {
      const catVal = $('comparisonFilterCategory').value;
      const subSelect = $('comparisonFilterSubCategory');
      if (subSelect && !subSelect.isMock) {
        setOptions(subSelect, subCategories(catVal), { placeholder: 'All Subcategories', includeBlank: true });
      }
      state.comparisonPage = 1;
      renderComparisonTable();
    });
    bindSafe('comparisonFilterSubCategory', 'change', () => {
      state.comparisonPage = 1;
      renderComparisonTable();
    });
    bindSafe('loginMode', 'change', () => {
      const mobile = $('loginMode').value === 'mobile';
      $('emailInput').hidden = mobile;
      $('passwordInput').hidden = mobile;
      $('mobileInput').hidden = !mobile;
      $('pinInput').hidden = !mobile;
    });
    bindSafe('logoutBtn', 'click', () => {
      state.token = '';
      saveSession();
      renderAll();
      toast('Logged out.');
    });
    bindSafe('loginForm', 'submit', async (event) => {
      event.preventDefault();
      try {
        const mobile = $('loginMode').value === 'mobile';
        const clientSlug = state.client || 'chaychaupal';
        const data = mobile
          ? { client: clientSlug, mobile: $('mobileInput').value.trim(), pin: $('pinInput').value.trim() }
          : { client: clientSlug, email: $('emailInput').value.trim(), password: $('passwordInput').value };
        const session = await api('/auth/login', { method: 'POST', body: JSON.stringify(data) });
        state.token = session.token || '';
        state.client = session.client?.slug || state.client;
        $('clientSelect').value = state.client;
        
        if (mobile) {
          localStorage.setItem('pos_last_mobile', $('mobileInput').value.trim());
          localStorage.setItem('pos_last_pin', $('pinInput').value.trim());
        }
        
        saveSession();
        renderAll();
        await loadData();
        toast('Login successful.');
      } catch (error) {
        toast(error.message);
      }
    });

    const updatePinBoxes = () => {
      const pinInput = $('pinInput');
      if (!pinInput || pinInput.isMock) return;
      const len = pinInput.value.length;
      const boxes = document.querySelectorAll('.pin-box');
      boxes.forEach((box, i) => {
        if (i < len) {
          box.classList.add('filled');
          box.textContent = '•';
        } else {
          box.classList.remove('filled');
          box.textContent = '';
        }
        if (i === len && document.activeElement === pinInput) {
          box.classList.add('active');
        } else {
          box.classList.remove('active');
        }
      });
    };

    listen('mobileInput', 'input', () => {
      localStorage.setItem('pos_last_mobile', $('mobileInput').value.trim());
    });
    listen('pinInput', 'input', () => {
      localStorage.setItem('pos_last_pin', $('pinInput').value.trim());
      updatePinBoxes();
    });
    listen('pinInput', 'focus', updatePinBoxes);
    listen('pinInput', 'blur', updatePinBoxes);

    document.querySelectorAll('.tab').forEach((button) => {
      if (button.hasListener) return;
      button.addEventListener('click', (e) => {
        const tab = button.dataset.tab;
        const currentTab = document.body.dataset.activeTab || 'items';
        
        if (tab === currentTab) {
          e.preventDefault();
          return;
        }
        
        e.preventDefault();
        const href = button.getAttribute('href');
        
        // Remember the active tab for seamless redirects/load
        localStorage.setItem('pos_menu_active_tab', tab);
        
        // Auto-close sidebar on mobile
        document.body.classList.remove('sidebar-open');
        
        // SPA navigation
        navigateToPage(tab, href);
      });
      button.hasListener = true;
    });

    // Mobile sidebar toggle click bindings
    bindSafe('mobileMenuToggle', 'click', (e) => {
      e.stopPropagation();
      document.body.classList.toggle('sidebar-open');
    });

    bindSafe('sidebarOverlay', 'click', () => {
      document.body.classList.remove('sidebar-open');
    });

    document.querySelectorAll('.sub-tab').forEach((button) => {
      if (button.hasListener) return;
      button.addEventListener('click', () => {
        document.querySelectorAll('.sub-tab').forEach((tab) => tab.classList.remove('active'));
        button.classList.add('active');

        const subTab = button.dataset.subTab;
        if (subTab === 'table-list') {
          $('subTabTables').style.display = 'block';
          $('subTabAreas').style.display = 'none';
          $('addTableBtn').style.display = 'inline-flex';
          $('addAreaBtn').style.display = 'none';
        } else {
          $('subTabTables').style.display = 'none';
          $('subTabAreas').style.display = 'block';
          $('addTableBtn').style.display = 'none';
          $('addAreaBtn').style.display = 'inline-flex';
        }
      });
      button.hasListener = true;
    });

    document.querySelectorAll('[data-reset]').forEach((button) => {
      if (button.hasListener) return;
      button.addEventListener('click', () => {
        ({ item: resetItemForm, category: resetCategoryForm, table: resetTableForm, area: resetAreaForm })[button.dataset.reset]();
      });
      button.hasListener = true;
    });

    bindSafe('itemCategory', 'change', () => {
      setOptions($('itemSubCategory'), subCategories($('itemCategory').value), { placeholder: 'No subcategory' });
    });

    bindSafe('galleryFilterCategory', 'change', () => {
      setOptions($('galleryFilterSubCategory'), subCategories($('galleryFilterCategory').value), { placeholder: 'All Subcategories', includeBlank: true });
      renderGallery();
    });

    bindSafe('galleryFilterSubCategory', 'change', () => {
      renderGallery();
    });

    bindSafe('galleryUploadCategory', 'change', () => {
      setOptions($('galleryUploadSubCategory'), subCategories($('galleryUploadCategory').value), { placeholder: 'No subcategory' });
    });

    bindSafe('editGalleryImageCategory', 'change', () => {
      setOptions($('editGalleryImageSubCategory'), subCategories($('editGalleryImageCategory').value), { placeholder: 'No subcategory' });
    });
    
    bindSafe('itemSearch', 'input', renderItems);
    bindSafe('gallerySearch', 'input', renderGallery);

    bindSafe('gallerySelectFilterCategory', 'change', () => {
      renderGallerySelectGrid();
    });
    bindSafe('gallerySelectSearch', 'input', () => {
      renderGallerySelectGrid();
    });
    bindSafe('selectFromGalleryBtn', 'click', (e) => {
      e.preventDefault();
      renderGallerySelectGrid();
      openModal('gallerySelectModal');
    });
    bindSafe('gallerySelectGrid', 'click', async (e) => {
      const card = e.target.closest('.gallery-select-card');
      if (!card) return;
      const url = card.dataset.url;

      if (state.activeAssignItemId) {
        try {
          const itemId = state.activeAssignItemId;
          const item = state.items.find(i => Number(i.id) === Number(itemId));
          if (item) {
            let descObj = {};
            try {
              descObj = JSON.parse(item.description || '{}');
            } catch(ex) {}
            
            let itemImgs = [];
            if (item.image) {
              try {
                if (item.image.trim().startsWith('[')) {
                  itemImgs = JSON.parse(item.image);
                } else {
                  itemImgs = [item.image];
                }
              } catch(e) {
                itemImgs = [item.image];
              }
            }
            
            const isAlreadyAssigned = itemImgs.includes(url);
            const newImgs = isAlreadyAssigned ? [] : [url];
            
            await api('/menu-items/' + itemId, {
              method: 'PUT',
              body: JSON.stringify({
                name: item.name,
                category_id: item.category_id,
                sub_category_id: item.sub_category_id,
                price: item.price,
                image: newImgs.length ? JSON.stringify(newImgs) : null,
                description: JSON.stringify(descObj),
                is_veg: Number(item.is_veg) ? 1 : 0,
                is_available: Number(item.is_available) ? 1 : 0
              })
            });
            
            closeModal('gallerySelectModal');
            toast(isAlreadyAssigned ? 'Image unassigned successfully!' : 'Image assigned successfully!');
            state.activeAssignItemId = null;
            await loadData();
          }
        } catch (err) {
          toast(err.message);
        }
        return;
      }

      if (state.itemImages.includes(url)) {
        state.itemImages = state.itemImages.filter(img => img !== url);
      } else {
        state.itemImages.push(url);
      }
      renderUploadedImagesGrid();
      renderGallerySelectGrid();
    });

    bindSafe('filterCategory', 'change', () => {
      const catVal = $('filterCategory').value;
      updateSubCategoryFilter(catVal);
      localStorage.setItem(filterStorageKey, JSON.stringify({
        category: catVal,
        subcategory: ''
      }));
      renderItems();
    });

    bindSafe('filterSubCategory', 'change', () => {
      localStorage.setItem(filterStorageKey, JSON.stringify({
        category: $('filterCategory').value,
        subcategory: $('filterSubCategory').value
      }));
      renderItems();
    });

    bindSafe('addItemBtn', 'click', () => {
      resetItemForm();
      openModal('itemModal');
    });
    bindSafe('addCategoryBtn', 'click', () => {
      resetCategoryForm();
      openModal('categoryModal');
    });
    bindSafe('addTableBtn', 'click', () => {
      resetTableForm();
      openModal('tableModal');
    });
    bindSafe('addAreaBtn', 'click', () => {
      resetAreaForm();
      openModal('areaModal');
    });

    bindSafe('quickAddAreaBtn', 'click', (e) => {
      e.preventDefault();
      state.returningToTableForm = true;
      state.tempTableNumber = $('tableNumber').value;
      state.tempTableId = $('tableId').value;
      closeModal('tableModal');
      resetAreaForm();
      openModal('areaModal');
    });

    document.querySelectorAll('.close-modal-btn').forEach((btn) => {
      if (btn.hasListener) return;
      btn.addEventListener('click', () => {
        const modal = btn.closest('.modal-overlay');
        if (modal) {
          closeModal(modal.id);
        }
      });
      btn.hasListener = true;
    });

    document.querySelectorAll('.modal-overlay').forEach((overlay) => {
      if (overlay.hasListener) return;
      overlay.addEventListener('click', (e) => {
        if (e.target === overlay) {
          closeModal(overlay.id);
        }
      });
      overlay.hasListener = true;
    });

    // Language toggle pills in the Add Menu Item form
    document.querySelectorAll('.lang-btn').forEach((btn) => {
      if (btn.hasListener) return;
      btn.addEventListener('click', (e) => {
        e.preventDefault();
        document.querySelectorAll('.lang-btn').forEach((b) => b.classList.remove('active'));
        btn.classList.add('active');
        const lang = btn.dataset.lang;
        if (lang === 'hindi') {
          $('itemName').placeholder = 'Type in English for Hindi';
          $('translitContainer').style.display = 'flex';
          // Immediately trigger transliteration suggestions for the current input
          $('itemName').dispatchEvent(new Event('input'));
        } else {
          $('itemName').placeholder = 'Enter item name in English';
          $('translitContainer').style.display = 'none';
        }
        $('itemName').focus();
      });
      btn.hasListener = true;
    });

    // Real-time Hindi transliteration fetching
    bindSafe('itemName', 'input', async () => {
      const activeBtn = document.querySelector('.lang-btn.active');
      const isHindi = activeBtn && activeBtn.dataset.lang === 'hindi';
      if (!isHindi) return;

      const val = $('itemName').value;
      const selectionEnd = $('itemName').selectionEnd;
      const textBeforeCursor = val.substring(0, selectionEnd);
      
      const words = textBeforeCursor.split(/\s+/);
      const lastWord = words[words.length - 1];

      if (!lastWord || lastWord.trim() === '') {
        $('translitSuggestions').innerHTML = '<span class="placeholder-suggestion">Type to see suggestions...</span>';
        state.currentSuggestions = [];
        return;
      }

      const suggestions = await fetchTransliteration(lastWord);
      state.currentSuggestions = suggestions || [];
      if (state.currentSuggestions.length > 0) {
        $('translitSuggestions').innerHTML = state.currentSuggestions.map((s) => (
          `<button type="button" class="suggestion-pill" data-suggestion="${escapeHtml(s)}">${escapeHtml(s)}</button>`
        )).join('');

        // Listen to pill clicks
        $('translitSuggestions').querySelectorAll('.suggestion-pill').forEach((pill) => {
          pill.addEventListener('click', (e) => {
            e.preventDefault();
            e.stopPropagation();
            selectSuggestion(pill.dataset.suggestion);
          });
        });
      } else {
        $('translitSuggestions').innerHTML = '<span class="placeholder-suggestion">No suggestions</span>';
      }
    });

    // Space & Enter triggers for transliteration select
    bindSafe('itemName', 'keydown', (e) => {
      const activeBtn = document.querySelector('.lang-btn.active');
      const isHindi = activeBtn && activeBtn.dataset.lang === 'hindi';
      if (!isHindi) return;

      if (e.key === ' ' || e.code === 'Space') {
        if (state.currentSuggestions && state.currentSuggestions.length > 0) {
          e.preventDefault();
          selectSuggestion(state.currentSuggestions[0]);
        }
      } else if (e.key === 'Enter') {
        if (state.currentSuggestions && state.currentSuggestions.length > 0) {
          e.preventDefault();
          selectSuggestion(state.currentSuggestions[0]);
        }
      }
    });

    // Premium Image Upload event bindings (supports multiple images)
    bindSafe('imageUploadZone', 'click', (e) => {
      if (e.target && e.target.id === 'itemImageFile') return;
      e.preventDefault();
      e.stopPropagation();
      openFilePickerOnce($('itemImageFile'));
    });

    bindSafe('itemImageFile', 'change', async () => {
      state.filePickerOpen = false;
      const files = Array.from($('itemImageFile').files);
      if (!files.length) return;

      await ensureLatestGallery();

      const existingNames = (state.gallery || []).map(g => (g.filename || '').toLowerCase().trim());
      const validFiles = [];
      const duplicates = [];

      for (const file of files) {
        const name = file.name.toLowerCase().trim();
        if (existingNames.includes(name)) {
          duplicates.push(`${file.name} (Already exists in gallery)`);
        } else if (validFiles.some(f => f.name.toLowerCase().trim() === name)) {
          duplicates.push(`${file.name} (Duplicate in this batch)`);
        } else {
          validFiles.push(file);
        }
      }

      if (duplicates.length) {
        await showAlert("Upload Blocked", "The following files already exist in the gallery or are duplicates and will not be included:\n\n" + duplicates.join("\n"));
        const dt = new DataTransfer();
        validFiles.forEach(f => dt.items.add(f));
        $('itemImageFile').files = dt.files;
        if (!validFiles.length) {
          return;
        }
      }

      try {
        toast(`Uploading ${validFiles.length} images...`);
        const imageFiles = validFiles.filter(file => file.type.startsWith('image/'));
        if (!imageFiles.length) {
          toast('Please select valid image files.');
          return;
        }
        setItemUploadBusy(true);
        setUploadProgress('item', 1, `Preparing ${imageFiles.length} image${imageFiles.length > 1 ? 's' : ''}...`);
        const uploadStartedAt = Date.now();
        for (let index = 0; index < imageFiles.length; index++) {
          const file = imageFiles[index];
          if (file.type.startsWith('image/')) {
            const data = await uploadImageFile(file, (percent) => {
              const totalPercent = ((index + (percent / 100)) / imageFiles.length) * 100;
              setUploadProgress('item', totalPercent, uploadEtaLabel(`Uploading ${index + 1} of ${imageFiles.length}: ${file.name}`, totalPercent, uploadStartedAt));
            });
            state.itemImages.push(data.url);

            // Register in Gallery
            try {
              const catId = $('itemCategory').value;
              const subCatId = $('itemSubCategory').value;
              await api('/gallery', {
                method: 'POST',
                body: JSON.stringify({
                  url: data.url,
                  filename: file.name,
                  is_visible: 1,
                  category_id: catId ? Number(catId) : null,
                  sub_category_id: subCatId ? Number(subCatId) : null
                })
              });
            } catch (galleryError) {
              console.warn('Failed to register image in gallery:', galleryError);
            }
          }
        }
        finishItemUploadProgress();
        renderUploadedImagesGrid();
        toast('Images uploaded successfully.');
      } catch (error) {
        resetUploadProgress('item');
        setItemUploadBusy(false);
        toast('Some uploads failed: ' + error.message);
      }
    });

    bindSafe('imageUploadZone', 'dragover', (e) => {
      e.preventDefault();
      $('imageUploadZone').classList.add('dragover');
    });

    bindSafe('imageUploadZone', 'dragleave', () => {
      $('imageUploadZone').classList.remove('dragover');
    });

    bindSafe('imageUploadZone', 'drop', async (e) => {
      e.preventDefault();
      $('imageUploadZone').classList.remove('dragover');

      const files = Array.from(e.dataTransfer.files);
      const imageFiles = files.filter(f => f.type.startsWith('image/'));
      
      if (imageFiles.length > 0) {
        await ensureLatestGallery();
        const existingNames = (state.gallery || []).map(g => (g.filename || '').toLowerCase().trim());
      const validFiles = [];
      const duplicates = [];

      for (const file of imageFiles) {
        const name = file.name.toLowerCase().trim();
        if (existingNames.includes(name)) {
          duplicates.push(`${file.name} (Already exists in gallery)`);
        } else if (validFiles.some(f => f.name.toLowerCase().trim() === name)) {
          duplicates.push(`${file.name} (Duplicate in this batch)`);
        } else {
          validFiles.push(file);
        }
      }

      if (duplicates.length) {
        await showAlert("Upload Blocked", "The following files already exist in the gallery or are duplicates and will not be included:\n\n" + duplicates.join("\n"));
        if (!validFiles.length) {
          return;
        }
      }

        try {
          toast(`Uploading ${validFiles.length} dropped images...`);
          setItemUploadBusy(true);
          setUploadProgress('item', 1, `Preparing ${validFiles.length} image${validFiles.length > 1 ? 's' : ''}...`);
          const uploadStartedAt = Date.now();
          for (let index = 0; index < validFiles.length; index++) {
            const file = validFiles[index];
            const data = await uploadImageFile(file, (percent) => {
              const totalPercent = ((index + (percent / 100)) / validFiles.length) * 100;
              setUploadProgress('item', totalPercent, uploadEtaLabel(`Uploading ${index + 1} of ${validFiles.length}: ${file.name}`, totalPercent, uploadStartedAt));
            });
            state.itemImages.push(data.url);

            // Register in Gallery
            try {
              const catId = $('itemCategory').value;
              const subCatId = $('itemSubCategory').value;
              await api('/gallery', {
                method: 'POST',
                body: JSON.stringify({
                  url: data.url,
                  filename: file.name,
                  is_visible: 1,
                  category_id: catId ? Number(catId) : null,
                  sub_category_id: subCatId ? Number(subCatId) : null
                })
              });
            } catch (galleryError) {
              console.warn('Failed to register image in gallery:', galleryError);
            }
          }
          finishItemUploadProgress();
          renderUploadedImagesGrid();
          toast('Images uploaded successfully.');
        } catch (error) {
          resetUploadProgress('item');
          setItemUploadBusy(false);
          toast('Some uploads failed: ' + error.message);
        }
      } else {
        toast('Please drop valid image files.');
      }
    });

    // Category Image Upload event bindings
    bindSafe('categoryImageUploadZone', 'click', (e) => {
      if (e.target && e.target.id === 'categoryImageFile') return;
      if (e.target.closest('#removeCategoryImageBtn')) return;
      e.preventDefault();
      e.stopPropagation();
      openFilePickerOnce($('categoryImageFile'));
    });

    bindSafe('categoryImageFile', 'change', async () => {
      state.filePickerOpen = false;
      const file = $('categoryImageFile').files[0];
      if (!file) return;

      try {
        toast('Uploading category image...');
        setUploadProgress('category', 1, `Preparing: ${file.name}`);
        const uploadStartedAt = Date.now();
        const data = await uploadImageFile(file, (percent) => {
          setUploadProgress('category', percent, uploadEtaLabel(`Uploading: ${file.name}`, percent, uploadStartedAt));
        });
        
        $('categoryImage').value = data.url;
        $('categoryUploadPreview').src = getCleanImageUrl(data.url);
        $('categoryUploadPreviewContainer').style.display = 'block';
        $('categoryUploadPlaceholder').style.display = 'none';
        
        finishUploadProgress('category');
        toast('Category image uploaded successfully.');
      } catch (error) {
        resetUploadProgress('category');
        toast('Category image upload failed: ' + error.message);
      }
    });

    bindSafe('removeCategoryImageBtn', 'click', (e) => {
      e.preventDefault();
      e.stopPropagation();
      
      $('categoryImageFile').value = '';
      $('categoryImage').value = '';
      $('categoryUploadPreview').src = '';
      $('categoryUploadPreviewContainer').style.display = 'none';
      $('categoryUploadPlaceholder').style.display = 'flex';
      
      toast('Category image removed.');
    });

    bindSafe('categoryImageUploadZone', 'dragover', (e) => {
      e.preventDefault();
      $('categoryImageUploadZone').classList.add('dragover');
    });

    bindSafe('categoryImageUploadZone', 'dragleave', () => {
      $('categoryImageUploadZone').classList.remove('dragover');
    });

    bindSafe('categoryImageUploadZone', 'drop', async (e) => {
      e.preventDefault();
      $('categoryImageUploadZone').classList.remove('dragover');

      const file = e.dataTransfer.files[0];
      if (file && file.type.startsWith('image/')) {
        try {
          toast('Uploading category image...');
          setUploadProgress('category', 1, `Preparing: ${file.name}`);
          const uploadStartedAt = Date.now();
          const data = await uploadImageFile(file, (percent) => {
            setUploadProgress('category', percent, uploadEtaLabel(`Uploading: ${file.name}`, percent, uploadStartedAt));
          });
          
          $('categoryImage').value = data.url;
          $('categoryUploadPreview').src = getCleanImageUrl(data.url);
          $('categoryUploadPreviewContainer').style.display = 'block';
          $('categoryUploadPlaceholder').style.display = 'none';
          
          finishUploadProgress('category');
          toast('Category image uploaded successfully.');
        } catch (error) {
          resetUploadProgress('category');
          toast('Category image upload failed: ' + error.message);
        }
      } else {
        toast('Please drop a valid image file.');
      }
    });

    bindSafe('itemForm', 'submit', async (event) => {
      event.preventDefault();
      const id = $('itemId').value;
      
      const itemCodeValue = $('itemCode').value.trim();
      const catId = Number($('itemCategory').value);
      const subCatId = $('itemSubCategory').value ? Number($('itemSubCategory').value) : null;
      
      const catText = categoryName(catId);
      const subCatText = subCatId ? (state.categories.find(c => Number(c.id) === subCatId)?.name || '') : '';
      
      // Pack the description JSON
      const descObj = {
        code: itemCodeValue || null,
        subCategory: subCatText || null,
        isFavorite: $('itemFavorite').checked
      };

      const payload = {
        name: $('itemName').value.trim(),
        category_id: catId,
        sub_category_id: subCatId,
        price: Number($('itemPrice').value || 0),
        image: state.itemImages.length ? JSON.stringify(state.itemImages) : null,
        description: JSON.stringify(descObj),
        is_veg: $('itemVeg').checked ? 1 : 0,
        is_available: $('itemAvailable').checked ? 1 : 0,
        isFavorite: $('itemFavorite').checked
      };

      await api(id ? '/menu-items/' + id : '/menu-items', { method: id ? 'PUT' : 'POST', body: JSON.stringify(payload) });
      
      // Update "Last Added Item" preview card
      $('lastAddedName').textContent = payload.name;
      $('lastAddedPrice').textContent = 'Rs. ' + payload.price.toFixed(2);
      $('lastAddedCode').textContent = itemCodeValue || 'None';
      $('lastAddedContainer').style.display = 'block';

      toast('Item saved successfully.');
      await loadData();

      if (!id) {
        // If it was a NEW item, reset inputs but KEEP category & subcategory for consecutive fast entries
        $('itemName').value = '';
        $('itemPrice').value = '';
        $('itemCode').value = '';
        $('translitSuggestions').innerHTML = '<span class="placeholder-suggestion">Type to see suggestions...</span>';
        $('itemName').focus();
      } else {
        // If editing an existing item, close modal
        closeModal('itemModal');
      }
    });

    bindSafe('categoryForm', 'submit', async (event) => {
      event.preventDefault();
      const id = $('categoryId').value;
      const payload = {
        name: $('categoryName').value.trim(),
        image: $('categoryImage').value.trim() || null,
        parent_id: $('categoryParent').value ? Number($('categoryParent').value) : null,
        sort_order: Number($('categorySort').value || 0),
        is_active: $('categoryActive').checked ? 1 : 0
      };
      await api(id ? '/categories/' + id : '/categories', { method: id ? 'PUT' : 'POST', body: JSON.stringify(payload) });
      resetCategoryForm();
      await loadData();
      toast('Category saved.');
      closeModal('categoryModal');
    });

    bindSafe('tableForm', 'submit', async (event) => {
      event.preventDefault();
      const id = $('tableId').value;
      const payload = {
        table_number: $('tableNumber').value.trim(),
        area_id: $('tableArea').value ? Number($('tableArea').value) : null,
        is_active: 1
      };
      await api(id ? '/tables/' + id : '/tables', { method: id ? 'PUT' : 'POST', body: JSON.stringify(payload) });
      resetTableForm();
      await loadData();
      toast('Table saved.');
      closeModal('tableModal');
    });

    bindSafe('areaForm', 'submit', async (event) => {
      event.preventDefault();
      const id = $('areaId').value;
      const payload = {
        name: $('areaName').value.trim(),
        sort_order: Number($('areaSort').value || 0),
        is_active: $('areaActive').checked ? 1 : 0
      };
      const newArea = await api(id ? '/dining-areas/' + id : '/dining-areas', { method: id ? 'PUT' : 'POST', body: JSON.stringify(payload) });
      resetAreaForm();
      await loadData();
      toast('Dining area saved.');
      closeModal('areaModal');

      // Returning to Table Modal workflow
      if (state.returningToTableForm) {
        state.returningToTableForm = false;
        
        // Restore table form title based on whether it was add or edit
        if (state.tempTableId) {
          $('tableFormTitle').textContent = 'Edit Table';
          const submitBtn = $('tableForm').querySelector('button[type="submit"]');
          if (submitBtn) submitBtn.textContent = 'Save Changes';
        } else {
          $('tableFormTitle').textContent = 'Add New Table';
          const submitBtn = $('tableForm').querySelector('button[type="submit"]');
          if (submitBtn) submitBtn.textContent = 'Create Table';
        }
        
        $('tableId').value = state.tempTableId;
        $('tableNumber').value = state.tempTableNumber;
        
        // Find the newly created area and auto-select it
        if (newArea && newArea.id) {
          $('tableArea').value = newArea.id;
        } else if (state.areas.length > 0) {
          // Fallback to auto-select the latest area added in the array
          const latestArea = state.areas[state.areas.length - 1];
          if (latestArea) {
            $('tableArea').value = latestArea.id;
          }
        }
        $('tableArea').dispatchEvent(new Event('change', { bubbles: true }));
        openModal('tableModal');
      }
    });

    // Perform item assignment
    async function performAssignItem(itemId, url) {
      try {
        const item = state.items.find(i => Number(i.id) === Number(itemId));
        if (item) {
          let imgs = [];
          if (item.image) {
            try {
              if (item.image.trim().startsWith('[')) {
                imgs = JSON.parse(item.image);
              } else {
                imgs = [item.image];
              }
            } catch (ex) {
              imgs = [item.image];
            }
          }
          if (!imgs.includes(url)) {
            imgs.push(url);
          }
          
          let descObj = {};
          try {
            descObj = JSON.parse(item.description || '{}');
          } catch(ex) {}
          
          await api('/menu-items/' + itemId, {
            method: 'PUT',
            body: JSON.stringify({
              name: item.name,
              category_id: item.category_id,
              sub_category_id: item.sub_category_id,
              price: item.price,
              image: JSON.stringify(imgs),
              description: JSON.stringify(descObj),
              is_veg: asBool(item.is_veg) ? 1 : 0,
              is_available: asBool(item.is_available) ? 1 : 0
            })
          });
          
          // Update local state immediately so the gallery reflects the new
          // assignment without a page refresh, then sync from server.
          item.image = JSON.stringify(imgs);
          closeModal('assignImageModal');
          toast('Image assigned successfully!');
          renderGallery();
          loadData();
        }
      } catch (err) {
        toast(err.message);
      }
    }

    // Perform category assignment
    async function performAssignCategory(categoryId, url) {
      try {
        const cat = state.categories.find(c => Number(c.id) === Number(categoryId));
        if (cat) {
          await api('/categories/' + categoryId, {
            method: 'PUT',
            body: JSON.stringify({
              name: cat.name,
              parent_id: cat.parent_id,
              sort_order: cat.sort_order,
              is_active: asBool(cat.is_active) ? 1 : 0,
              image: url
            })
          });
          
          // Instant local update -> gallery shows assignment without refresh.
          cat.image = url;
          closeModal('assignImageModal');
          toast('Image assigned successfully to Category!');
          renderGallery();
          loadData();
        }
      } catch (err) {
        toast(err.message);
      }
    }

    // Reset all assignments of a gallery image
    async function performResetGalleryImage(url) {
      const confirmed = await showConfirm(
        'Reset Assignment', 
        'Are you sure you want to remove all assignments for this image? (It will reset to default)'
      );
      if (!confirmed) {
        return;
      }
      
      let updatedCount = 0;
      
      try {
        // 1. Unassign from all items
        for (const item of state.items) {
          let imgs = [];
          if (item.image) {
            try {
              if (item.image.trim().startsWith('[')) {
                imgs = JSON.parse(item.image);
              } else {
                imgs = [item.image];
              }
            } catch (ex) {
              imgs = [item.image];
            }
          }
          
          if (imgs.includes(url)) {
            const newImgs = imgs.filter(imgUrl => imgUrl !== url);
            let descObj = {};
            try {
              descObj = JSON.parse(item.description || '{}');
            } catch(ex) {}
            
            await api('/menu-items/' + item.id, {
              method: 'PUT',
              body: JSON.stringify({
                name: item.name,
                category_id: item.category_id,
                sub_category_id: item.sub_category_id,
                price: item.price,
                image: newImgs.length > 0 ? JSON.stringify(newImgs) : '',
                description: JSON.stringify(descObj),
                is_veg: asBool(item.is_veg) ? 1 : 0,
                is_available: asBool(item.is_available) ? 1 : 0
              })
            });
            updatedCount++;
          }
        }
        
        // 2. Unassign from all categories
        for (const cat of state.categories) {
          if (cat.image === url) {
            await api('/categories/' + cat.id, {
              method: 'PUT',
              body: JSON.stringify({
                name: cat.name,
                parent_id: cat.parent_id,
                sort_order: cat.sort_order,
                is_active: asBool(cat.is_active) ? 1 : 0,
                image: ''
              })
            });
            updatedCount++;
          }
        }
        
        toast('Image assignments successfully reset!');
        await loadData();
      } catch (err) {
        toast(err.message);
      }
    }

    // Perform single item unassignment
    async function performUnassignItem(itemId, url) {
      try {
        const item = state.items.find(i => Number(i.id) === Number(itemId));
        if (item) {
          let imgs = [];
          if (item.image) {
            try {
              if (item.image.trim().startsWith('[')) {
                imgs = JSON.parse(item.image);
              } else {
                imgs = [item.image];
              }
            } catch (ex) {
              imgs = [item.image];
            }
          }
          const newImgs = imgs.filter(imgUrl => imgUrl !== url);
          
          let descObj = {};
          try {
            descObj = JSON.parse(item.description || '{}');
          } catch(ex) {}
          
          await api('/menu-items/' + itemId, {
            method: 'PUT',
            body: JSON.stringify({
              name: item.name,
              category_id: item.category_id,
              sub_category_id: item.sub_category_id,
              price: item.price,
              image: newImgs.length > 0 ? JSON.stringify(newImgs) : '',
              description: JSON.stringify(descObj),
              is_veg: asBool(item.is_veg) ? 1 : 0,
              is_available: asBool(item.is_available) ? 1 : 0
            })
          });
          
          item.image = newImgs.length > 0 ? JSON.stringify(newImgs) : '';
          toast('Image unassigned (reset) successfully!');
          renderGallery();
          renderAssignList();
          loadData();
        }
      } catch (err) {
        toast(err.message);
      }
    }

    // Perform single category unassignment
    async function performUnassignCategory(categoryId, url) {
      try {
        const cat = state.categories.find(c => Number(c.id) === Number(categoryId));
        if (cat) {
          await api('/categories/' + categoryId, {
            method: 'PUT',
            body: JSON.stringify({
              name: cat.name,
              parent_id: cat.parent_id,
              sort_order: cat.sort_order,
              is_active: asBool(cat.is_active) ? 1 : 0,
              image: ''
            })
          });
          
          cat.image = '';
          toast('Category image reset successfully!');
          renderGallery();
          renderAssignList();
          loadData();
        }
      } catch (err) {
        toast(err.message);
      }
    }

    // Bind listeners for Assign Modal controls
    bindSafe('assignTabItems', 'click', () => switchAssignTab('items'));
    bindSafe('assignTabCategories', 'click', () => switchAssignTab('categories'));
    bindSafe('assignItemSearch', 'input', renderAssignList);
    bindSafe('assignFilterCategory', 'change', () => {
      const catId = $('assignFilterCategory').value;
      setOptions($('assignFilterSubCategory'), catId ? subCategories(catId) : [], { placeholder: 'All Subcategories' });
      $('assignFilterSubCategory').dispatchEvent(new Event('change', { bubbles: true }));
      renderAssignList();
    });
    bindSafe('assignFilterSubCategory', 'change', renderAssignList);

    bindSafe('editGalleryImageForm', 'submit', async (e) => {
      e.preventDefault();
      const id = $('editGalleryImageId').value;
      const url = $('editGalleryImageUrl').value;
      const name = $('editGalleryImageName').value.trim();
      const isVisible = $('editGalleryImageVisible').checked ? 1 : 0;
      const catId = $('editGalleryImageCategory').value;
      const subCatId = $('editGalleryImageSubCategory').value;
      
      try {
        await api('/gallery/' + id, {
          method: 'PUT',
          body: JSON.stringify({
            url: url,
            filename: name,
            is_visible: isVisible,
            category_id: catId || null,
            sub_category_id: subCatId || null
          })
        });
        
        closeModal('editGalleryImageModal');
        toast('Gallery image updated!');
        await loadData();
      } catch (err) {
        toast(err.message);
      }
    });

    bindSafe('galleryUploadBtn', 'click', () => {
      resetGalleryUploadForm();
      openModal('galleryUploadModal');
    });

    const dropZone = $('galleryDragDropZone');
    const uFileInput = $('galleryUploadFileInput');
    const filesList = $('galleryUploadFilesList');

    if (dropZone && uFileInput) {
      bindSafe('galleryDragDropZone', 'click', (e) => {
        if (e.target && e.target.id === 'galleryUploadFileInput') return;
        e.preventDefault();
        e.stopPropagation();
        openFilePickerOnce(uFileInput);
      });

      bindSafe('galleryUploadFileInput', 'change', async () => {
        state.filePickerOpen = false;
        const files = Array.from(uFileInput.files);
        await ensureLatestGallery();
        const existingNames = (state.gallery || []).map(g => (g.filename || '').toLowerCase().trim());
        const validFiles = [];
        const duplicates = [];

        for (const file of files) {
          const name = file.name.toLowerCase().trim();
          if (existingNames.includes(name)) {
            duplicates.push(`${file.name} (Already exists in gallery)`);
          } else if (validFiles.some(f => f.name.toLowerCase().trim() === name)) {
            duplicates.push(`${file.name} (Duplicate in this batch)`);
          } else {
            validFiles.push(file);
          }
        }

        if (duplicates.length) {
          await showAlert("Upload Blocked", "The following files already exist in the gallery or are duplicates and will not be included:\n\n" + duplicates.join("\n"));
          const dt = new DataTransfer();
          validFiles.forEach(f => dt.items.add(f));
          uFileInput.files = dt.files;
        }

        if (validFiles.length) {
          filesList.innerHTML = validFiles.map(f => `<div>📄 ${escapeHtml(f.name)} (${(f.size / 1024).toFixed(1)} KB)</div>`).join('');
          filesList.style.display = 'block';
        } else {
          filesList.innerHTML = '';
          filesList.style.display = 'none';
        }
      });

      // Drag and drop event handlers
      bindSafe('galleryDragDropZone', 'dragover', (e) => {
        e.preventDefault();
        dropZone.classList.add('dragover');
      });

      bindSafe('galleryDragDropZone', 'dragleave', () => {
        dropZone.classList.remove('dragover');
      });

      bindSafe('galleryDragDropZone', 'drop', async (e) => {
        e.preventDefault();
        dropZone.classList.remove('dragover');
        const dt = e.dataTransfer;
        if (dt && dt.files.length) {
          await ensureLatestGallery();
          const files = Array.from(dt.files);
          const existingNames = (state.gallery || []).map(g => (g.filename || '').toLowerCase().trim());
          const validFiles = [];
          const duplicates = [];

          for (const file of files) {
            const name = file.name.toLowerCase().trim();
            if (existingNames.includes(name)) {
              duplicates.push(`${file.name} (Already exists in gallery)`);
            } else if (validFiles.some(f => f.name.toLowerCase().trim() === name)) {
              duplicates.push(`${file.name} (Duplicate in this batch)`);
            } else {
              validFiles.push(file);
            }
          }

          if (duplicates.length) {
            await showAlert("Upload Blocked", "The following files already exist in the gallery or are duplicates and will not be included:\n\n" + duplicates.join("\n"));
          }

          const newDt = new DataTransfer();
          validFiles.forEach(f => newDt.items.add(f));
          uFileInput.files = newDt.files;

          if (validFiles.length) {
            filesList.innerHTML = validFiles.map(f => `<div>📄 ${escapeHtml(f.name)} (${(f.size / 1024).toFixed(1)} KB)</div>`).join('');
            filesList.style.display = 'block';
          } else {
            filesList.innerHTML = '';
            filesList.style.display = 'none';
          }
        }
      });
    }

    bindSafe('galleryUploadForm', 'submit', async (e) => {
      e.preventDefault();
      const files = Array.from($('galleryUploadFileInput').files);
      if (!files.length) {
        toast('Please select or drop at least one image file.');
        return;
      }

      await ensureLatestGallery();

      const existingNames = (state.gallery || []).map(g => (g.filename || '').toLowerCase().trim());
      const validFiles = [];
      const duplicates = [];

      for (const file of files) {
        const name = file.name.toLowerCase().trim();
        if (existingNames.includes(name)) {
          duplicates.push(`${file.name} (Already exists in gallery)`);
        } else if (validFiles.some(f => f.name.toLowerCase().trim() === name)) {
          duplicates.push(`${file.name} (Duplicate in this batch)`);
        } else {
          validFiles.push(file);
        }
      }

      if (duplicates.length) {
        await showAlert("Upload Blocked", "The following files already exist in the gallery or are duplicates and will not be uploaded:\n\n" + duplicates.join("\n"));
        if (!validFiles.length) {
          return;
        }
      }

      const catId = $('galleryUploadCategory').value;
      const subCatId = $('galleryUploadSubCategory').value;

      toast(`Uploading ${validFiles.length} images...`);
      let uploadedCount = 0;
      const imageFiles = validFiles.filter(file => file.type.startsWith('image/'));
      if (!imageFiles.length) {
        toast('Please select valid image files.');
        return;
      }
      setUploadProgress('gallery', 1, `Preparing ${imageFiles.length} image${imageFiles.length > 1 ? 's' : ''}...`);
      const uploadStartedAt = Date.now();

      for (let index = 0; index < validFiles.length; index++) {
        const file = validFiles[index];
        if (file.type.startsWith('image/')) {
          try {
            const imageIndex = imageFiles.indexOf(file);
            const uploadRes = await uploadImageFile(file, (percent) => {
              const totalPercent = ((imageIndex + (percent / 100)) / imageFiles.length) * 100;
              setUploadProgress('gallery', totalPercent, uploadEtaLabel(`Uploading ${imageIndex + 1} of ${imageFiles.length}: ${file.name}`, totalPercent, uploadStartedAt));
            });
            await api('/gallery', {
              method: 'POST',
              body: JSON.stringify({
                url: uploadRes.url,
                filename: file.name,
                is_visible: 1,
                category_id: catId || null,
                sub_category_id: subCatId || null
              })
            });
            uploadedCount++;
          } catch (err) {
            toast('Failed to upload: ' + file.name);
          }
        }
      }

      finishUploadProgress('gallery');
      closeModal('galleryUploadModal');
      toast(`Successfully uploaded ${uploadedCount} of ${validFiles.length} images!`);
      await loadData();
    });

    bindSafe('updateGalleryImageFileInput', 'change', async () => {
      state.filePickerOpen = false;
      const fileInput = $('updateGalleryImageFileInput');
      const file = fileInput.files[0];
      const id = fileInput.dataset.targetId;
      if (!file || !id) return;

      await ensureLatestGallery();

      const name = file.name.toLowerCase().trim();
      const otherImages = (state.gallery || []).filter(g => Number(g.id) !== Number(id));
      const existingNames = otherImages.map(g => (g.filename || '').toLowerCase().trim());
      if (existingNames.includes(name)) {
        await showAlert("Upload Blocked", "File '" + file.name + "' already exists in the gallery!");
        fileInput.value = '';
        return;
      }

      try {
        toast('Uploading updated image...');
        const uploadRes = await uploadImageFile(file);
        
        const img = state.gallery.find(g => Number(g.id) === Number(id));
        if (img) {
          await api('/gallery/' + id, {
            method: 'PUT',
            body: JSON.stringify({
              url: uploadRes.url,
              filename: file.name || img.filename,
              is_visible: asBool(img.is_visible) ? 1 : 0,
              category_id: img.category_id || null,
              sub_category_id: img.sub_category_id || null
            })
          });
          toast('Gallery image updated successfully!');
          await loadData();
        } else {
          toast('Gallery image not found.');
        }
      } catch (err) {
        toast('Image update failed: ' + err.message);
      }
    });

    bindSafe('directItemImageFileInput', 'change', async () => {
      state.filePickerOpen = false;
      const fileInput = $('directItemImageFileInput');
      const file = fileInput.files && fileInput.files[0];
      const itemId = fileInput.dataset.itemId || state.directUploadItemId;
      if (!file || !itemId) return;
      if (!file.type.startsWith('image/')) {
        toast('Please select a valid image file.');
        fileInput.value = '';
        return;
      }

      await ensureLatestGallery();

      const name = file.name.toLowerCase().trim();
      const existingNames = (state.gallery || []).map(g => (g.filename || '').toLowerCase().trim());
      if (existingNames.includes(name)) {
        await showAlert("Upload Blocked", "File '" + file.name + "' already exists in the gallery!");
        fileInput.value = '';
        return;
      }

      try {
        setUploadProgress('gallery', 1, `Uploading ${file.name}...`);
        const uploadStartedAt = Date.now();
        await uploadAndAssignImageToItem(itemId, file, (percent) => {
          setUploadProgress('gallery', percent, uploadEtaLabel(`Uploading ${file.name}`, percent, uploadStartedAt));
        });
        setUploadProgress('gallery', 100, 'Upload complete');
        setTimeout(() => resetUploadProgress('gallery'), 650);
        toast('Image uploaded and assigned successfully!');
        state.directUploadItemId = null;
        fileInput.dataset.itemId = '';
        fileInput.value = '';
        await loadData();
      } catch (err) {
        toast('Image upload failed: ' + err.message);
        fileInput.value = '';
      }
    });

    // Bind this delegated click handler ONCE. bindEvents() runs on every page
    // navigation/render, but document.body persists — re-adding the listener
    // each time stacked duplicates that toggled menus (the 3-dot menu) open and
    // shut on a single click, so it appeared not to open.
    if (!document.body.dataset.menuAdminClickBound) {
      document.body.dataset.menuAdminClickBound = '1';
      document.body.addEventListener('click', async (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) return;

        const assignImgWrapper = target.closest('[data-assign-image-item-id]');
        if (assignImgWrapper) {
          event.preventDefault();
          const itemId = assignImgWrapper.dataset.assignImageItemId;
          const item = state.items.find(i => Number(i.id) === Number(itemId));
          const fileInput = $('directItemImageFileInput');
          if (fileInput && !fileInput.isMock) {
            state.directUploadItemId = itemId;
            fileInput.dataset.itemId = itemId;
            fileInput.dataset.categoryId = item ? (item.category_id || '') : '';
            fileInput.dataset.subCategoryId = item ? (item.sub_category_id || '') : '';
            fileInput.value = '';
            openFilePickerOnce(fileInput);
          } else {
            state.activeAssignItemId = itemId;
            renderGallerySelectGrid();
            openModal('gallerySelectModal');
          }
          return;
        }

        // Close all gallery dropdowns if clicked outside the container
        if (!target.closest('.gallery-card-menu-container')) {
          document.querySelectorAll('.gallery-card-menu-dropdown').forEach(d => d.style.display = 'none');
        }

        // Handle dropdown item click to close it
        const dropdownItem = target.closest('.gallery-dropdown-item');
        if (dropdownItem) {
          const dropdown = dropdownItem.closest('.gallery-card-menu-dropdown');
          if (dropdown) dropdown.style.display = 'none';
        }

        // Toggle 3-dot dropdown menu in gallery
        const menuBtn = target.closest('.gallery-card-menu-btn');
        if (menuBtn) {
          event.preventDefault();
          event.stopPropagation();
          const container = menuBtn.closest('.gallery-card-menu-container');
          const dropdown = container ? container.querySelector('.gallery-card-menu-dropdown') : null;
          if (dropdown) {
            const isOpen = dropdown.style.display === 'block';
            // Close all open gallery dropdowns first
            document.querySelectorAll('.gallery-card-menu-dropdown').forEach(d => d.style.display = 'none');
            if (!isOpen) {
              dropdown.style.display = 'block';
            }
          }
          return;
        }

      const qrBtn = target.closest('.view-qr-action');
      if (qrBtn) {
        event.preventDefault();
        const qrUrl = qrBtn.dataset.qrUrl;
        const tableNumber = qrBtn.dataset.tableNumber;
        const areaName = qrBtn.dataset.areaName;
        showQrModal(qrUrl, tableNumber, areaName);
        return;
      }

      if (target.dataset.editItem) editItem(target.dataset.editItem);
      if (target.dataset.editCategory) editCategory(target.dataset.editCategory);
      if (target.dataset.addSubcategory) {
        const parentId = target.dataset.addSubcategory;
        resetCategoryForm();
        $('categoryParent').value = parentId;
        $('categoryParent').dispatchEvent(new Event('change', { bubbles: true }));
        const parentName = categoryName(parentId);
        $('categoryFormTitle').textContent = `Add Subcategory under "${parentName}"`;
        openModal('categoryModal');
      }
      if (target.dataset.editTable) editTable(target.dataset.editTable);
      if (target.dataset.editArea) editArea(target.dataset.editArea);
      
      if (target.closest('.gallery-reset-all-btn')) {
        event.preventDefault();
        const btn = target.closest('.gallery-reset-all-btn');
        const url = btn.dataset.resetGalleryUrl;
        await performResetGalleryImage(url);
        return;
      }

      if (target.closest('.gallery-item-unassign-btn')) {
        event.preventDefault();
        const btn = target.closest('.gallery-item-unassign-btn');
        const itemId = btn.dataset.unassignItemId;
        const url = $('assignImageUrl').value;
        await performUnassignItem(itemId, url);
        return;
      }

      if (target.closest('.gallery-category-unassign-btn')) {
        event.preventDefault();
        const btn = target.closest('.gallery-category-unassign-btn');
        const categoryId = btn.dataset.unassignCategoryId;
        const url = $('assignImageUrl').value;
        await performUnassignCategory(categoryId, url);
        return;
      }

      if (target.dataset.assignGalleryUrl) {
        event.preventDefault();
        openAssignModal(target.dataset.assignGalleryUrl);
        return;
      }

      if (target.dataset.assignItemActionId) {
        event.preventDefault();
        const itemId = target.dataset.assignItemActionId;
        const url = $('assignImageUrl').value;
        await performAssignItem(itemId, url);
        return;
      }

      if (target.dataset.assignCategoryActionId) {
        event.preventDefault();
        const categoryId = target.dataset.assignCategoryActionId;
        const url = $('assignImageUrl').value;
        await performAssignCategory(categoryId, url);
        return;
      }

      if (target.dataset.toggleGalleryId) {
        event.preventDefault();
        const id = target.dataset.toggleGalleryId;
        const visible = Number(target.dataset.toggleGalleryStatus);
        const img = state.gallery.find(g => Number(g.id) === Number(id));
        if (img) {
          try {
            await api('/gallery/' + id, {
              method: 'PUT',
              body: JSON.stringify({
                url: img.url,
                filename: img.filename,
                is_visible: visible
              })
            });
            toast(visible ? 'Image shown in gallery!' : 'Image hidden from gallery!');
            await loadData();
          } catch (err) {
            toast(err.message);
          }
        }
        return;
      }

      if (target.dataset.editGalleryId) {
        event.preventDefault();
        const id = target.dataset.editGalleryId;
        const name = target.dataset.editGalleryName;
        const visible = target.dataset.editGalleryVisible;
        const url = target.dataset.editGalleryUrl;
        const cat = target.dataset.editGalleryCategory;
        const subcat = target.dataset.editGallerySubcategory;
        openEditGalleryModal(id, name, visible, url, cat, subcat);
        return;
      }

      if (target.dataset.updateGalleryImageId) {
        event.preventDefault();
        const id = target.dataset.updateGalleryImageId;
        const fileInput = $('updateGalleryImageFileInput');
        if (fileInput) {
          fileInput.dataset.targetId = id;
          fileInput.value = '';
          openFilePickerOnce(fileInput);
        }
        return;
      }

      if (target.dataset.removeMobileDownloadIndex !== undefined) {
        event.preventDefault();
        const index = Number(target.dataset.removeMobileDownloadIndex);
        if (!Number.isNaN(index)) {
          state.mobileDownloadImages.splice(index, 1);
          renderMobileDownloadImages();
          try {
            await saveMobileDownloadImages();
            refreshMobilePreviewFrame();
            toast('Mobile download image removed.');
          } catch (err) {
            toast('Failed to save mobile download images: ' + err.message);
          }
        }
        return;
      }

      // Click on any clickable image to open lightbox
      const clickableImg = target.closest('.gallery-image-clickable, .menu-item-image-clickable, #editGalleryImagePreview, #assignImagePreview, .image-preview-card img, #categoryUploadPreview');
      if (clickableImg) {
        event.preventDefault();
        const fullUrl = clickableImg.dataset.fullUrl || clickableImg.src;
        const filename = clickableImg.dataset.filename || 'Image Preview';
        
        const lightbox = $('lightboxModal');
        const lightboxImg = $('lightboxImage');
        const lightboxTitle = $('lightboxTitle');
        if (lightbox && lightboxImg) {
          lightboxImg.src = getCleanImageUrl(fullUrl);
          if (lightboxTitle) {
            lightboxTitle.textContent = filename;
          }
          openModal('lightboxModal');
        }
        return;
      }

      try {
        if (target.dataset.deleteItem) await remove('/menu-items/' + target.dataset.deleteItem, 'Delete this menu item?');
        if (target.dataset.deleteCategory) await remove('/categories/' + target.dataset.deleteCategory, 'Delete this category?');
        if (target.dataset.deleteTable) await remove('/tables/' + target.dataset.deleteTable, 'Delete this table?');
        if (target.dataset.deleteArea) await remove('/dining-areas/' + target.dataset.deleteArea, 'Delete this dining area?');
        if (target.dataset.deleteGalleryId) await remove('/gallery/' + target.dataset.deleteGalleryId, 'Delete this gallery image permanently?');
      } catch (error) {
        toast(error.message);
      }
    });
    } // end one-time document.body click binding

    // Reports Event Listeners
    if ($('reportDate')) {
      $('reportDate').addEventListener('change', loadReportsData);
    }
    initReportDatePicker();
    if ($('reportRangeType')) {
      $('reportRangeType').addEventListener('change', loadReportsData);
    }
    if ($('billSearchInput')) {
      $('billSearchInput').addEventListener('input', (e) => {
        state.billSearchQuery = e.target.value;
        state.billCurrentPage = 1;
        renderReports();
      });
    }

    document.querySelectorAll('[data-highlow-tab]').forEach(btn => {
      btn.addEventListener('click', (e) => {
        e.preventDefault();
        document.querySelectorAll('[data-highlow-tab]').forEach(b => b.classList.remove('active'));
        btn.classList.add('active');
        state.highLowTab = btn.dataset.highlowTab;
        calculateHighLow();
      });
    });

    document.querySelectorAll('[data-fest-year]').forEach(btn => {
      btn.addEventListener('click', (e) => {
        e.preventDefault();
        document.querySelectorAll('[data-fest-year]').forEach(b => b.classList.remove('active'));
        btn.classList.add('active');
        state.selectedFestivalYear = parseInt(btn.dataset.festYear);
        state.festivalCurrentPage = 1; // year changed -> back to first page
        renderReports();
      });
    });

    if ($('detailsTabBill')) {
      $('detailsTabBill').addEventListener('click', async (e) => {
        e.preventDefault();
        setActiveDetailsTab('bill');
        await loadReportsData('bills');
      });
    }
    if ($('detailsTabSales')) {
      $('detailsTabSales').addEventListener('click', async (e) => {
        e.preventDefault();
        setActiveDetailsTab('sales');
        await loadReportsData('timeline');
      });
    }
    if ($('detailsTabItems')) {
      $('detailsTabItems').addEventListener('click', async (e) => {
        e.preventDefault();
        setActiveDetailsTab('items');
        await loadReportsData('items');
      });
    }

    document.querySelectorAll('[data-timeline-range]').forEach(btn => {
      btn.addEventListener('click', (e) => {
        e.preventDefault();
        document.querySelectorAll('[data-timeline-range]').forEach(b => b.classList.remove('active'));
        btn.classList.add('active');
        state.timelineRange = btn.dataset.timelineRange;
        renderReports();
      });
    });

    document.querySelectorAll('[data-items-range]').forEach(btn => {
      btn.addEventListener('click', (e) => {
        e.preventDefault();
        document.querySelectorAll('[data-items-range]').forEach(b => b.classList.remove('active'));
        btn.classList.add('active');
        state.itemsRange = btn.dataset.itemsRange;
        state.itemsCurrentPage = 1;
        // REFETCH the items section with this range (for the selected date).
        // Previously this only re-rendered the already-loaded list, so the
        // DAILY/WEEKLY/MONTHLY pills never actually changed the data.
        loadReportsData('items');
      });
    });

    if ($('reportItemCategoryContainer')) {
      $('reportItemCategoryContainer').addEventListener('click', (e) => {
        const btn = e.target.closest('[data-filter-cat]');
        if (!btn) return;
        e.preventDefault();
        state.selectedReportCategory = btn.dataset.filterCat;
        // Category changed -> the subcategory list is different now, so any
        // previously selected subcategory no longer applies. Reset to All.
        state.selectedReportSubCategory = 'All Subcategories';
        state.itemsCurrentPage = 1;
        renderReportItemsTab();
      });
    }

    if ($('reportItemSubCategoryContainer')) {
      $('reportItemSubCategoryContainer').addEventListener('click', (e) => {
        const btn = e.target.closest('[data-filter-subcat]');
        if (!btn) return;
        e.preventDefault();
        state.selectedReportSubCategory = btn.dataset.filterSubcat;
        state.itemsCurrentPage = 1;
        renderReportItemsTab();
      });
    }
  }

  function getTodayDateString() {
    const today = new Date();
    const y = today.getFullYear();
    const m = String(today.getMonth() + 1).padStart(2, '0');
    const d = String(today.getDate()).padStart(2, '0');
    return `${y}-${m}-${d}`;
  }

  // ---------------------------------------------------------------------
  // Custom dark-theme date picker for the Reports date filter. Replaces the
  // native <input type="date"> popup, which is rendered by the OS/browser
  // and can't be restyled with CSS. The original input stays in the DOM as
  // type="hidden" (id="reportDate") so all existing code (loadReportsData's
  // 'change' listener, every `$('reportDate').value` read) keeps working
  // unchanged -- this picker just drives that hidden input's value.
  // ---------------------------------------------------------------------
  const WEEKDAY_LABELS = ['Su', 'Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa'];
  const MONTH_LABELS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

  function formatDateDDMMYYYY(dateStr) {
    const [y, m, d] = dateStr.split('-');
    return `${d}-${m}-${y}`;
  }

  function toDateString(year, month, day) {
    const m = String(month + 1).padStart(2, '0');
    const d = String(day).padStart(2, '0');
    return `${year}-${m}-${d}`;
  }

  function initReportDatePicker() {
    const trigger = $('reportDateTrigger');
    const calendarEl = $('reportDateCalendar');
    const hiddenInput = $('reportDate');
    if (!trigger || !calendarEl || !hiddenInput) return;

    if (!hiddenInput.value) {
      hiddenInput.value = getTodayDateString();
    }

    const [selY, selM] = hiddenInput.value.split('-').map(Number);
    if (!state.reportCalendarViewYear) {
      state.reportCalendarViewYear = selY;
      state.reportCalendarViewMonth = selM - 1; // 0-indexed
    }

    const updateTriggerLabel = () => {
      const selectedRange = $('reportRangeType')?.value || 'day';
      const dateLabel = $('reportDateLabel');
      if (selectedRange === 'month') {
        if (dateLabel) dateLabel.textContent = 'Month';
        const [y, m, d] = hiddenInput.value.split('-');
        const monthName = MONTH_LABELS[Number(m) - 1];
        $('reportDateTriggerLabel').textContent = `${monthName} ${y}`;
      } else {
        if (dateLabel) dateLabel.textContent = 'Date';
        $('reportDateTriggerLabel').textContent = formatDateDDMMYYYY(hiddenInput.value);
      }
    };
    updateTriggerLabel();

    const setSelectedDate = (dateStr) => {
      hiddenInput.value = dateStr;
      updateTriggerLabel();
      calendarEl.style.display = 'none';
      hiddenInput.dispatchEvent(new Event('change', { bubbles: true }));
    };

    const renderCalendar = () => {
      const viewYear = state.reportCalendarViewYear;
      const viewMonth = state.reportCalendarViewMonth;
      const selectedDateStr = hiddenInput.value;
      const selectedRange = $('reportRangeType')?.value || 'day';

      if (selectedRange === 'month') {
        let monthCells = '';
        const [selY, selM] = selectedDateStr.split('-').map(Number);
        for (let i = 0; i < 12; i++) {
          const isSelected = (i === selM - 1 && viewYear === selY);
          const dStr = toDateString(viewYear, i, 1);
          monthCells += `<button type="button" class="rdc-day${isSelected ? ' rdc-selected' : ''}" data-rdc-month-date="${dStr}" style="padding: 10px 4px; font-size: 11px; font-weight: 700; border-radius: var(--radius-sm); text-align: center;">${MONTH_LABELS[i].substring(0, 3)}</button>`;
        }

        calendarEl.innerHTML = `
          <div class="rdc-header">
            <button type="button" class="rdc-nav-btn" data-rdc-nav="prev">&#8592;</button>
            <span class="rdc-month-label">${viewYear}</span>
            <button type="button" class="rdc-nav-btn" data-rdc-nav="next">&#8594;</button>
          </div>
          <div class="rdc-days" style="grid-template-columns: repeat(3, 1fr) !important; gap: 8px; padding: 10px 4px;">
            ${monthCells}
          </div>
          <div class="rdc-footer">
            <button type="button" class="rdc-footer-btn" data-rdc-action="today" style="width: 100%;">This Month</button>
          </div>
        `;
        return;
      }

      const firstOfMonth = new Date(viewYear, viewMonth, 1);
      const gridStartOffset = firstOfMonth.getDay(); // 0=Sun
      const daysInMonth = new Date(viewYear, viewMonth + 1, 0).getDate();
      const daysInPrevMonth = new Date(viewYear, viewMonth, 0).getDate();

      let dayCells = '';
      // Leading days from previous month
      for (let i = gridStartOffset - 1; i >= 0; i--) {
        const day = daysInPrevMonth - i;
        const prevMonth = viewMonth === 0 ? 11 : viewMonth - 1;
        const prevYear = viewMonth === 0 ? viewYear - 1 : viewYear;
        const dStr = toDateString(prevYear, prevMonth, day);
        dayCells += `<button type="button" class="rdc-day rdc-other-month" data-rdc-date="${dStr}">${day}</button>`;
      }
      // Current month days
      for (let day = 1; day <= daysInMonth; day++) {
        const dStr = toDateString(viewYear, viewMonth, day);
        const isSelected = dStr === selectedDateStr;
        dayCells += `<button type="button" class="rdc-day${isSelected ? ' rdc-selected' : ''}" data-rdc-date="${dStr}">${day}</button>`;
      }
      // Trailing days from next month (fill to a multiple of 7)
      const totalCells = gridStartOffset + daysInMonth;
      const trailing = (7 - (totalCells % 7)) % 7;
      for (let day = 1; day <= trailing; day++) {
        const nextMonth = viewMonth === 11 ? 0 : viewMonth + 1;
        const nextYear = viewMonth === 11 ? viewYear + 1 : viewYear;
        const dStr = toDateString(nextYear, nextMonth, day);
        dayCells += `<button type="button" class="rdc-day rdc-other-month" data-rdc-date="${dStr}">${day}</button>`;
      }

      calendarEl.innerHTML = `
        <div class="rdc-header">
          <button type="button" class="rdc-nav-btn" data-rdc-nav="prev">&#8592;</button>
          <span class="rdc-month-label">${MONTH_LABELS[viewMonth]}, ${viewYear}</span>
          <button type="button" class="rdc-nav-btn" data-rdc-nav="next">&#8594;</button>
        </div>
        <div class="rdc-weekdays">${WEEKDAY_LABELS.map(w => `<span>${w}</span>`).join('')}</div>
        <div class="rdc-days">${dayCells}</div>
        <div class="rdc-footer">
          <button type="button" class="rdc-footer-btn" data-rdc-action="clear">Clear</button>
          <button type="button" class="rdc-footer-btn" data-rdc-action="today">Today</button>
        </div>
      `;
    };

    // Guard: bindEvents() re-runs on every tab render, but if these specific
    // elements persist (not recreated), avoid stacking duplicate listeners.
    if (!trigger.dataset.rdcBound) {
      trigger.dataset.rdcBound = '1';

      trigger.addEventListener('click', (e) => {
        e.stopPropagation();
        const isOpen = calendarEl.style.display !== 'none';
        if (isOpen) {
          calendarEl.style.display = 'none';
          return;
        }
        const [y, m] = hiddenInput.value.split('-').map(Number);
        state.reportCalendarViewYear = y;
        state.reportCalendarViewMonth = m - 1;
        renderCalendar();
        calendarEl.style.display = 'block';
      });

      calendarEl.addEventListener('click', (e) => {
        const selectedRange = $('reportRangeType')?.value || 'day';

        // Check if month cell clicked
        const monthCell = e.target.closest('[data-rdc-month-date]');
        if (monthCell) {
          setSelectedDate(monthCell.dataset.rdcMonthDate);
          return;
        }

        const dayBtn = e.target.closest('[data-rdc-date]');
        if (dayBtn) {
          setSelectedDate(dayBtn.dataset.rdcDate);
          return;
        }
        const navBtn = e.target.closest('[data-rdc-nav]');
        if (navBtn) {
          if (selectedRange === 'month') {
            if (navBtn.dataset.rdcNav === 'prev') {
              state.reportCalendarViewYear -= 1;
            } else {
              state.reportCalendarViewYear += 1;
            }
            renderCalendar();
            return;
          }
          if (navBtn.dataset.rdcNav === 'prev') {
            state.reportCalendarViewMonth -= 1;
            if (state.reportCalendarViewMonth < 0) {
              state.reportCalendarViewMonth = 11;
              state.reportCalendarViewYear -= 1;
            }
          } else {
            state.reportCalendarViewMonth += 1;
            if (state.reportCalendarViewMonth > 11) {
              state.reportCalendarViewMonth = 0;
              state.reportCalendarViewYear += 1;
            }
          }
          renderCalendar();
          return;
        }
        const actionBtn = e.target.closest('[data-rdc-action]');
        if (actionBtn) {
          // Both Clear and Today reset to today's date -- reports always need
          // a selected date, so there is no "empty" state to clear to.
          const today = getTodayDateString();
          const [y, m] = today.split('-').map(Number);
          state.reportCalendarViewYear = y;
          state.reportCalendarViewMonth = m - 1;
          setSelectedDate(today);
        }
      });

      document.addEventListener('click', (e) => {
        if (calendarEl.style.display === 'none') return;
        if (!calendarEl.contains(e.target) && e.target !== trigger && !trigger.contains(e.target)) {
          calendarEl.style.display = 'none';
        }
      });
    }
  }

  function getDateRanges(dateStr) {
    const dt = new Date(dateStr + 'T00:00:00');
    
    const formatYMD = (d) => {
      const y = d.getFullYear();
      const m = String(d.getMonth() + 1).padStart(2, '0');
      const r = String(d.getDate()).padStart(2, '0');
      return `${y}-${m}-${r}`;
    };

    const todayStr = dateStr;
    
    const prevDay = new Date(dt);
    prevDay.setDate(prevDay.getDate() - 1);
    const prevDayStr = formatYMD(prevDay);

    const currentDayOfWeek = dt.getDay(); 
    const distanceToMonday = currentDayOfWeek === 0 ? -6 : 1 - currentDayOfWeek;
    
    const mondayOfThisWeek = new Date(dt);
    mondayOfThisWeek.setDate(mondayOfThisWeek.getDate() + distanceToMonday);
    
    const sundayOfThisWeek = new Date(mondayOfThisWeek);
    sundayOfThisWeek.setDate(sundayOfThisWeek.getDate() + 6);
    
    const thisWeekStart = formatYMD(mondayOfThisWeek);
    const thisWeekEnd = formatYMD(sundayOfThisWeek);

    const mondayOfPrevWeek = new Date(mondayOfThisWeek);
    mondayOfPrevWeek.setDate(mondayOfPrevWeek.getDate() - 7);
    const sundayOfPrevWeek = new Date(mondayOfPrevWeek);
    sundayOfPrevWeek.setDate(sundayOfPrevWeek.getDate() + 6);
    
    const prevWeekStart = formatYMD(mondayOfPrevWeek);
    const prevWeekEnd = formatYMD(sundayOfPrevWeek);

    const firstDayOfThisMonth = new Date(dt.getFullYear(), dt.getMonth(), 1);
    const lastDayOfThisMonth = new Date(dt.getFullYear(), dt.getMonth() + 1, 0);
    
    const thisMonthStart = formatYMD(firstDayOfThisMonth);
    const thisMonthEnd = formatYMD(lastDayOfThisMonth);

    const firstDayOfPrevMonth = new Date(dt.getFullYear(), dt.getMonth() - 1, 1);
    const lastDayOfPrevMonth = new Date(dt.getFullYear(), dt.getMonth(), 0);
    
    const prevMonthStart = formatYMD(firstDayOfPrevMonth);
    const prevMonthEnd = formatYMD(lastDayOfPrevMonth);

    return {
      today: todayStr,
      prevDay: prevDayStr,
      thisWeekStart,
      thisWeekEnd,
      prevWeekStart,
      prevWeekEnd,
      thisMonthStart,
      thisMonthEnd,
      prevMonthStart,
      prevMonthEnd
    };
  }

  function getDatesInRange(startStr, endStr) {
    const dates = [];
    const start = new Date(startStr + 'T00:00:00');
    const end = new Date(endStr + 'T00:00:00');
    const current = new Date(start);
    while (current <= end) {
      const y = current.getFullYear();
      const m = String(current.getMonth() + 1).padStart(2, '0');
      const d = String(current.getDate()).padStart(2, '0');
      dates.push(`${y}-${m}-${d}`);
      current.setDate(current.getDate() + 1);
    }
    return dates;
  }

  function formatPrice(val) {
    return 'Rs. ' + Math.round(Number(val) || 0).toLocaleString('en-IN');
  }

  function formatDateShort(dateStr) {
    if (!dateStr) return '';
    const parts = dateStr.split('-');
    if (parts.length < 3) return dateStr;
    return `${parseInt(parts[2])}-${parseInt(parts[1])}-${parts[0]}`;
  }

  function filterOrdersByDateAndRange(orders, dateStr, range) {
    const ranges = getDateRanges(dateStr);
    if (range === 'week') {
      const start = new Date(ranges.thisWeekStart + 'T00:00:00').getTime();
      const end = new Date(ranges.thisWeekEnd + 'T23:59:59').getTime();
      return orders.filter(o => {
        if (!o.billed_at) return false;
        const t = new Date(o.billed_at.replace(' ', 'T')).getTime();
        return t >= start && t <= end;
      });
    } else if (range === 'month') {
      const start = new Date(ranges.thisMonthStart + 'T00:00:00').getTime();
      const end = new Date(ranges.thisMonthEnd + 'T23:59:59').getTime();
      return orders.filter(o => {
        if (!o.billed_at) return false;
        const t = new Date(o.billed_at.replace(' ', 'T')).getTime();
        return t >= start && t <= end;
      });
    } else {
      return orders.filter(o => o.billed_at && o.billed_at.startsWith(dateStr));
    }
  }

  // Which response keys each report section actually computes server-side
  // (ReportController). A section's response contains ALL keys, but the ones
  // it doesn't own come back null/empty -- applying only the owned keys keeps
  // parallel section loads from clobbering each other's data.
  const REPORT_SECTION_KEYS = {
    core: ['kpis', 'monthly_sales', 'high_low'],
    festivals: ['festival_dates', 'festival_sales'],
    items: ['all_sold_items'],
    bills: ['range_orders'],
    timeline: ['timeline_data']
  };

  function applyReportSection(sec, data) {
    if (!state.reportsData) state.reportsData = {};
    const ownedKeys = REPORT_SECTION_KEYS[sec];
    if (ownedKeys) {
      ownedKeys.forEach((key) => {
        if (data[key] !== undefined && data[key] !== null) {
          state.reportsData[key] = data[key];
        }
      });
      if (data.last_order_id !== undefined && data.last_order_id !== null) {
        state.reportsData.last_order_id = data.last_order_id;
      }
      return;
    }
    // Unknown section (e.g. 'all'): apply every non-null key.
    for (const key in data) {
      if (data[key] !== null && data[key] !== undefined) {
        state.reportsData[key] = data[key];
      }
    }
  }

  async function loadReportsData(section = null, force = false) {
    let targetSection = null;
    if (typeof section === 'string' && ['core', 'festivals', 'items', 'bills', 'timeline'].includes(section)) {
      targetSection = section;
    }

    const dateInput = $('reportDate');
    const selectedDate = dateInput && dateInput.value ? dateInput.value : getTodayDateString();
    const rangeSelect = $('reportRangeType');
    const selectedRange = rangeSelect && rangeSelect.value ? rangeSelect.value : 'day';

    if (!state.reportsData) {
      state.reportsData = {};
    }

    // The report Items tab builds its category/subcategory chips from the
    // MASTER category list -- make sure it is loaded even when the admin was
    // opened directly on the Reports tab (cache first, API fallback).
    if (!Array.isArray(state.categories) || !state.categories.length) {
      const cachedCats = getCachedData('categories');
      if (cachedCats && cachedCats.length) {
        state.categories = cachedCats;
      } else {
        try {
          const cats = await api('/categories');
          state.categories = cats || [];
          setCachedData('categories', state.categories);
        } catch { /* chips fall back to sold-item names */ }
      }
    }

    try {
      if (!targetSection) {
        // Reset category/subcategory filters on client/date/range change to prevent filtering out new data
        state.selectedReportCategory = 'All Categories';
        state.selectedReportSubCategory = 'All Subcategories';

        // Full reload: sync the Most-Selling-Item panel's own DAILY/WEEKLY/
        // MONTHLY pills with the main "Filter By" range, so the panel shows
        // the same period the rest of the report shows.
        state.itemsRange = selectedRange;
        document.querySelectorAll('[data-items-range]').forEach(b => {
          b.classList.toggle('active', b.dataset.itemsRange === selectedRange);
        });

        const sections = ['core', 'festivals', 'items', 'bills', 'timeline'];

        // PROGRESSIVE LOAD: apply and render each section AS IT ARRIVES so the
        // KPI cards paint in ~1s instead of the whole page waiting for the
        // slowest of five requests (why client-switch felt slow). Each section
        // writes only ITS OWN keys, so sections can't clobber each other, and
        // a sequence guard discards responses from a superseded load (rapid
        // client/date switching).
        const loadSeq = (state.reportsLoadSeq = (state.reportsLoadSeq || 0) + 1);
        state.reportsData = {};

        const promises = sections.map(async (sec) => {
          const data = await api(`/reports/summary?date=${selectedDate}&range=${selectedRange}&report_client=${state.client}&section=${sec}&last_order_id=0`);
          if (loadSeq !== state.reportsLoadSeq) return; // superseded -> discard
          if (data) {
            applyReportSection(sec, data);
            renderReports();
          }
        });
        // allSettled (not all): if one section request fails, the others still
        // apply and the report still renders, instead of aborting the whole load.
        await Promise.allSettled(promises);
        if (loadSeq !== state.reportsLoadSeq) return;
        renderReports();
      } else {
        // The items section honours its panel's own range pills; every other
        // section follows the main "Filter By" range.
        const sectionRange = targetSection === 'items'
          ? (state.itemsRange || selectedRange)
          : selectedRange;
        const data = await api(`/reports/summary?date=${selectedDate}&range=${sectionRange}&report_client=${state.client}&section=${targetSection}&last_order_id=0`);
        if (data) {
          applyReportSection(targetSection, data);
          renderReports();
        }
      }

      if (!targetSection || targetSection === 'core') {
        state.billSearchQuery = '';
        state.billCurrentPage = 1;
        state.itemsCurrentPage = 1;
        if ($('billSearchInput')) {
          $('billSearchInput').value = '';
        }
      }
    } catch (err) {
      toast(err.message);
    }
  }

  function renderReports() {
    if (!document.querySelector('.reports-kpi-grid')) return;
    if (!state.reportsData) return;

    const dateInput = $('reportDate');
    if (dateInput) {
      if (!dateInput.value) {
        dateInput.value = getTodayDateString();
      }
      dateInput.max = getTodayDateString();
      const selectedRange = $('reportRangeType')?.value || 'day';
      const dateLabel = $('reportDateLabel');
      if (selectedRange === 'month') {
        if (dateLabel) dateLabel.textContent = 'Month';
        const [y, m, d] = dateInput.value.split('-');
        const monthName = MONTH_LABELS[Number(m) - 1];
        if ($('reportDateTriggerLabel')) {
          $('reportDateTriggerLabel').textContent = `${monthName} ${y}`;
        }
      } else {
        if (dateLabel) dateLabel.textContent = 'Date';
        if ($('reportDateTriggerLabel')) {
          $('reportDateTriggerLabel').textContent = formatDateDDMMYYYY(dateInput.value);
        }
      }
    }

    const dateStr = $('reportDate').value;

    // Render KPI Grid
    const kpiGrid = document.querySelector('.reports-kpi-grid');
    // Only render KPIs when we actually have the populated object. Non-core
    // report sections return `kpis: []` (empty array) which is truthy but has
    // no fields -> rendering it produced "Rs. NaN" / "undefined". Skip those so
    // the cards keep their last good values until the core data arrives.
    const rawKpis = state.reportsData.kpis;
    const k = (rawKpis && typeof rawKpis === 'object' && !Array.isArray(rawKpis)) ? rawKpis : null;
    if (kpiGrid && k) {
      const num = (v) => Number(v) || 0;
      const gst = (v) => `GST Value Rs. ${Math.round(num(v) * 0.05)}`;
      const kpis = [
        { title: 'Today Sale', value: num(k.today_sale), desc: gst(k.today_sale) },
        { title: 'Prev. Day Sale', value: num(k.prev_day_sale), desc: gst(k.prev_day_sale) },
        { title: 'Current Week Sale', value: num(k.this_week_sale), desc: k.this_week_range || '' },
        { title: 'Prev. Week Sale', value: num(k.prev_week_sale), desc: k.prev_week_range || '' },
        { title: 'Current Month Sale', value: num(k.this_month_sale), desc: gst(k.this_month_sale) },
        { title: 'Prev. Month Sale', value: num(k.prev_month_sale), desc: gst(k.prev_month_sale) },
        { title: 'Average/Person Spend', value: num(k.avg_spend), desc: gst(k.avg_spend) },
        { title: 'Discount', value: num(k.discount), desc: gst(k.discount) }
      ];

      kpiGrid.innerHTML = kpis.map(kpi => {
        let cardStyle = 'background: rgba(22, 28, 45, 0.35); border: 1px solid rgba(255,255,255,0.05);';
        let valColor = 'color: #fff;';
        if (kpi.title === 'Discount' && kpi.value > 0) {
          cardStyle = 'background: rgba(239, 68, 68, 0.04); border: 1px solid rgba(239, 68, 68, 0.15);';
          valColor = 'color: #ef4444;';
        } else if (kpi.title === 'Average/Person Spend') {
          cardStyle = 'background: rgba(99, 102, 241, 0.04); border: 1px solid rgba(99, 102, 241, 0.15);';
          valColor = 'color: var(--brand-light);';
        }
        return `
          <div class="panel" style="${cardStyle} padding: 18px; border-radius: var(--radius-md); display: flex; flex-direction: column; justify-content: space-between; min-height: 110px;">
            <div>
              <span style="font-size: 11px; font-weight: 700; color: var(--muted); text-transform: uppercase; letter-spacing: 0.5px;">${kpi.title}</span>
              <div style="font-size: 24px; font-weight: 800; ${valColor} margin-top: 6px; font-family: 'Outfit', sans-serif;">${formatPrice(kpi.value)}</div>
            </div>
            <span style="font-size: 11px; color: var(--muted); opacity: 0.8; margin-top: 6px;">${kpi.desc}</span>
          </div>
        `;
      }).join('');
    }

    // Render Growth Wave Chart
    const monthlySales = state.reportsData.monthly_sales || Array(12).fill(0);
    const monthNames = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    const peak = Math.max(...monthlySales, 1);

    const chartContainer = $('growthWaveChartContainer');
    if (chartContainer) {
      chartContainer.innerHTML = monthlySales.map((sales, monthIdx) => {
        const pct = (sales / peak) * 100;
        const tooltipHtml = `<span style="font-weight:700;">${monthNames[monthIdx].toUpperCase()} SALES</span><br>${formatPrice(sales)}<br><span style="color:#10b981; font-weight:700;">${Math.round(pct)}% of peak</span>`;
        const isCurrentMonth = (new Date(dateStr + 'T00:00:00').getMonth() === monthIdx);
        const barBg = isCurrentMonth 
          ? 'linear-gradient(180deg, var(--brand) 0%, var(--brand-dark) 100%)' 
          : 'rgba(255, 255, 255, 0.08)';
        
        return `
          <div class="chart-bar-wrapper" style="flex: 1; height: 100%; display: flex; flex-direction: column; align-items: center; justify-content: flex-end; position: relative; margin: 0 4px; cursor: pointer;">
            <div class="chart-tooltip" style="position: absolute; bottom: calc(${pct}% + 12px); background: #0b0f19; border: 1px solid rgba(255,255,255,0.1); border-radius: var(--radius-sm); padding: 8px 12px; font-size: 10px; color: #fff; text-align: center; pointer-events: none; opacity: 0; transition: opacity 0.2s; white-space: nowrap; z-index: 10; box-shadow: 0 4px 12px rgba(0,0,0,0.5);">
              ${tooltipHtml}
            </div>
            <div class="chart-bar" style="width: 70%; max-width: 32px; height: ${Math.max(pct, 4)}%; background: ${barBg}; border-radius: 4px 4px 0 0; transition: background 0.2s, transform 0.2s;"></div>
          </div>
        `;
      }).join('');
    }

    const chartLabels = $('growthWaveLabels');
    if (chartLabels) {
      chartLabels.innerHTML = monthNames.map(name => `
        <div style="flex: 1; text-align: center; text-transform: uppercase;">${name}</div>
      `).join('');
    }

    // Render High & Low Sales
    calculateHighLow();

    // Render Monthly Sales Breakdown
    const breakdownBody = $('monthlyBreakdownTableBody');
    if (breakdownBody) {
      const currentMonthIdx = new Date(dateStr + 'T00:00:00').getMonth();
      const currentYear = new Date(dateStr + 'T00:00:00').getFullYear();
      
      let html = '';
      const today = new Date();
      const actualYear = today.getFullYear();
      const actualMonthIdx = today.getMonth();

      for (let m = 11; m >= 0; m--) {
        // Skip future months relative to actual today's date
        if (currentYear > actualYear || (currentYear === actualYear && m > actualMonthIdx)) {
          continue;
        }
        const sales = monthlySales[m];
        let daysInMonth = new Date(currentYear, m + 1, 0).getDate();
        if (m === currentMonthIdx) {
          daysInMonth = new Date(dateStr + 'T00:00:00').getDate();
        }
        
        const avgDaily = daysInMonth > 0 ? sales / daysInMonth : 0;
        const avgWeekly = sales / (daysInMonth / 7 || 1);
        
        const monthName = monthNames[m];
        let statusBadge = '';
        if (m === currentMonthIdx) {
          statusBadge = '<span class="badge" style="background: rgba(249, 115, 22, 0.15); color: #f97316; margin-left: 8px; font-size: 9px; padding: 2px 6px;">CURRENT</span>';
        } else if (m === currentMonthIdx - 1) {
          statusBadge = '<span class="badge" style="background: rgba(99, 102, 241, 0.15); color: var(--brand-light); margin-left: 8px; font-size: 9px; padding: 2px 6px;">PREVIOUS</span>';
        }
        
        const monthLabelColor = (m === currentMonthIdx) ? 'color: #f97316; font-weight: 700;' : (m === currentMonthIdx - 1) ? 'color: var(--brand-light); font-weight: 700;' : 'color: #fff;';
        
        html += `
          <tr style="border-bottom: 1px solid rgba(255,255,255,0.04); font-size: 13px;">
            <td style="padding: 12px 8px; ${monthLabelColor}">${monthName}${statusBadge}</td>
            <td style="padding: 12px 8px; font-weight: 700; ${m === currentMonthIdx ? 'color: #f97316;' : m === currentMonthIdx - 1 ? 'color: var(--brand-light);' : 'color: #fff;'}">${formatPrice(sales)}</td>
            <td style="padding: 12px 8px; color: var(--muted); font-size: 12px;">Daily: ${formatPrice(avgDaily)}</td>
            <td style="padding: 12px 8px; color: var(--muted); font-size: 12px;">Weekly: ${formatPrice(avgWeekly)}</td>
          </tr>
        `;
      }
      breakdownBody.innerHTML = html;
    }

    // Render Festival Selling
    const festivalBody = $('festivalSellingTableBody');
    if (festivalBody && state.reportsData?.festival_sales) {
      const selectedYear = state.selectedFestivalYear || 2026;
      const salesList = state.reportsData.festival_sales;
      const todayStr = getTodayDateString();
      const todayYear = new Date().getFullYear();

      // Filter out future festivals if the selected year is the current year
      let filteredSalesList = salesList || [];
      if (selectedYear === todayYear) {
        filteredSalesList = salesList.filter(fest => {
          const match = (state.reportsData.festival_dates || []).find(fd => fd.name === fest.name && Number(fd.year) === todayYear);
          if (match && match.date) {
            return match.date <= todayStr;
          }
          return true;
        });
      }

      if (!filteredSalesList.length) {
        festivalBody.innerHTML = '<tr><td colspan="5" style="text-align: center; color: var(--muted); padding: 20px;">No festivals found.</td></tr>';
        if ($('festivalPaginationContainer')) $('festivalPaginationContainer').innerHTML = '';
      } else {
        const getFestDate = (festName, yr) => {
          const match = (state.reportsData.festival_dates || []).find(fd => fd.name === festName && Number(fd.year) === Number(yr));
          if (match && match.date) {
            const parts = match.date.split('-');
            return `${parts[2]}-${parts[1]}-${parts[0]}`; // d-m-Y format
          }
          return '';
        };

        // Pagination: 5 festivals per page.
        const festPerPage = 5;
        const festTotal = filteredSalesList.length;
        const festTotalPages = Math.ceil(festTotal / festPerPage);
        if (!state.festivalCurrentPage || state.festivalCurrentPage > festTotalPages) {
          state.festivalCurrentPage = 1;
        }
        const festStart = (state.festivalCurrentPage - 1) * festPerPage;
        const pagedSalesList = filteredSalesList.slice(festStart, festStart + festPerPage);

        renderPagination('festivalPaginationContainer', festTotal, festPerPage, state.festivalCurrentPage, (newPage) => {
          state.festivalCurrentPage = newPage;
          renderReports();
        });

        festivalBody.innerHTML = pagedSalesList.map(fest => {
          const val2026 = fest.sales[2026] || 0;
          const val2025 = fest.sales[2025] || 0;
          const val2024 = fest.sales[2024] || 0;
          const val2023 = fest.sales[2023] || 0;
          
          const formatVal = (val) => {
            if (val === 0) return '0';
            if (val >= 1000) {
              return (val / 1000).toFixed(1) + ' K';
            }
            return Math.round(val).toString();
          };

          const isYearCol = (yr) => yr === selectedYear ? 'color: var(--brand-light); font-weight: 700; background: rgba(99, 102, 241, 0.03);' : 'color: var(--muted);';
          
          const festDate = getFestDate(fest.name, selectedYear);
          const dateSub = festDate ? `<br><small style="color: var(--muted); font-size: 10px; font-weight: 500;">Date: ${festDate}</small>` : '';

          return `
            <tr style="border-bottom: 1px solid rgba(255,255,255,0.04); font-size: 13px;">
              <td style="padding: 12px 8px; color: #fff; font-weight: 600; line-height: 1.4;">${escapeHtml(fest.name)}${dateSub}</td>
              <td style="padding: 12px 8px; text-align: right; ${isYearCol(2026)}">${formatVal(val2026)}</td>
              <td style="padding: 12px 8px; text-align: right; ${isYearCol(2025)}">${formatVal(val2025)}</td>
              <td style="padding: 12px 8px; text-align: right; ${isYearCol(2024)}">${formatVal(val2024)}</td>
              <td style="padding: 12px 8px; text-align: right; ${isYearCol(2023)}">${formatVal(val2023)}</td>
            </tr>
          `;
        }).join('');
      }
    }

    // Render Detailed Analysis sub-tabs
    const detailsTab = state.reportsDetailsTab || 'items';
    if (detailsTab === 'bill') {
      const billBody = $('detailsBillTableBody');
      if (billBody) {
        let filteredOrders = state.reportsData?.range_orders || [];
        
        // Filter by search query (amount, table number, bill number, customer name)
        if (state.billSearchQuery) {
          const query = state.billSearchQuery.toLowerCase().trim();
          filteredOrders = filteredOrders.filter(order => {
            const billNo = (order.formatted_bill_number || ('#' + (order.bill_number || order.id))).toLowerCase();
            const tableNo = (order.table_number ? 'table ' + order.table_number : 'takeaway').toLowerCase();
            const amount = String(Number(order.total_amount || 0).toFixed(2));
            const customer = (order.customer_name || 'walk-in').toLowerCase();
            
            return billNo.includes(query) || 
                   tableNo.includes(query) || 
                   amount.includes(query) || 
                   customer.includes(query);
          });
        }

        if (!filteredOrders.length) {
          billBody.innerHTML = '<tr><td colspan="5" style="text-align: center; color: var(--muted); padding: 20px;">No transactions found.</td></tr>';
          const pagContainer = $('billPaginationContainer');
          if (pagContainer) pagContainer.innerHTML = '';
        } else {
          // Pagination: limit to 15 items per page
          const totalItems = filteredOrders.length;
          const itemsPerPage = 15;
          const totalPages = Math.ceil(totalItems / itemsPerPage);
          if (state.billCurrentPage > totalPages) {
            state.billCurrentPage = Math.max(1, totalPages);
          }
          const startIdx = (state.billCurrentPage - 1) * itemsPerPage;
          const pageOrders = filteredOrders.slice(startIdx, startIdx + itemsPerPage);

          billBody.innerHTML = pageOrders.map(order => {
            const formattedBillNo = order.formatted_bill_number || ('#' + (order.bill_number || order.id));
            const formattedDate = formatDateShort(order.billed_at ? order.billed_at.split(' ')[0] : '');
            return `
              <tr style="border-bottom: 1px solid rgba(255,255,255,0.04); font-size: 13px;">
                <td style="padding: 12px 8px; color: var(--brand-light); font-weight: 700;">${escapeHtml(formattedBillNo)}</td>
                <td style="padding: 12px 8px; color: var(--muted);">${formattedDate}</td>
                <td style="padding: 12px 8px; color: #fff; font-weight: 600;">${escapeHtml(order.customer_name || 'Walk-in')}</td>
                <td style="padding: 12px 8px; color: var(--muted);">${escapeHtml(order.table_number ? 'Table ' + order.table_number : 'Takeaway')}</td>
                <td style="padding: 12px 8px; text-align: right; color: #fff; font-weight: 700;">Rs. ${Number(order.total_amount || 0).toFixed(2)}</td>
              </tr>
            `;
          }).join('');

          renderPagination('billPaginationContainer', totalItems, itemsPerPage, state.billCurrentPage, (newPage) => {
            state.billCurrentPage = newPage;
            renderReports();
          });
        }
      }
    } else if (detailsTab === 'sales') {
      const salesBody = $('detailsSalesTimelineBody');
      if (salesBody) {
        const timelineRange = state.timelineRange || 'day';
        const timelineData = state.reportsData?.timeline_data;
        let timelineRows = [];
        
        if (timelineRange === 'day' && timelineData?.hourly) {
          timelineRows = timelineData.hourly.map(t => {
            const hr = parseInt(t.hour);
            const label = `${String(hr).padStart(2, '0')}:00 - ${String(hr+1).padStart(2, '0')}:00`;
            return {
              label: label,
              orders: parseInt(t.orders),
              revenue: parseFloat(t.revenue)
            };
          });
        } else if (timelineData?.daily) {
          timelineRows = timelineData.daily.map(d => {
            const dt = new Date(d.local_date + 'T00:00:00');
            const dayNamesShort = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
            const label = `${formatDateShort(d.local_date)} (${dayNamesShort[dt.getDay()]})`;
            return {
              label: label,
              orders: d.orders,
              revenue: d.revenue
            };
          });
        }
        
        if (!timelineRows.length) {
          salesBody.innerHTML = '<tr><td colspan="4" style="text-align: center; color: var(--muted); padding: 20px;">No data available.</td></tr>';
        } else {
          const maxRev = Math.max(...timelineRows.map(r => r.revenue), 1);
          salesBody.innerHTML = timelineRows.map(row => {
            const pct = row.revenue / maxRev;
            let badge = '<span class="badge" style="background: rgba(255,255,255,0.05); color: var(--muted); font-size: 9px; padding: 2px 6px; font-weight: 700;">NORMAL</span>';
            if (pct >= 0.7) {
              badge = '<span class="badge" style="background: rgba(249, 115, 22, 0.15); color: #f97316; font-size: 9px; padding: 2px 6px; font-weight: 700;">PEAK</span>';
            } else if (pct >= 0.3) {
              badge = '<span class="badge" style="background: rgba(99, 102, 241, 0.15); color: var(--brand-light); font-size: 9px; padding: 2px 6px; font-weight: 700;">HIGH</span>';
            }
            return `
              <tr style="border-bottom: 1px solid rgba(255,255,255,0.04); font-size: 13px;">
                <td style="padding: 12px 8px; color: #fff; font-weight: 600;">${escapeHtml(row.label)}</td>
                <td style="padding: 12px 8px; text-align: center; color: #fff;">${row.orders}</td>
                <td style="padding: 12px 8px; text-align: right; color: var(--brand-light); font-weight: 700;">₹ ${Math.round(row.revenue).toLocaleString('en-IN')}</td>
                <td style="padding: 12px 8px; text-align: center;">${badge}</td>
              </tr>
            `;
          }).join('');
        }
      }
    } else if (detailsTab === 'items') {
      renderReportItemsTab();
    }
  }

  function calculateHighLow() {
    const tab = state.highLowTab || 'week';
    const highLowData = state.reportsData?.high_low?.[tab];
    
    const formatDayName = (dStr) => {
      if (!dStr) return '--';
      const dt = new Date(dStr + 'T00:00:00');
      const monthNamesShort = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
      const dayNames = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
      const parts = dStr.split('-');
      return `${parseInt(parts[2])}-${monthNamesShort[dt.getMonth()]}-${parts[0]}, ${dayNames[dt.getDay()]}`;
    };

    if (highLowData) {
      if ($('highSalesValue')) $('highSalesValue').textContent = formatPrice(highLowData.current.max);
      if ($('highSalesDate')) $('highSalesDate').textContent = highLowData.current.max > 0 ? formatDayName(highLowData.current.maxDate) : '--';
      
      if ($('lowSalesValue')) $('lowSalesValue').textContent = formatPrice(highLowData.current.min);
      if ($('lowSalesDate')) $('lowSalesDate').textContent = highLowData.current.min > 0 ? formatDayName(highLowData.current.minDate) : '--';

      if ($('prevPeriodHigh')) $('prevPeriodHigh').textContent = formatPrice(highLowData.prev.max);
      if ($('prevPeriodLow')) $('prevPeriodLow').textContent = formatPrice(highLowData.prev.min);
    } else {
      if ($('highSalesValue')) $('highSalesValue').textContent = '--';
      if ($('highSalesDate')) $('highSalesDate').textContent = '--';
      if ($('lowSalesValue')) $('lowSalesValue').textContent = '--';
      if ($('lowSalesDate')) $('lowSalesDate').textContent = '--';
      if ($('prevPeriodHigh')) $('prevPeriodHigh').textContent = '--';
      if ($('prevPeriodLow')) $('prevPeriodLow').textContent = '--';
    }
  }

  function renderReportItemsTab() {
    if (!$('detailsItemsTableBody') || $('detailsItemsTableBody').isMock) return;
    const itemsList = state.reportsData?.all_sold_items || [];
    
    // Category chips come from the MASTER category list, not just from items
    // sold in the selected range -- every category must always be visible,
    // whether or not it sold anything today. Sold-item names are unioned in
    // as a fallback (covers 'No Category' and renamed/deleted categories).
    const masterParentNames = (state.categories || [])
      .filter(c => !c.parent_id)
      .map(c => c.name)
      .filter(Boolean);
    const soldCatNames = itemsList.map(item => item.category).filter(Boolean);
    const categories = ['All Categories', ...new Set([...masterParentNames, ...soldCatNames])];

    // Subcategory chips follow the SELECTED category: "All Categories" shows
    // every subcategory (master list), a specific category shows only its own
    // subcategories. Sold names unioned in as the same fallback.
    const selCatForSubs = state.selectedReportCategory || 'All Categories';
    let masterSubNames;
    if (selCatForSubs === 'All Categories') {
      masterSubNames = (state.categories || []).filter(c => c.parent_id).map(c => c.name);
    } else {
      const parentCat = (state.categories || []).find(c => !c.parent_id && c.name === selCatForSubs);
      masterSubNames = parentCat
        ? (state.categories || []).filter(c => Number(c.parent_id) === Number(parentCat.id)).map(c => c.name)
        : [];
    }
    const subSource = selCatForSubs === 'All Categories'
      ? itemsList
      : itemsList.filter(item => item.category === selCatForSubs);
    const soldSubNames = subSource.map(item => item.sub_category).filter(Boolean);
    const subCategories = ['All Subcategories', ...new Set([...masterSubNames.filter(Boolean), ...soldSubNames])];

    const catContainer = $('reportItemCategoryContainer');
    if (catContainer) {
      catContainer.innerHTML = categories.map(cat => {
        const active = (state.selectedReportCategory || 'All Categories') === cat;
        const btnClass = active ? 'badge' : 'badge ghost';
        const bg = active ? 'background: var(--brand); color: #fff;' : 'background: rgba(255,255,255,0.03); color: var(--muted);';
        const count = cat === 'All Categories'
          ? itemsList.length
          : itemsList.filter(item => item.category === cat).length;
        return `
          <button type="button" class="${btnClass}" data-filter-cat="${escapeHtml(cat)}" style="${bg} border: 1px solid rgba(255,255,255,0.06); padding: 6px 12px; font-size: 11px; font-weight: 600; cursor: pointer; border-radius: 20px;">
            ${escapeHtml(cat)} (${count})
          </button>
        `;
      }).join('');
    }

    const subCatContainer = $('reportItemSubCategoryContainer');
    if (subCatContainer) {
      subCatContainer.innerHTML = subCategories.map(sub => {
        const active = (state.selectedReportSubCategory || 'All Subcategories') === sub;
        const btnClass = active ? 'badge' : 'badge ghost';
        const bg = active ? 'background: #f97316; color: #fff;' : 'background: rgba(255,255,255,0.03); color: var(--muted);';
        const count = sub === 'All Subcategories'
          ? subSource.length
          : subSource.filter(item => item.sub_category === sub).length;
        return `
          <button type="button" class="${btnClass}" data-filter-subcat="${escapeHtml(sub)}" style="${bg} border: 1px solid rgba(255,255,255,0.06); padding: 6px 12px; font-size: 11px; font-weight: 600; cursor: pointer; border-radius: 20px;">
            ${escapeHtml(sub)} (${count})
          </button>
        `;
      }).join('');
    }

    const selCat = state.selectedReportCategory || 'All Categories';
    const selSub = state.selectedReportSubCategory || 'All Subcategories';

    const filtered = itemsList.filter(item => {
      const matchCat = selCat === 'All Categories' || item.category === selCat;
      const matchSub = selSub === 'All Subcategories' || item.sub_category === selSub;
      return matchCat && matchSub;
    });

    const total = itemsList.reduce((sum, item) => sum + item.amount, 0);
    const subtotal = filtered.reduce((sum, item) => sum + item.amount, 0);

    if ($('reportItemTotalAmt')) $('reportItemTotalAmt').textContent = 'Rs. ' + Math.round(total).toLocaleString('en-IN');
    if ($('reportItemSubtotalAmt')) $('reportItemSubtotalAmt').textContent = 'Rs. ' + Math.round(subtotal).toLocaleString('en-IN');

    const itemsBody = $('detailsItemsTableBody');
    if (itemsBody) {
      if (!filtered.length) {
        itemsBody.innerHTML = '<tr><td colspan="5" style="text-align: center; color: var(--muted); padding: 20px;">No items found.</td></tr>';
        const pagContainer = $('itemsPaginationContainer');
        if (pagContainer) pagContainer.innerHTML = '';
      } else {
        const totalItems = filtered.length;
        const itemsPerPage = 15;
        const totalPages = Math.ceil(totalItems / itemsPerPage);
        if (state.itemsCurrentPage > totalPages) {
          state.itemsCurrentPage = Math.max(1, totalPages);
        }
        const startIdx = (state.itemsCurrentPage - 1) * itemsPerPage;
        const pageItems = filtered.slice(startIdx, startIdx + itemsPerPage);

        itemsBody.innerHTML = pageItems.map(item => `
          <tr style="border-bottom: 1px solid rgba(255,255,255,0.04); font-size: 13px;">
            <td style="padding: 12px 8px; color: #fff; font-weight: 600;">${escapeHtml(item.name)}</td>
            <td style="padding: 12px 8px; color: var(--muted);"><span class="badge" style="background: rgba(255,255,255,0.05); color: var(--muted); font-size: 10px;">${escapeHtml(item.category)}</span></td>
            <td style="padding: 12px 8px; text-align: right; color: var(--muted);">Rs. ${item.price}</td>
            <td style="padding: 12px 8px; text-align: center; color: #fff; font-weight: 700;">${item.qty}</td>
            <td style="padding: 12px 8px; text-align: right; color: var(--brand-light); font-weight: 700;">Rs. ${Math.round(item.amount).toLocaleString('en-IN')}</td>
          </tr>
        `).join('');

        renderPagination('itemsPaginationContainer', totalItems, itemsPerPage, state.itemsCurrentPage, (newPage) => {
          state.itemsCurrentPage = newPage;
          renderReportItemsTab();
        });
      }
    }
  }

  function renderPagination(containerId, totalItems, itemsPerPage, currentPage, onPageChange) {
    const container = $(containerId);
    if (!container) return;
    
    if (totalItems <= itemsPerPage) {
      container.innerHTML = '';
      return;
    }
    
    const totalPages = Math.ceil(totalItems / itemsPerPage);
    const startIdx = (currentPage - 1) * itemsPerPage + 1;
    const endIdx = Math.min(currentPage * itemsPerPage, totalItems);
    
    let buttonsHtml = '';
    
    buttonsHtml += `
      <button type="button" class="btn" ${currentPage === 1 ? 'disabled style="opacity: 0.5; pointer-events: none;"' : ''} id="${containerId}-prev" style="padding: 6px 12px; min-height: auto; font-size: 12px; font-weight: 700; background: rgba(255,255,255,0.05); border: 1px solid rgba(255,255,255,0.08); border-radius: var(--radius-sm); color: #fff; cursor: pointer; display: flex; align-items: center; justify-content: center; gap: 4px; border: 1px solid rgba(255,255,255,0.08); transition: background 0.2s;">
        &larr; Prev
      </button>
    `;
    
    buttonsHtml += `
      <span style="font-weight: 600; color: var(--muted); font-size: 12px; font-family: 'Outfit', sans-serif;">Showing ${startIdx}-${endIdx} of ${totalItems}</span>
    `;
    
    buttonsHtml += `
      <button type="button" class="btn" ${currentPage === totalPages ? 'disabled style="opacity: 0.5; pointer-events: none;"' : ''} id="${containerId}-next" style="padding: 6px 12px; min-height: auto; font-size: 12px; font-weight: 700; background: rgba(255,255,255,0.05); border: 1px solid rgba(255,255,255,0.08); border-radius: var(--radius-sm); color: #fff; cursor: pointer; display: flex; align-items: center; justify-content: center; gap: 4px; border: 1px solid rgba(255,255,255,0.08); transition: background 0.2s;">
        Next &rarr;
      </button>
    `;
    
    container.innerHTML = buttonsHtml;
    
    const prevBtn = $(`${containerId}-prev`);
    if (prevBtn) {
      prevBtn.addEventListener('click', (e) => {
        e.preventDefault();
        if (currentPage > 1) onPageChange(currentPage - 1);
      });
    }
    
    const nextBtn = $(`${containerId}-next`);
    if (nextBtn) {
      nextBtn.addEventListener('click', (e) => {
        e.preventDefault();
        if (currentPage < totalPages) onPageChange(currentPage + 1);
      });
    }
  }

  // ==== Month-wise Item Comparison (comparison.php) =========================
  // Client + up-to-4-month chips; per selected month one section=items call
  // (range=month) runs in parallel; rows merge per item NAME (summing any
  // price-variant rows) so each cell = that month's total qty + amount.

  function comparisonMonthList() {
    // Last 12 months, newest first, as { key: 'YYYY-MM', label: 'Jul 2026' }.
    const list = [];
    const now = new Date();
    for (let i = 0; i < 12; i++) {
      const d = new Date(now.getFullYear(), now.getMonth() - i, 1);
      const key = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
      list.push({ key, label: `${MONTH_LABELS[d.getMonth()].substring(0, 3)} ${d.getFullYear()}` });
    }
    return list;
  }

  function ensureComparisonDefaults() {
    if (!Array.isArray(state.comparisonMonths) || !state.comparisonMonths.length) {
      const months = comparisonMonthList();
      // Default: last 3 months.
      state.comparisonMonths = [months[2].key, months[1].key, months[0].key];
    }
  }

  function renderComparisonMonthChips() {
    const container = $('comparisonMonthChips');
    if (!container || container.isMock) return;
    container.innerHTML = comparisonMonthList().map(m => {
      const active = state.comparisonMonths.includes(m.key);
      const bg = active ? 'background: var(--brand); color: #fff;' : 'background: rgba(255,255,255,0.03); color: var(--muted);';
      return `
        <button type="button" class="badge${active ? '' : ' ghost'}" data-comp-month="${m.key}" style="${bg} border: 1px solid rgba(255,255,255,0.06); padding: 4px 10px; font-size: 11px; font-weight: 600; cursor: pointer; border-radius: 20px;">
          ${m.label}
        </button>
      `;
    }).join('');
  }

  async function loadComparisonData() {
    const body = $('comparisonTableBody');
    if (!body || body.isMock) return;
    ensureComparisonDefaults();
    renderComparisonMonthChips();

    // Ensure categories are loaded
    if (!Array.isArray(state.categories) || !state.categories.length) {
      const cachedCats = getCachedData('categories');
      if (cachedCats && cachedCats.length) {
        state.categories = cachedCats;
      } else {
        try {
          const cats = await api('/categories');
          state.categories = cats || [];
          setCachedData('categories', state.categories);
        } catch {}
      }
    }

    // Ensure items are loaded
    if (!Array.isArray(state.items) || !state.items.length) {
      const cachedItems = getCachedData('items');
      if (cachedItems && cachedItems.length) {
        state.items = cachedItems;
      } else {
        try {
          const itemsData = await api('/menu-items');
          state.items = itemsData || [];
          setCachedData('items', state.items);
        } catch {}
      }
    }

    // Populate category dropdowns on load
    const catSelect = $('comparisonFilterCategory');
    if (catSelect && !catSelect.isMock) {
      const savedVal = catSelect.value;
      setOptions(catSelect, parentCategories(), { placeholder: 'Category', includeBlank: true });
      catSelect.value = savedVal;

      const subSelect = $('comparisonFilterSubCategory');
      if (subSelect && !subSelect.isMock) {
        const savedSub = subSelect.value;
        setOptions(subSelect, subCategories(savedVal), { placeholder: 'Subcat', includeBlank: true });
        subSelect.value = savedSub;
      }
    }

    const months = [...state.comparisonMonths].sort(); // oldest -> newest columns
    const loadSeq = (state.comparisonLoadSeq = (state.comparisonLoadSeq || 0) + 1);
    body.innerHTML = '<tr><td colspan="8" style="text-align: center; color: var(--muted); padding: 24px;">Loading...</td></tr>';

    try {
      const results = await Promise.all(months.map(async (m) => {
        const data = await api(`/reports/summary?date=${m}-01&range=month&report_client=${state.client}&section=items&last_order_id=0`);
        return { month: m, items: (data && data.all_sold_items) || [] };
      }));
      if (loadSeq !== state.comparisonLoadSeq) return; // superseded

      // Merge per item name: months -> { qty, amount }.
      const itemsMap = new Map();
      results.forEach(({ month, items }) => {
        items.forEach((item) => {
          const name = item.name || '';
          if (!name) return;
          if (!itemsMap.has(name)) {
            itemsMap.set(name, { name, category: item.category || '', months: {} });
          }
          const entry = itemsMap.get(name);
          if (!entry.category && item.category) entry.category = item.category;
          if (!entry.months[month]) entry.months[month] = { qty: 0, amount: 0 };
          entry.months[month].qty += Number(item.qty) || 0;
          entry.months[month].amount += Number(item.amount) || 0;
        });
      });

      state.comparisonData = { months, items: Array.from(itemsMap.values()) };
      state.comparisonPage = 1;
      renderComparisonTable();
    } catch (err) {
      if (loadSeq !== state.comparisonLoadSeq) return;
      body.innerHTML = `<tr><td colspan="8" style="text-align: center; color: var(--muted); padding: 24px;">${escapeHtml(err.message || 'Failed to load.')}</td></tr>`;
    }
  }

  function renderComparisonTable() {
    const head = $('comparisonTableHead');
    const body = $('comparisonTableBody');
    if (!head || !body || body.isMock) return;
    const data = state.comparisonData;
    if (!data) return;

    const monthLabel = (key) => {
      const [y, m] = key.split('-').map(Number);
      return `${MONTH_LABELS[m - 1].substring(0, 3)} ${y}`;
    };

    head.innerHTML = `
      <tr style="border-bottom: 1px solid rgba(255,255,255,0.08); font-size: 11px; font-weight: 700; color: var(--muted); text-transform: uppercase;">
        <th style="padding: 12px 8px;">Item Name</th>
        <th style="padding: 12px 8px;">Category</th>
        ${data.months.map(m => `<th style="padding: 12px 8px; text-align: right;">${monthLabel(m)}<br><small style="font-weight: 500; text-transform: none;">Qty / Amount</small></th>`).join('')}
      </tr>
    `;

    const query = (state.comparisonSearch || '').trim().toLowerCase();
    const catFilter = $('comparisonFilterCategory') ? $('comparisonFilterCategory').value : '';
    const subCatFilter = $('comparisonFilterSubCategory') ? $('comparisonFilterSubCategory').value : '';

    let rows = data.items;
    
    // Helper to find shortcode for any sold item name
    const getSoldItemShortcode = (name) => {
      if (!name) return '';
      const menuItem = (state.items || []).find(it => it.name === name);
      if (menuItem) {
        return getItemShortcode(menuItem).toLowerCase();
      }
      // Fallback: generate from name initials
      const words = name.trim().split(/\s+/);
      return words.map(w => w.charAt(0)).join('').toLowerCase();
    };

    // Filter by search text (matching name or shortcode)
    if (query) {
      const cleanQuery = query.replace(/\s+/g, '');
      rows = rows.filter(it => {
        const nameLower = (it.name || '').toLowerCase();
        const code = getSoldItemShortcode(it.name);
        return nameLower.includes(query) || code.includes(cleanQuery);
      });
    }

    // Filter by category
    if (catFilter) {
      const catObj = state.categories.find(c => String(c.id) === String(catFilter));
      const catName = catObj ? catObj.name.toLowerCase() : '';
      if (catName) {
        rows = rows.filter(it => (it.category || '').toLowerCase() === catName);
      }
    }

    // Filter by subcategory
    if (subCatFilter) {
      const subCatObj = state.categories.find(c => String(c.id) === String(subCatFilter));
      const subCatName = subCatObj ? subCatObj.name.toLowerCase() : '';
      if (subCatName) {
        rows = rows.filter(it => (it.sub_category || '').toLowerCase() === subCatName);
      }
    }

    const totals = (it) => data.months.reduce((acc, m) => {
      const cell = it.months[m];
      if (cell) { acc.qty += cell.qty; acc.amount += cell.amount; }
      return acc;
    }, { qty: 0, amount: 0 });

    rows = [...rows].sort((a, b) => totals(b).qty - totals(a).qty);

    if (!rows.length) {
      body.innerHTML = '<tr><td colspan="8" style="text-align: center; color: var(--muted); padding: 24px;">No items found.</td></tr>';
      if ($('comparisonPaginationContainer')) $('comparisonPaginationContainer').innerHTML = '';
      return;
    }

    const perPage = 15;
    const totalRows = rows.length;
    const totalPages = Math.ceil(totalRows / perPage);
    if (!state.comparisonPage || state.comparisonPage > totalPages) state.comparisonPage = 1;
    const start = (state.comparisonPage - 1) * perPage;
    const paged = rows.slice(start, start + perPage);

    const cellHtml = (cell) => {
      if (!cell || (!cell.qty && !cell.amount)) {
        return '<span style="color: var(--muted);">-</span>';
      }
      return `<span style="color: #fff; font-weight: 700;">${Math.round(cell.qty)}</span>
              <br><small style="color: var(--brand-light); font-weight: 600;">Rs. ${Math.round(cell.amount).toLocaleString('en-IN')}</small>`;
    };

    body.innerHTML = paged.map(it => {
      return `
        <tr style="border-bottom: 1px solid rgba(255,255,255,0.04); font-size: 13px;">
          <td style="padding: 12px 8px; color: #fff; font-weight: 600;">${escapeHtml(it.name)}</td>
          <td style="padding: 12px 8px;"><span class="badge" style="background: rgba(255,255,255,0.05); color: var(--muted); font-size: 10px;">${escapeHtml(it.category || 'No Category')}</span></td>
          ${data.months.map(m => `<td style="padding: 12px 8px; text-align: right; line-height: 1.5;">${cellHtml(it.months[m])}</td>`).join('')}
        </tr>
      `;
    }).join('');

    renderPagination('comparisonPaginationContainer', totalRows, perPage, state.comparisonPage, (newPage) => {
      state.comparisonPage = newPage;
      renderComparisonTable();
    });
  }

  function setActiveDetailsTab(tab) {
    state.reportsDetailsTab = tab;
    
    if ($('detailsTabBill')) $('detailsTabBill').classList.toggle('active', tab === 'bill');
    if ($('detailsTabSales')) $('detailsTabSales').classList.toggle('active', tab === 'sales');
    if ($('detailsTabItems')) $('detailsTabItems').classList.toggle('active', tab === 'items');
    
    if ($('detailsContentBill')) $('detailsContentBill').style.display = tab === 'bill' ? 'block' : 'none';
    if ($('detailsContentSales')) $('detailsContentSales').style.display = tab === 'sales' ? 'block' : 'none';
    if ($('detailsContentItems')) $('detailsContentItems').style.display = tab === 'items' ? 'block' : 'none';
    
    renderReports();
  }

  async function init() {
    loadSession();
    renderAll();
    bindEvents();
    
    // Restore remembered mobile and pin
    const savedMobile = localStorage.getItem('pos_last_mobile');
    const savedPin = localStorage.getItem('pos_last_pin');
    if (savedMobile && $('mobileInput') && !$('mobileInput').isMock) {
      $('mobileInput').value = savedMobile;
    }
    if (savedPin && $('pinInput') && !$('pinInput').isMock) {
      $('pinInput').value = savedPin;
      $('pinInput').dispatchEvent(new Event('input'));
    }
    

    
    // Restore saved active tab on page refresh (set classes only, refresh will load the data)
    const activeTab = document.body.dataset.activeTab || 'items';
    localStorage.setItem('pos_menu_active_tab', activeTab);
    document.querySelectorAll('.tab').forEach((tab) => tab.classList.remove('active'));
    document.querySelectorAll('.workspace').forEach((tab) => tab.classList.remove('active'));
    const defaultTabButton = document.querySelector(`.tab[data-tab="${activeTab}"]`) || document.querySelector('.tab');
    if (defaultTabButton) {
      defaultTabButton.classList.add('active');
      const wsTab = $(defaultTabButton.dataset.tab + 'Tab');
      if (wsTab) {
        wsTab.classList.add('active');
      }
    }
    
    await refresh();
  }

  init();
})();
