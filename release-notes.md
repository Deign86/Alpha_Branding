## Alpha Premier Property Branding Studio v2.4.0

### New Features
- **CLEAR ALL**: empty the selected media in one click (asks first only when crops, pen marks or pairs would be lost); the branded results view gets its own CLEAR ALL that offers to export before clearing.
- **Drop to add**: dropping files onto the selected media now adds them instead of replacing the selection; SELECT MEDIA asks before replacing edited items.
- **Crop editor keyboard panning**: focus the photo and use the arrow keys (Shift for bigger steps); CLEAR MARKS can be undone with UNDO or `Ctrl+Z`.

### Fixes & Improvements
- **Consistent controls**: PAN/PEN, LEFT/RIGHT PHOTO and COMBINE IMAGES/KEEP SEPARATE use one dark-gold segmented style (no more default Windows chrome); the active choice is gold and bold, and keyboard focus shows a gold ring.
- **Crop editor**: header names the photo being edited; toolbar grouped into mode/pen, zoom and framing; labelled pen SIZE; rule-of-thirds guides fit the pair slots.
- **Pen marks are kept** when a crop is reset.
- **No false notice** that branded photos are a "new selection" right after APPLY BRANDING; skipped files are named in the status line.
- **Layout & accessibility**: filename prefix no longer clipped; staged card hover/drop highlights work; pairing shows the partner's file name; screen readers announce status results; at 200% scaling the page scrolls instead of squeezing the gallery.

## Alpha Premier Property Branding Studio v2.3.0

### New Features
- **PEN redaction tool**: crop editor gains a `PEN` mode with freehand opaque strokes, brush-size slider, Undo/Clear per photo slot, and `Esc`/`Ctrl+Z` shortcuts; marks bake into branded exports and survive rebrand, zoom, pan, and rotation.
- **Manual pair matching**: drag one staged photo onto another to pair them explicitly (fixes wrong auto-matches), plus per-card `PAIR`/`UNPAIR` buttons; per-photo `SOLO` choice unchanged; explicit pairs flow through Apply and Rebrand.

## Alpha Premier Property Branding Studio v2.2.2

### Fixes & Improvements
- **HEIC/HEIF photo input**: drag-drop + file dialog accept `.heic`/`.heif`; thumbnails, crop editor, and branding pipeline transcode via bundled Magick.NET (JPEG output unchanged).
- **Scope note**: templates untouched; corrupt HEIC surfaces a meaningful error instead of silent drop.

## Alpha Premier Property Branding Studio v2.2.1

### Fixes & Improvements
- **UI & Automation**: Removed redundant duplicate `APPLY BRANDING` button (`StagedApplyButton`) from the staged media gallery header, ensuring a single canonical `ApplyBrandingButton` in the main action bar and eliminating UI automation / accessibility tree ambiguities.
- **Skill System**: Integrated `i-have-adhd` skill repo-wide (`.agents/skills/i-have-adhd/` and `AGENTS.md`) for concise, ADHD-friendly agent output formatting.

## Alpha Premier Property Branding Studio v2.2.0

High-DPI responsive layout: the app now fits and stays fully usable on displays scaled beyond 100% (125%/150%/175%/200%), verified with rendered audit screenshots in `scaling_audit_issues/`.

### Fixes & Changes
- **Responsive control workspace**: selection/template groups wrap to a second line on narrow windows instead of clipping APPLY/EXPORT; CHECK FOR UPDATES moved into the action row (same automation IDs).
- **No more clipped buttons**: staged/results/status/session-notice headers trim with ellipsis while keeping every action button reachable.
- **Lower minimum window sizes** so windows physically fit scaled screens: Main 840x480, Crop Editor 700x520, Preview 720x480, Update dialog min-height 460.
- **Crop Editor preset bar** wraps to a 2x2 grid on narrow windows (fixes FIT/FILL/ROTATE/RESET overlap); footer hint and Preview footer hint wrap instead of colliding.
- **Test hardening**: exit-confirmation dialog no longer hangs UI automation (safe-dismiss teardown) or the in-process scaling screenshot generator (suppress-confirmation hook).

## Alpha Premier Property Branding Studio v2.1.0

Native .NET 8 WPF desktop application with an Authenticode code-signed self-contained win-x64 installer.

### Major Features & UI/UX Improvements
- **Interactive Drag & Drop Visual Reaction**:
  - Empty state drop box illuminates with a 2px Gold accent border (`#C5A059`), warm surface elevation (`#251E14`), and soft golden glow (`DropShadowEffect`) when hovering files over the app.
  - Phoenix emblem shifts to 100% opacity with an illuminated center callout badge (`DROP VIDEOS & PHOTOS HERE TO IMPORT`).
  - Active drag-over state banner on staged and results galleries for seamless additional file imports.
  - Hierarchical drag-depth tracking prevents flickering when mouse crosses nested UI controls.
- **Project-Wide UI/UX Polish (Applied from `jakubkrehel/skills`)**:
  - **Accessibility**: Custom dark-gold accessible slider styling with high-contrast keyboard focus borders, 32×32px minimum touch target floors, and screen-reader `AutomationProperties.Name` coverage across all controls.
  - **Typography**: Display formatting mode, ClearType rendering, and tabular numeral alignment (`Typography.NumeralAlignment="Tabular"`) across all numbers, counters, timestamps, and coordinates.
  - **Colors & Contrast**: Increased `TextMuted` contrast to >5.5:1 (WCAG AA compliant) and distinct crimson destructive action buttons.
  - **Surface Depth & Motion**: Concentric corner radii, 1px subtle image depth outlines, and layered elevation shadows.

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

