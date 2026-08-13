const photosInput = document.getElementById('photosInput');
const processBtn = document.getElementById('processBtn');
const downloadAllBtn = document.getElementById('downloadAllBtn');
const gallery = document.getElementById('gallery');
const frameStatus = document.getElementById('frameStatus');

// Path to folder asset
const FRAME_PATH = 'img/alpha_branding.png';

// Target resolution
const TARGET_WIDTH = 1000;
const TARGET_HEIGHT = 1000;

let loadedFrame = null;
let processedImages = [];

// Automatically load the branding frame from folder on startup
window.addEventListener('DOMContentLoaded', () => {
  const img = new Image();
  img.onload = () => {
    loadedFrame = img;
    frameStatus.textContent = 'Branding Frame Linked (Ready)';
    frameStatus.className = 'status-badge ready';
    photosInput.disabled = false;
    checkReady();
  };
  img.onerror = () => {
    frameStatus.textContent = 'Error: assets/branding-frame.png not found.';
    frameStatus.className = 'status-badge error';
  };
  img.src = FRAME_PATH;
});

photosInput.addEventListener('change', checkReady);

function checkReady() {
  processBtn.disabled = !(loadedFrame && photosInput.files.length > 0);
}

processBtn.addEventListener('click', async () => {
  gallery.innerHTML = '';
  processedImages = [];
  downloadAllBtn.style.display = 'none';

  const files = Array.from(photosInput.files);

  for (let i = 0; i < files.length; i++) {
    const file = files[i];
    const dataUrl = await applyBranding(file, loadedFrame);
    
    processedImages.push({
      name: `branded_${file.name}`,
      dataUrl: dataUrl
    });

    renderPreviewCard(dataUrl, `branded_${file.name}`);
  }

  if (processedImages.length > 1) {
    downloadAllBtn.style.display = 'inline-block';
  }
});

function applyBranding(photoFile, frameImage) {
  return new Promise((resolve) => {
    const photo = new Image();
    photo.onload = () => {
      const canvas = document.createElement('canvas');
      const ctx = canvas.getContext('2d');

      canvas.width = TARGET_WIDTH;
      canvas.height = TARGET_HEIGHT;

      // Fit and center photo inside 1000x1000
      const scale = Math.max(TARGET_WIDTH / photo.width, TARGET_HEIGHT / photo.height);
      const x = (TARGET_WIDTH / 2) - (photo.width / 2) * scale;
      const y = (TARGET_HEIGHT / 2) - (photo.height / 2) * scale;

      // 1. Draw property image
      ctx.drawImage(photo, x, y, photo.width * scale, photo.height * scale);

      // 2. Draw branding frame from assets folder
      ctx.drawImage(frameImage, 0, 0, TARGET_WIDTH, TARGET_HEIGHT);

      resolve(canvas.toDataURL('image/jpeg', 0.92));
    };
    photo.src = URL.createObjectURL(photoFile);
  });
}

function renderPreviewCard(dataUrl, fileName) {
  const card = document.createElement('div');
  card.className = 'card';

  const img = document.createElement('img');
  img.src = dataUrl;

  const downloadLink = document.createElement('a');
  downloadLink.href = dataUrl;
  downloadLink.download = fileName;
  downloadLink.innerText = 'Download (1000x1000)';

  card.appendChild(img);
  card.appendChild(downloadLink);
  gallery.appendChild(card);
}

downloadAllBtn.addEventListener('click', () => {
  const zip = new JSZip();
  
  processedImages.forEach((img) => {
    const base64Data = img.dataUrl.replace(/^data:image\/(png|jpeg);base64,/, "");
    zip.file(img.name, base64Data, { base64: true });
  });

  zip.generateAsync({ type: 'blob' }).then((content) => {
    const link = document.createElement('a');
    link.href = URL.createObjectURL(content);
    link.download = 'branded_property_photos_1000x1000.zip';
    link.click();
  });
});