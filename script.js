// ==========================================
// DOM ELEMENTS
// ==========================================
const photosInput = document.getElementById('photosInput');
const processBtn = document.getElementById('processBtn');
const downloadAllBtn = document.getElementById('downloadAllBtn');
const frameStatus = document.getElementById('frameStatus');

// Loader Elements
const loadingState = document.getElementById('loadingState');
const loadingText = document.getElementById('loadingText');
const progressBar = document.getElementById('progressBar');
const progressCount = document.getElementById('progressCount');

// Preview & Modal Elements
const previewSection = document.getElementById('previewSection');
const gallery = document.getElementById('gallery');
const imageModal = document.getElementById('imageModal');
const modalImage = document.getElementById('modalImage');
const closeModal = document.getElementById('closeModal');
const prevBtn = document.getElementById('prevBtn');
const nextBtn = document.getElementById('nextBtn');

// App State & Config (STRICT 1200x1000 WEBP OUTPUT)
const FRAME_SRC = 'img/alpha_branding.png'; // Path to your branding frame overlay PNG
const TARGET_WIDTH = 1200;         // Fixed width in pixels
const TARGET_HEIGHT = 1000;        // Fixed height in pixels
const WEBP_QUALITY = 0.80;         // WebP quality (80% yields tiny file sizes with crisp detail)

let processedImages = [];
let currentModalIndex = 0;
let loadedFrameImg = null;

// ==========================================
// INITIALIZATION & FRAME PRELOADING (FIXED)
// ==========================================

// Run frame preload immediately if DOM is already loaded, otherwise attach listener
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', preloadBrandingFrame);
} else {
  preloadBrandingFrame();
}

function preloadBrandingFrame() {
  if (frameStatus) {
    frameStatus.textContent = 'Loading branding frame...';
    frameStatus.className = 'loading';
  }

  const img = new Image();

  img.onload = () => {
    loadedFrameImg = img;
    if (frameStatus) {
      frameStatus.textContent = 'Branding Frame Ready';
      frameStatus.className = 'ready';
    }
    if (photosInput) photosInput.disabled = false;
  };

  img.onerror = () => {
    if (frameStatus) {
      frameStatus.textContent = `Frame Load Error (Check path: ${FRAME_SRC})`;
      frameStatus.className = 'error';
    }
    console.error(`Failed to load branding frame image from: ${FRAME_SRC}. Verify the file exists in the img/ folder and matches case sensitivity.`);
  };

  // Trigger image fetch
  img.src = FRAME_SRC;
}

// ==========================================
// FILE SELECTION & PROCESSING EVENTS
// ==========================================

if (photosInput) {
  // Process photos when files are confirmed/selected
  photosInput.addEventListener('change', async (event) => {
    const files = Array.from(event.target.files);

    if (files.length === 0) {
      return;
    }

    if (!loadedFrameImg) {
      alert(`Branding frame is not loaded yet. Please verify that '${FRAME_SRC}' exists in your project folder.`);
      photosInput.value = ''; // Reset input
      return;
    }

    showLoader();
    const total = files.length;
    updateProgress(0, total, 'Reading uploaded images...');

    // Reset gallery state
    if (gallery) gallery.innerHTML = '';
    processedImages = [];
    if (previewSection) previewSection.style.display = 'none';
    if (downloadAllBtn) downloadAllBtn.style.display = 'none';

    // Process selected files sequentially
    for (let i = 0; i < total; i++) {
      const file = files[i];
      updateProgress(i + 1, total, `Converting to WebP (1200x1000) ${i + 1} of ${total}...`);

      try {
        await processAndRenderPhoto(file);
      } catch (err) {
        console.error(`Error processing ${file.name}:`, err);
      }
    }

    // Hide loader and show results
    hideLoader();
    if (previewSection) previewSection.style.display = 'block';
    if (downloadAllBtn && processedImages.length > 0) {
      downloadAllBtn.style.display = 'inline-block';
    }
  });
}

// ==========================================
// FILE PROCESSING & WEBP CONVERSION LOGIC
// ==========================================

