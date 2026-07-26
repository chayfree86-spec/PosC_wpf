<!-- Modals -->
<div id="itemModal" class="modal-overlay">
  <div class="modal-content">
    <button type="button" class="close-modal-btn" aria-label="Close modal">&times;</button>
    <form id="itemForm" class="editor panel">
      <input type="hidden" id="itemId">
      <input type="hidden" id="itemDescription">
      <input type="hidden" id="itemImage">
      <div class="editor-head">
        <h2 id="itemFormTitle">Add New Menu Item</h2>
        <button type="button" class="ghost" data-reset="item">Clear</button>
      </div>
      
      <div class="form-row-2">
        <label>Category<select id="itemCategory" required></select></label>
        <label>Sub Category<select id="itemSubCategory"></select></label>
      </div>

      <div class="form-group-name">
        <div class="name-header">
          <span class="field-label">ITEM NAME</span>
          <div class="lang-toggle-pill">
            <button type="button" class="lang-btn active" data-lang="hindi">HINDI</button>
            <button type="button" class="lang-btn" data-lang="english">ENGLISH</button>
          </div>
        </div>
        <input id="itemName" required placeholder="Type in English for Hindi">
        
        <div id="translitContainer" class="translit-box">
          <span class="field-label">HINDI TRANSLITERATION</span>
          <div id="translitSuggestions" class="suggestions-list">
            <span class="placeholder-suggestion">Type to see suggestions...</span>
          </div>
        </div>
      </div>

      <div class="form-row-2">
        <label>Item Price
          <div class="input-with-icon">
            <span class="input-icon">₹</span>
            <input id="itemPrice" type="number" min="0" step="0.01" required placeholder="0.00">
          </div>
        </label>
        <label>Item Code<input id="itemCode" placeholder="e.g. mt01"></label>
      </div>

      <div class="form-group-image">
        <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 8px;">
          <span class="field-label" style="margin: 0;">Menu Images (Upload one or more)</span>
          <button type="button" id="selectFromGalleryBtn" class="ghost" style="padding: 2px 10px; font-size: 11px; min-height: auto; font-weight: 600; text-transform: none; box-shadow: none; border-radius: var(--radius-sm);">🖼️ Select from Gallery</button>
        </div>
        <div id="imageUploadZone" class="upload-zone">
          <input type="file" id="itemImageFile" accept="image/*" multiple style="display: none;">
          <div id="uploadPlaceholder" class="upload-placeholder">
            <span class="upload-icon">📁</span>
            <span class="upload-text">Upload Product Images (Drag & Drop)</span>
            <span class="upload-size-hint">Recommended: 800 x 600 px (4:3). Any size will auto-fit.</span>
          </div>
          <div id="itemUploadProgress" class="upload-progress upload-progress-inside" style="display: none;">
            <div class="upload-progress-head">
              <span id="itemUploadProgressLabel">Uploading image...</span>
              <span id="itemUploadProgressPercent">0%</span>
            </div>
            <div class="upload-progress-track">
              <div id="itemUploadProgressBar" class="upload-progress-bar"></div>
            </div>
          </div>
        </div>
        <!-- Grid of uploaded images preview -->
        <div id="itemImagesGrid" class="uploaded-images-grid" style="display: none;"></div>
      </div>

      <div class="checks" style="display: none;">
        <label><input id="itemVeg" type="checkbox" checked> Veg</label>
        <label><input id="itemAvailable" type="checkbox" checked> Available</label>
        <label><input id="itemFavorite" type="checkbox"> Favorite</label>
      </div>

      <button type="submit" id="saveItemSubmitBtn">Save Menu Item</button>
    </form>

    <!-- Last Added Item preview block -->
    <div id="lastAddedContainer" class="last-added-card" style="display: none;">
      <div class="card-head">
        <span class="dot-success"></span>
        <h4>Last Added Item</h4>
      </div>
      <div class="card-body">
        <div class="info-row">
          <span class="info-label">Name</span>
          <span id="lastAddedName" class="info-val">-</span>
        </div>
        <div class="info-row">
          <span class="info-label">Price</span>
          <span id="lastAddedPrice" class="info-val">-</span>
        </div>
        <div class="info-row">
          <span class="info-label">Code</span>
          <span id="lastAddedCode" class="info-val">-</span>
        </div>
      </div>
    </div>
  </div>
