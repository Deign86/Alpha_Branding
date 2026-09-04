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

