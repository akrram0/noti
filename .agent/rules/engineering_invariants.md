# NOTI ENGINEERING INVARIANTS & SYSTEM LEDGER

This document defines the permanent engineering invariants, frozen UI/architectural contracts, and the historical bug ledger for the Noti codebase. All automated agents and human developers must review and adhere strictly to these rules before proposing or making changes.

---

## 1. Frozen UI & Design Invariants (MANDATORY CHECKS)

### 0% Surface Opacity & Background Transparency
- In Liquid-Glass and Acrylic modes where surface opacity is dialed down to 0%, canvas text boxes, block containers, and rented controls must **NEVER** have opaque solid backgrounds (such as `#FFF8F8FC`).
- **DirectWrite Light Mode Caret Fix:**
  - Setting `TextControlBackground` to pure transparent (`#00000000`) causes DirectWrite's background luminance calculation to evaluate as pure black (luminance 0.0), automatically flipping the native text caret to invisible White in Light Mode.
  - Fix: In Light Mode, set `_cachedInputBgBrush` and `TextControlBackground` to near-zero alpha white: `#01FFFFFF` (`ColorHelper.FromArgb(1, 255, 255, 255)`).
  - To human eyes, `#01FFFFFF` is 99.6% transparent (zero solid strips, underlying desktop glass/acrylic shines through completely).
  - To DirectWrite luminance calculations, RGB is (255, 255, 255) (pure white), cleanly forcing the native text caret to near-black / charcoal (`#1C1C1E`).
  - In Dark Mode, use `Colors.Transparent`.

### Caret Synchronization & Non-Interference
- Both the native stationary blinking caret and the animated `SmoothCaretOverlayBorder` must bind dynamically to `{ThemeResource TextFillColorPrimaryBrush}` (`#1C1C1E` in Light Mode, `#F5F5F7` in Dark Mode).
- `SmoothCaretOverlayBorder` must **ALWAYS** have `IsHitTestVisible="False"` so it never intercepts mouse clicks, text selection, or drag gestures.
- The overlay caret must fade out smoothly when stationary so the native WinUI blinking caret provides standard accessibility and editing cues.