</div>

<div id="categoryModal" class="modal-overlay">
  <div class="modal-content compact">
    <button type="button" class="close-modal-btn" aria-label="Close modal">&times;</button>
    <form id="categoryForm" class="editor panel">
      <input type="hidden" id="categoryId">
      <input type="hidden" id="categoryImage">
      <div class="editor-head">
        <h2 id="categoryFormTitle">Add Category</h2>
        <button type="button" class="ghost" data-reset="category">Clear</button>
      </div>
      <label>Name<input id="categoryName" required></label>
      <label>Parent<select id="categoryParent"></select></label>
      <label>Sort Order<input id="categorySort" type="number" step="1" value="0"></label>
      
      <div class="form-group-image">
        <span class="field-label">Category Image (Optional)</span>
        <div id="categoryImageUploadZone" class="upload-zone">
          <input type="file" id="categoryImageFile" accept="image/*" style="display: none;">
          <div id="categoryUploadPlaceholder" class="upload-placeholder">
            <span class="upload-icon">📁</span>
            <span class="upload-text">Upload Category Image</span>
            <span class="upload-size-hint">Recommended: 800 x 600 px (4:3). Any size will auto-fit.</span>
          </div>
          <div id="categoryUploadPreviewContainer" class="upload-preview-container" style="display: none;">
            <img id="categoryUploadPreview" src="" alt="Category preview">
            <button type="button" id="removeCategoryImageBtn" class="remove-img-btn" title="Remove image">&times;</button>
          </div>
        </div>
        <div id="categoryUploadProgress" class="upload-progress" style="display: none;">
          <div class="upload-progress-head">
            <span id="categoryUploadProgressLabel">Uploading image...</span>
            <span id="categoryUploadProgressPercent">0%</span>
          </div>
          <div class="upload-progress-track">
            <div id="categoryUploadProgressBar" class="upload-progress-bar"></div>
          </div>
        </div>
      </div>

      <label class="inline-check"><input id="categoryActive" type="checkbox" checked> Active</label>
      <button type="submit">Save Category</button>
    </form>
  </div>
</div>

<div id="tableModal" class="modal-overlay">
  <div class="modal-content compact">
    <button type="button" class="close-modal-btn" aria-label="Close modal">&times;</button>
    <form id="tableForm" class="editor panel">
      <input type="hidden" id="tableId">
      <div class="editor-head">
        <h2 id="tableFormTitle">Add New Table</h2>
      </div>
      <label>Table Name/Number<input id="tableNumber" required placeholder="e.g. Table 05"></label>
      <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 2px;">
        <label style="margin: 0; text-transform: uppercase; font-size: 11px; font-weight: 700; letter-spacing: 0.05em; color: var(--muted);">Select Area</label>
        <button type="button" id="quickAddAreaBtn" class="ghost" style="padding: 2px 8px; font-size: 11px; min-height: auto; font-weight: 600; text-transform: none; box-shadow: none; border-radius: var(--radius-sm);">+ Add Area</button>
      </div>
      <select id="tableArea" required></select>
      <button type="submit">Create Table</button>
    </form>
  </div>
</div>

<div id="areaModal" class="modal-overlay">
  <div class="modal-content compact">
    <button type="button" class="close-modal-btn" aria-label="Close modal">&times;</button>
    <form id="areaForm" class="editor panel">
      <input type="hidden" id="areaId">
      <div class="editor-head">
        <h2 id="areaFormTitle">Add Dining Area</h2>
        <button type="button" class="ghost" data-reset="area">Clear</button>
      </div>
      <label>Name<input id="areaName" required></label>
      <label>Sort Order<input id="areaSort" type="number" step="1" value="0"></label>
      <label class="inline-check"><input id="areaActive" type="checkbox" checked> Active</label>
      <button type="submit">Save Area</button>
    </form>
  </div>