function processAndRenderPhoto(file) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();

    reader.onload = (e) => {
      const photoImg = new Image();

      photoImg.onload = () => {
        if (!loadedFrameImg) {
          reject('Branding frame not ready');
          return;
        }

        // Create canvas locked strictly to 1200x1000
        const canvas = document.createElement('canvas');
        const ctx = canvas.getContext('2d');

        canvas.width = TARGET_WIDTH;   // 1200px
        canvas.height = TARGET_HEIGHT; // 1000px

        // Enable high-quality scaling
        ctx.imageSmoothingEnabled = true;
        ctx.imageSmoothingQuality = 'high';

        // Layer 1: Draw photo stretched strictly to 1200x1000
        ctx.drawImage(photoImg, 0, 0, TARGET_WIDTH, TARGET_HEIGHT);

        // Layer 2: Overlay branding frame strictly to 1200x1000
        ctx.drawImage(loadedFrameImg, 0, 0, TARGET_WIDTH, TARGET_HEIGHT);

        // Convert canvas output to WEBP format
        const brandedDataUrl = canvas.toDataURL('image/webp', WEBP_QUALITY);

        // Replace existing file extension with .webp
        const baseName = file.name.substring(0, file.name.lastIndexOf('.')) || file.name;
        const webpFileName = `branded-${baseName}.webp`;

        // Store result item
        const imageItem = {
          id: processedImages.length,
          name: webpFileName,
          src: brandedDataUrl
        };
        processedImages.push(imageItem);

        // Render card preview in gallery grid
        createGalleryCard(imageItem);

        setTimeout(resolve, 50);
      };

      photoImg.onerror = () => reject('Failed to read image file');
      photoImg.src = e.target.result;
    };

    reader.onerror = () => reject('FileReader error');
    reader.readAsDataURL(file);
  });
}

function createGalleryCard(item) {
  if (!gallery) return;
  const card = document.createElement('div');
  card.className = 'card';
  card.innerHTML = `
    <img src="${item.src}" alt="${item.name}">
    <div class="card-actions">
      <button class="btn-preview" onclick="openModal(${item.id})">Preview</button>
      <a href="${item.src}" download="${item.name}" class="btn-download">Download</a>
    </div>
  `;
  gallery.appendChild(card);
}

// ==========================================
// BATCH ZIP DOWNLOAD LOGIC
// ==========================================
if (downloadAllBtn) {
  downloadAllBtn.addEventListener('click', async () => {
    if (typeof JSZip === 'undefined') {
      alert('JSZip library failed to load. Please verify your script tag for JSZip.');
      return;
    }

    const zip = new JSZip();
    const folder = zip.folder('branded_property_photos');

    processedImages.forEach((img) => {
      // Strip base64 header for WebP
      const base64Data = img.src.replace(/^data:image\/[a-z]+;base64,/, '');
      folder.file(img.name, base64Data, { base64: true });
    });

    showLoader();
    updateProgress(100, 100, 'Generating WebP ZIP archive...');

    const content = await zip.generateAsync({ type: 'blob' });
    const link = document.createElement('a');
    link.href = URL.createObjectURL(content);
    link.download = 'Branded_Property_Photos_WebP.zip';
    link.click();

    hideLoader();
  });
}

// ==========================================
// FULLSCREEN MODAL GALLERY LOGIC
// ==========================================

window.openModal = function (index) {
  currentModalIndex = index;
  updateModalImage();
  if (imageModal) imageModal.style.display = 'flex';
};

function updateModalImage() {
  if (modalImage && processedImages[currentModalIndex]) {
    modalImage.src = processedImages[currentModalIndex].src;
  }
}

if (closeModal) {
  closeModal.addEventListener('click', () => {
    if (imageModal) imageModal.style.display = 'none';
  });
}

if (prevBtn) {
  prevBtn.addEventListener('click', () => {
    if (currentModalIndex > 0) {
      currentModalIndex--;
    } else {
      currentModalIndex = processedImages.length - 1;
    }
    updateModalImage();
  });
}

if (nextBtn) {
  nextBtn.addEventListener('click', () => {
    if (currentModalIndex < processedImages.length - 1) {
      currentModalIndex++;
    } else {
      currentModalIndex = 0;
    }
    updateModalImage();
  });
}

// Keyboard Navigation for Modal
window.addEventListener('keydown', (e) => {
  if (imageModal && imageModal.style.display === 'flex') {
    if (e.key === 'Escape') imageModal.style.display = 'none';
    if (e.key === 'ArrowLeft' && prevBtn) prevBtn.click();
    if (e.key === 'ArrowRight' && nextBtn) nextBtn.click();
  }
});

// Close modal when clicking backdrop
window.addEventListener('click', (e) => {
  if (e.target === imageModal) {
    imageModal.style.display = 'none';
  }
});

// ==========================================
// LOADER HELPER FUNCTIONS
// ==========================================

function showLoader() {
  if (loadingState) loadingState.style.display = 'flex';
  if (processBtn) processBtn.disabled = true;
}

function hideLoader() {
  if (loadingState) loadingState.style.display = 'none';
  if (processBtn) processBtn.disabled = false;
}

function updateProgress(current, total, text) {
  const percent = total > 0 ? Math.round((current / total) * 100) : 0;
  if (progressBar) progressBar.style.width = `${percent}%`;
  if (progressCount) {
    progressCount.textContent = total > 0 ? `${current} / ${total} images (${percent}%)` : '0 / 0 images';
  }
  if (text && loadingText) loadingText.textContent = text;
}