### Typography Hierarchy (Strict Boundaries)
- `FontFamily="Cascadia Mono"` (or monospace font stack) is **STRICTLY ISOLATED** to Code Blocks (```) and Inline Code (`code`).
- Paragraphs, bullet lists, numbered lists, and checklists must **ALWAYS** use `Segoe UI Variable Text` (Regular 400, line-height 1.6).
- Headings (H1, H2, H3) must **ALWAYS** use `Segoe UI Variable Display` (SemiBold 600).
- Never allow monospace fonts to bleed into standard paragraphs or list items.

### Layout & Shell Invariants
- The floating sidebar must strictly retain `CornerRadius="12"`, concentric border geometry, and its multi-stop soft elevation shadow.
- Window dragging via `WM_NCLBUTTONDOWN` on empty chrome spaces must never be obstructed by hit-testable transparent panels or unhandled pointer captures.
- Live canvas padding must respect the 8pt grid (`Padding="48,24,48,32"`).

### Media & Image Rendering
- Relative image paths (such as `<img src="Noti.App/Assets/Noti.png" ... />` or `![alt](assets/pic.png)`) must resolve against `Path.GetDirectoryName(activeNotePath)` as primary candidate via `ViewModel.ActiveNoteFilePath`.
- URLs ending in `.svg` or containing `shields.io` / `badgen.net` must use WinUI 3 `SvgImageSource` configured with `RasterizeSize` at 2x target size for crisp high-DPI vector rendering.
- Fallback for shields.io SVGs must attempt shields raster PNG (`https://raster.shields.io/...`) before falling back to text chips.
- Linked badges and images (`[![badge](imgUrl)](linkUrl)`) must wire `PointerPressed` to launch `linkUrl` in the user's default browser via `Launcher.LaunchUriAsync`.

---

## 2. Core Architecture & Storage Invariants

### Atomic File Writes
- **NEVER** write directly to target `.md` files or settings files.
- Always stage content into a sibling temporary file: `.{Guid:N}.tmp`.
- Commit atomically via `File.Move(tempPath, targetPath, overwrite: true)` inside a `finally` block to ensure zero file corruption on crashes, sudden power loss, or OS restarts.

### Crash Logging & Diagnostic Resilience
- Global unhandled exceptions (`AppDomain.CurrentDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`, `XamlRoot.UnhandledException`) must pipe to `%LOCALAPPDATA%\Noti\crash.log` with timestamp, stack trace, and inner exceptions.
- The app must avoid crashing silently without leaving a diagnostic footprint.

### Zero Automated Polling
- **NEVER** run background PowerShell timers, curl loops, or CI pollers.
- Builds and CI runs are tested manually by the developer or triggered via standard release pipelines.

---

## 3. Mandatory Workflow for Future Edits

Before writing any code or modifying XAML/C#:
1. **Read** `.agent/rules/engineering_invariants.md` and `.agent/rules/design.md`.
2. **Verify** that proposed changes do NOT violate any frozen invariant (e.g. transparent surface backgrounds, sidebar 12px radius, typography boundaries).
3. **Decouple** editor-specific controls from global `App.xaml` resources whenever local scopes apply, ensuring shared styles do not introduce unintended side effects.
4. **Update Ledger:** After resolving any bug or regression, append a new numbered entry to the "Historical Bug & Fix Ledger" section below detailing the symptom, root cause, and the invariant that prevents recurrence.

---

## 4. Historical Bug & Fix Ledger

### 1. Light Mode Caret White Invisibility vs. 0% Opacity Solid White Strips
- **Symptom:** Setting `TextControlBackground` to `Transparent` (`#00000000`) caused DirectWrite's background luminance calculation to evaluate as 0 (black), inverting the native blinking caret to invisible White on light backgrounds. Conversely, hardcoding a solid white background (`#FFF8F8FC`) broke 0% opacity acrylic mode by drawing solid white rectangular strips behind text blocks.
- **Root Cause:** WinUI 3 TextBox uses DirectWrite for font rasterization and caret painting. When alpha is 0, DirectWrite falls back to black luminance.
- **Invariant & Fix:** Set `_cachedInputBgBrush` and `TextControlBackground` to near-zero alpha white `#01FFFFFF` in Light Mode. DirectWrite luminance evaluates RGB (255, 255, 255) to 1.0 (white background) and paints the caret in charcoal `#1C1C1E`, while human vision perceives 99.6% transparency with zero solid strips.

### 2. Typography Cascadia Mono Bleeding into Lists & Body
- **Symptom:** Paragraphs, bullet lists, and numbered lists in Live Mode rendered in `Cascadia Mono`, giving the editor a terminal-like appearance instead of clean Apple/Fluent typography.
- **Root Cause:** LiveBlock typography factory inherited monospace font settings or applied monospace fonts globally rather than restricting them to Code Blocks.
- **Invariant & Fix:** `FontFamily="Cascadia Mono"` is strictly restricted to `LiveBlockKind.CodeBlock` and inline code spans. All Paragraphs, Bullet Lists, and Checklists must explicitly use `Segoe UI Variable Text` (Regular 400). Headings use `Segoe UI Variable Display` (SemiBold 600).

### 3. HTML `<img>` and Shields.io SVG Badge Rendering
- **Symptom:** Badges like `[![Latest Release](https://img.shields.io/...)](...)` rendered as split gray text chips or dumped raw markdown text, and HTML tags like `<img src="Noti.App/Assets/Noti.png" ... />` failed to resolve and displayed missing placeholders.
- **Root Cause:** Relative image paths only checked `vaultRootPath` and `AppContext.BaseDirectory`, failing when notes were loaded from outside the vault root. Shields.io SVGs failed in `SvgImageSource` because `RasterizeSize` was omitted, or because Direct2D SVG parser encountered unsupported gradient/filter elements.
- **Invariant & Fix:** Pass `ViewModel.ActiveNoteFilePath` into `TryResolveImageUri` to resolve relative paths against `Path.GetDirectoryName(activeNotePath)`. For SVG/Shields.io badges, configure `SvgImageSource.RasterizeSize` at 2x resolution, add fallback to shields raster PNG (`https://raster.shields.io/...`) on `OpenFailed`, and wire `PointerPressed` to launch linked URLs.

### 4. Enter Key Block Splitting & Checklist Mid-Line Mutation
- **Symptom:** Pressing `Enter` in the Live WYSIWYG editor failed to split the line or advance to a new block. Typing `[ ]` or `- [ ]` mid-sentence corrupted block layout, displaced text, and broke focus.
- **Root Cause:** Enter key handling did not capture the caret index to split the block's text into `headText` and `tailText`. Checklist auto-conversion triggered on any occurrence of `[ ]` regardless of position.
- **Invariant & Fix:** Handle `Enter` in `PreviewKeyDown` by splitting block content at `SelectionStart` into two blocks (`headText` in current block, `tailText` in newly created paragraph below), dispatching focus to the new block at index 0, and setting `e.Handled = true`. Checklist conversion is anchored strictly to line start (`^- \[[ xX]?\] $` or `^\[[ xX]?\] $` at index 0). `Shift+Enter` preserves soft newlines without splitting. Pressing Enter on an empty checklist item reverts it to a normal paragraph.

### 5. GitHub API Rate Limits (4-Hour Cooldown)
- **Symptom:** Frequent app launches or background checks hit GitHub REST API rate limits (HTTP 403 Forbidden: "API rate limit exceeded"), causing update failures and console errors.
- **Root Cause:** Unauthenticated GitHub API requests allow only 60 calls per hour per IP address. Checking updates on every launch rapidly depleted this quota.
- **Invariant & Fix:** Enforce a persistent 4-hour cooldown (`LastUpdateCheckUtc`) stored in `AppSettings`. Do not check the GitHub API if fewer than 4 hours have elapsed since the last check, unless the user explicitly clicks "Check for Updates" in Settings. Respect `X-RateLimit-Reset` and `retry-after` response headers. Never poll in automated background loops.