</div>

<!-- QR Modal -->
<div id="qrModal" class="modal-overlay">
  <div class="modal-content compact">
    <button type="button" class="close-modal-btn" aria-label="Close modal">&times;</button>
    <div class="editor panel">
      <div class="editor-head">
        <h2 id="qrModalTitle">Table QR Code</h2>
      </div>
      
      <div style="background: white; padding: 16px; border-radius: var(--radius-sm); display: flex; justify-content: center; align-items: center; margin: 16px 0; box-shadow: 0 4px 20px rgba(0,0,0,0.15);">
        <img id="qrModalImage" src="" alt="QR Code" style="width: 250px; height: 250px; display: block; max-width: 100%;">
      </div>

      <div style="margin-bottom: 16px;">
        <label style="text-transform: uppercase; font-size: 11px; font-weight: 700; letter-spacing: 0.05em; color: var(--muted); margin-bottom: 6px; display: block;">Direct Link</label>
        <div style="display: flex; gap: 8px;">
          <input id="qrModalLink" readonly style="flex: 1;" onclick="this.select()">
          <button type="button" id="copyQrLinkBtn" style="padding: 0 16px; min-height: 42px; width: auto; font-size: 13px; font-weight: 600;">Copy</button>
        </div>
      </div>

      <div style="display: flex; gap: 12px; margin-top: 8px;">
        <button type="button" id="downloadQrBtn" style="flex: 1; min-height: 42px; font-size: 13px; font-weight: 600;">Download PNG</button>
        <button type="button" id="printQrBtn" style="flex: 1; min-height: 42px; font-size: 13px; font-weight: 600; background: transparent; border: 1px solid rgba(255, 255, 255, 0.15); color: #fff;">Print QR</button>
      </div>
    </div>
  </div>
</div>

<!-- Gallery Image Assignment Modal -->
<div id="assignImageModal" class="modal-overlay">
  <div class="modal-content compact" style="max-width: 520px; width: 95%;">
    <button type="button" class="close-modal-btn" aria-label="Close modal">&times;</button>
    <div class="editor panel">
      <input type="hidden" id="assignImageUrl">
      <div class="editor-head">
        <h2>Assign Gallery Image</h2>
      </div>
      
      <div style="display: flex; align-items: center; gap: 14px; margin-bottom: 16px; background: rgba(255,255,255,0.02); padding: 10px; border-radius: var(--radius-sm); border: 1px solid rgba(255,255,255,0.05);">
        <img id="assignImagePreview" src="" style="width: 64px; height: 64px; border-radius: var(--radius-sm); border: 1px solid rgba(255,255,255,0.1); background: rgba(0, 0, 0, 0.25); padding: 0px; object-fit: contain;">
        <div style="flex: 1; min-width: 0;">
          <h4 style="color: #fff; font-size: 13px; margin: 0 0 2px 0;">Image Preview</h4>
          <p style="font-size: 11px; color: var(--muted); margin: 0; word-break: break-all;" id="assignImageNameText">Assigning this image to a Menu Item or Category</p>
        </div>
      </div>

      <!-- Premium Segmented Tab Selector -->
      <div style="display: flex; gap: 4px; background: rgba(0, 0, 0, 0.25); padding: 4px; border-radius: var(--radius-sm); margin-bottom: 14px; border: 1px solid rgba(255, 255, 255, 0.05);">
        <button type="button" id="assignTabItems" class="sub-tab active" style="flex: 1; padding: 8px; font-size: 12px; min-height: auto; border-radius: var(--radius-sm); border: none; background: linear-gradient(135deg, var(--brand) 0%, var(--brand-dark) 100%); font-weight: 600; color: #fff; cursor: pointer; transition: all 0.2; flex: 1;">🍔 Menu Items</button>
        <button type="button" id="assignTabCategories" class="sub-tab" style="flex: 1; padding: 8px; font-size: 12px; min-height: auto; border-radius: var(--radius-sm); border: none; background: transparent; font-weight: 600; color: var(--muted); cursor: pointer; transition: all 0.2; flex: 1;">📁 Categories</button>
      </div>

      <!-- Dynamic Item Search and Filters Section -->
      <div id="assignItemsFilterRow" style="display: flex; flex-direction: column; gap: 10px; margin-bottom: 12px;">
        <input type="search" id="assignItemSearch" placeholder="🔍 Search menu items..." style="width: 100%; min-height: 40px; margin: 0;">
        <div style="display: flex; gap: 8px; align-items: center;" id="assignFilterSelectsContainer">
          <div style="flex: 1;" id="assignCategoryRow">
            <select id="assignFilterCategory" aria-label="Category filter"></select>
          </div>
          <div style="flex: 1;" id="assignSubCategoryRow">
            <select id="assignFilterSubCategory" aria-label="Subcategory filter"></select>
          </div>
        </div>
      </div>

      <div style="margin-bottom: 6px; text-transform: uppercase; font-size: 10px; font-weight: 700; letter-spacing: 0.05em; color: var(--muted);" id="assignListLabel">Select Target to Assign</div>
      <div id="assignItemsList" style="max-height: 240px; overflow-y: auto; border: 1px solid rgba(255,255,255,0.08); border-radius: var(--radius-sm); padding: 4px; background: rgba(0,0,0,0.15);"></div>
    </div>
  </div>
