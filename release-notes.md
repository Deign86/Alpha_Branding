## Alpha Premier Property Branding Studio v2.0.0

Native .NET 8 WPF desktop application with an Authenticode code-signed self-contained win-x64 installer.

### Major Features & Changes
- **Interactive Image-Editing & Cropping Studio**:
  - Pan photos freely within the branding canvas using mouse or touch/pointer dragging.
  - Smooth zoom controls: slider (`0.2x` to `4.0x`), `+` / `−` step buttons, and mouse wheel support.
  - Live branding template overlay layered over the photo so you see the exact final branded output before applying.
  - Quick action presets: **Fit to Frame** (shows 100% of the image without edge clipping), **Fill Frame**, **Rotate 90°**, and **Reset Crop**.
  - Dual-slot editing for 2-up portrait pairs with easy switching between left and right photos.
- **Flexible Image Layout Options**:
  - Global layout controls: **Combine Images** (pairs portrait photos into 6:5 frames) vs. **Keep Images Separate** (brands each photo individually).
  - Per-image **Solo / Combine** toggle buttons allowing mixed layouts (e.g. combine two exterior photos while keeping an interior shot solo).
- **Post-Branding In-Place Editing**:
  - Edit any generated branded asset directly from the results gallery or the preview window without restarting the session.
  - In-place re-rendering updates the preview and export output immediately while preserving all branding watermarks and other batch items.
- **Excessive Crop Prevention**:
  - Letterbox framing onto the studio's dark surface (`#121212`) ensures wide or tall photos are never aggressively cropped unless desired.
- **Per-Image Crop Management & Persistence**:
  - Visual `CROPPED` badges on adjusted media.
  - **Reset All Crops** buttons for both staged media and generated outputs.
  - Staged crop adjustments persist across file list changes and session navigation.

### Installation
Download `Alpha.Branding.Setup.exe` to install per-user without administrator access.
Run with `--uninstall` to remove the application and its shortcuts.