</div>

<!-- Gallery Image Edit/Rename Modal -->
<div id="editGalleryImageModal" class="modal-overlay">
  <div class="modal-content compact">
    <button type="button" class="close-modal-btn" aria-label="Close modal">&times;</button>
    <form id="editGalleryImageForm" class="editor panel">
      <input type="hidden" id="editGalleryImageId">
      <input type="hidden" id="editGalleryImageUrl">
      <div class="editor-head">
        <h2>Edit Image Label</h2>
      </div>
      
      <div style="text-align: center; margin: 12px 0;">
        <img id="editGalleryImagePreview" src="" style="width: 120px; height: 120px; border-radius: var(--radius-sm); border: 1px solid rgba(255,255,255,0.1); background: rgba(0, 0, 0, 0.25); padding: 0px; object-fit: contain;">
      </div>

      <label>Label / Name
        <input id="editGalleryImageName" required placeholder="e.g. burger_image.jpg">
      </label>

      <label>Category
        <select id="editGalleryImageCategory"></select>
      </label>

      <label>Sub Category
        <select id="editGalleryImageSubCategory"></select>
      </label>

      <label class="inline-check">
        <input id="editGalleryImageVisible" type="checkbox" checked> Show in Gallery
      </label>

      <button type="submit">Save Changes</button>
    </form>
  </div>
</div>

<!-- Gallery Image Upload Modal -->
<div id="galleryUploadModal" class="modal-overlay">
  <div class="modal-content compact">
    <button type="button" class="close-modal-btn" aria-label="Close modal">&times;</button>
    <form id="galleryUploadForm" class="editor panel">
      <div class="editor-head">
        <h2>Upload Gallery Image</h2>
      </div>

      <label>Category
        <select id="galleryUploadCategory"></select>
      </label>

      <label>Sub Category
        <select id="galleryUploadSubCategory"></select>
      </label>

      <div id="galleryDragDropZone" class="upload-zone" style="margin-top: 16px; margin-bottom: 16px; padding: 24px; border: 2px dashed rgba(255,255,255,0.1); border-radius: var(--radius-sm); text-align: center; cursor: pointer; background: rgba(0,0,0,0.15); transition: border-color 0.2s ease;">
        <input type="file" id="galleryUploadFileInput" accept="image/*" multiple style="display: none;">
        <div style="font-size: 24px; margin-bottom: 8px;">📤</div>
        <p style="font-size: 12px; font-weight: 500; color: #fff; margin: 0 0 4px 0;">Drag & Drop files here or click to browse</p>
        <p style="font-size: 11px; color: var(--muted); margin: 0;">Supports PNG, JPG, JPEG, WEBP (multiple allowed)</p>
        <div id="galleryUploadFilesList" style="margin-top: 12px; font-size: 11px; color: var(--brand-light); text-align: left; max-height: 80px; overflow-y: auto; display: none;"></div>
      </div>
      <div id="galleryUploadProgress" class="upload-progress" style="display: none;">
        <div class="upload-progress-head">
          <span id="galleryUploadProgressLabel">Uploading image...</span>
          <span id="galleryUploadProgressPercent">0%</span>
        </div>
        <div class="upload-progress-track">
          <div id="galleryUploadProgressBar" class="upload-progress-bar"></div>
        </div>
      </div>

      <button type="submit" id="galleryUploadSubmitBtn" style="width: 100%; min-height: 40px; font-weight: 600; font-size: 13px;">Upload Image</button>
    </form>
  </div>
</div>


<!-- Lightbox Modal for Full Image Preview -->
<div id="lightboxModal" class="modal-overlay" style="background: rgba(7, 10, 19, 0.9);">
  <div class="modal-content" style="max-width: 90vw; width: auto; max-height: 90vh; padding: 16px; background: rgba(22, 28, 45, 0.95); backdrop-filter: blur(24px); border: 1px solid rgba(255, 255, 255, 0.1); border-radius: 12px; display: flex; flex-direction: column; align-items: center; justify-content: center; position: relative;">
    <button type="button" class="close-modal-btn" aria-label="Close modal" style="position: absolute; top: 12px; right: 16px; color: #fff; font-size: 28px; background: transparent; border: none; cursor: pointer; z-index: 10;">&times;</button>
    <div style="width: 100%; display: flex; align-items: center; justify-content: center; overflow: hidden; border-radius: 6px; background: rgba(0, 0, 0, 0.3); padding: 0px; margin-top: 24px;">
      <img id="lightboxImage" src="" alt="Full preview" style="max-width: 100%; max-height: 70vh; object-fit: contain;">
    </div>
    <div id="lightboxTitle" style="color: #fff; font-size: 14px; font-weight: 600; margin-top: 12px; text-align: center; word-break: break-all; width: 100%;"></div>
  </div>
</div>

<!-- Gallery Image Selector Modal (for Menu Items / Categories) -->
<div id="gallerySelectModal" class="modal-overlay">
  <div class="modal-content" style="max-width: 600px; width: 95%;">
    <button type="button" class="close-modal-btn" aria-label="Close modal">&times;</button>
    <div class="editor panel">
      <div class="editor-head">
        <h2>Select Image from Gallery</h2>
      </div>
      <!-- Filter & Search row in the selection modal -->
      <div style="display: flex; gap: 12px; margin-bottom: 16px; align-items: center; flex-wrap: wrap; background: rgba(255,255,255,0.02); padding: 12px; border-radius: var(--radius-sm); border: 1px solid rgba(255,255,255,0.05);">
        <select id="gallerySelectFilterCategory" class="filter-select"></select>
        <input type="search" id="gallerySelectSearch" placeholder="Search images..." style="max-width: 220px; min-height: 42px;">
      </div>
      <div id="gallerySelectGrid" style="display: grid; grid-template-columns: repeat(auto-fill, minmax(110px, 1fr)); gap: 12px; max-height: 320px; overflow-y: auto; padding: 4px; border: 1px solid rgba(255,255,255,0.08); border-radius: var(--radius-sm); background: rgba(0,0,0,0.15);">
        <!-- Gallery images will be rendered here dynamically -->
      </div>
    </div>
  </div>
</div>
