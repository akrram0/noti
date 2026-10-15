# NOTI ENGINEERING INVARIANTS & SYSTEM LEDGER

This document defines the permanent engineering invariants, frozen UI/architectural contracts, and the historical bug ledger for the Noti codebase. All automated agents and human developers must review and adhere strictly to these rules before proposing or making changes.

---

## 1. Frozen UI & Design Invariants (MANDATORY CHECKS)

### 0% Surface Opacity & Background Transparency
- In Liquid-Glass and Acrylic modes where surface opacity is dialed down to 0%, canvas text boxes, block containers, and rented controls must **NEVER** have opaque solid backgrounds (such as `#FFF8F8FC`).
- All backgrounds (`TextControlBackground`, `_cachedInputBgBrush`, `cellGrid`, live editor panel) are set to pure `Transparent` (`#00000000`) across all themes.
- Under Premultiplied Alpha in DirectComposition, near-zero alpha values like `#01FFFFFF` premultiply to near-zero luminance (0.0039 ~ 0.0 black), which fails to flip DirectWrite's contrast heuristic and contaminates pixel composition. Alpha manipulation tricks are strictly forbidden.
- `BorderElement` and `VisualStateManager.VisualStateGroups` must be completely omitted from `BorderlessTextBoxStyle` so WinUI 3's native C++ `TextBox` cannot hook or apply focus background highlights (such as `TextControlBackgroundFocused` `#1FFFFFFF`) around the focused line.
- `RequestedTheme = ElementTheme.Light` / `ElementTheme.Dark` must be explicitly enforced in code-behind down through the entire visual hierarchy (`RootGrid`, `MainWorkspaceGrid`, `LiveCanvasRootGrid`, `LiveEditorScrollViewer`, `LiveEditorPanel`, `cellGrid`, and every rented `TextBox`). This ensures `ActualTheme` resolves correctly and paints the native caret in high contrast (`#1C1C1E` in Light Mode, `#FFF5F5F7` in Dark Mode).

### Caret Synchronization & Single-Caret Invariant
- Exactly **ONE** single, crisp text caret must ever be visible on screen in both Light and Dark modes.
- The native WinUI 3 `TextBox` caret is the primary persistent blinking caret across all editor modes.
- `SmoothCaretOverlayBorder` operates strictly as a transient fluid gliding overlay when `ViewModel.IsFluidAnimationEnabled` is true (glides 85ms `CubicEase`, pulses and fades to `Opacity = 0.0` at 360ms, collapsing on completion).
- `SmoothCaretOverlayBorder` must **NEVER** blink indefinitely with `RepeatBehavior.Forever` or remain visible when stationary; doing so causes a dual/ghost caret artifact against DirectWrite's native blinking caret.
- `SmoothCaretOverlayBorder` must **ALWAYS** have `IsHitTestVisible="False"` so it never intercepts mouse clicks, text selection, or drag gestures.
- When `ViewModel.IsFluidAnimationEnabled` is true, caret repositioning glides smoothly (85ms `CubicEase`); when false, it snaps instantly without translation delay.

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
- Relative image paths (such as `<img src="Noti.App/Assets/Noti.png" ... />` or `![alt](assets/pic.png)`) must resolve against `Path.GetDirectoryName(activeNotePath)` as primary candidate via `ViewModel.ActiveNoteFilePath`, falling back to `AppContext.BaseDirectory`, `Assets\`, `vaultRootPath`, and parent directories in dev environments.
- Shields.io badge URLs (`shields.io`, `img.shields.io`) must be routed to `https://raster.shields.io/...` via `BitmapImage`. WinUI 3's `SvgImageSource` Direct2D engine (SVG 1.1 Tiny profile) discards SVG `<text>` elements completely, causing shields.io SVGs to render as empty split boxes.
- If an image cannot be resolved or fails to load, collapse the host element (`Visibility = Visibility.Collapsed; hostBorder.Child = null;`) without rendering bulky mock/placeholder pill UI elements on the canvas.
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
- **Invariant & Fix:** Pass `ViewModel.ActiveNoteFilePath` into `TryResolveImageUri` to resolve relative paths against `Path.GetDirectoryName(activeNotePath)`. For SVG/Shields.io badges, configure `SvgImageSource` with `RasterizePixelWidth` and `RasterizePixelHeight` at 2x resolution, add fallback to shields raster PNG (`https://raster.shields.io/...`) on `OpenFailed`, and wire `PointerPressed` to launch linked URLs.

### 4. Enter Key Block Splitting & Checklist Mid-Line Mutation
- **Symptom:** Pressing `Enter` in the Live WYSIWYG editor failed to split the line or advance to a new block. Typing `[ ]` or `- [ ]` mid-sentence corrupted block layout, displaced text, and broke focus.
- **Root Cause:** Enter key handling did not capture the caret index to split the block's text into `headText` and `tailText`. Checklist auto-conversion triggered on any occurrence of `[ ]` regardless of position.
- **Invariant & Fix:** Handle `Enter` in `PreviewKeyDown` by splitting block content at `SelectionStart` into two blocks (`headText` in current block, `tailText` in newly created paragraph below), dispatching focus to the new block at index 0, and setting `e.Handled = true`. Checklist conversion is anchored strictly to line start (`^- \[[ xX]?\] $` or `^\[[ xX]?\] $` at index 0). `Shift+Enter` preserves soft newlines without splitting. Pressing Enter on an empty checklist item reverts it to a normal paragraph.

### 5. GitHub API Rate Limits (4-Hour Cooldown)
- **Symptom:** Frequent app launches or background checks hit GitHub REST API rate limits (HTTP 403 Forbidden: "API rate limit exceeded"), causing update failures and console errors.
- **Root Cause:** Unauthenticated GitHub API requests allow only 60 calls per hour per IP address. Checking updates on every launch rapidly depleted this quota.
- **Invariant & Fix:** Enforce a persistent 4-hour cooldown (`LastUpdateCheckUtc`) stored in `AppSettings`. Do not check the GitHub API if fewer than 4 hours have elapsed since the last check, unless the user explicitly clicks "Check for Updates" in Settings. Respect `X-RateLimit-Reset` and `retry-after` response headers. Never poll in automated background loops.

### 6. Shields.io Direct2D Text Loss, Missing App Assets, & Dual Caret Elimination
- **Symptom:**
  1. Shields.io badges rendered as artificial two-tone rectangles (dark gray block attached to bright blue `#0A84FF` block) with no text.
  2. `<img src="Noti.App/Assets/Noti.png" ... />` rendered as a bulky gray fallback pill labeled `[icon] Noti App Icon (Noti.App/Assets/Noti.png)`.
  3. Clicking into text lines in Light Mode produced dual/ghost blinking carets (a white native caret rendered alongside a black custom overlay caret).
- **Root Cause:**
  1. WinUI 3's `SvgImageSource` relies on Direct2D SVG 1.1 Tiny profile which completely omits SVG `<text>` elements. `img.shields.io` badges have `<rect>` backgrounds and `<text>` labels, so Direct2D parsed the SVG successfully but silently dropped the text, making the badge appear like an empty hand-drawn mock.
  2. `Noti.App.csproj` had hardcoded `<Content Include="Assets\Noti.ico">`, leaving `Noti.png` out of published output, and resolution only checked note-relative paths. On failure, `CreateFallbackPlaceholder()` rendered a bulky styled pill.
  3. DirectWrite renders an unsuppressed native caret in `TextBox`. When `TextControlBackground` was set to pure `Transparent`, DirectWrite inverted the native caret to white. At the same time, `SmoothCaretOverlayBorder` was configured as a permanent, repeating black blinking overlay (`RepeatBehavior.Forever`), causing both the white native caret and black overlay caret to render simultaneously out of sync.
- **Invariant & Fix:**
  1. Automatically route shields.io requests (`img.shields.io` and `shields.io`) to official rasterized PNGs (`https://raster.shields.io/...`) using WinUI 3 `BitmapImage`.
  2. Package all assets via `<Content Include="Assets\Noti.ico">` and `<Content Include="Assets\Noti.png">`, expand `TryResolveImageUri` to search `AppContext.BaseDirectory`, `Assets\`, `vaultRootPath`, and directory traversal in dev mode, and collapse unresolvable/failed images (`Visibility = Visibility.Collapsed; hostBorder.Child = null;`) without generating bulky placeholder pills.
  3. Eliminate the dual-caret artifact by keeping the native `TextBox` caret as the sole persistent blinking cursor: restore `SmoothCaretOverlayBorder` to a transient gliding overlay (85ms glide, 360ms pulse-fade to `Opacity = 0.0`, collapsed on completion).
 
### 7. Premultiplied Alpha Ineffectiveness, Focused Line Strip Elimination, & Explicit Visual Tree Theme Propagation
- **Symptom:** In Light Mode on a Windows machine running in Dark Mode, clicking or typing in a line (e.g. `"hhhhhhhhhh"`) resulted in a stationary white native caret, and a faint translucent white rectangular box appeared around the focused line.
- **Root Cause:**
  1. DirectComposition Premultiplied Alpha: Setting `TextControlBackground` to `#01FFFFFF` premultiplied to RGB (1, 1, 1) with luminance 0.0039 (~0.0 black), which DirectWrite evaluated as dark, keeping the native caret white.
  2. Unstripped `BorderElement` in `BorderlessTextBoxStyle`: WinUI 3's native C++ `TextBox` hooks `GetTemplateChild("BorderElement")` and animates `TextControlBackgroundFocused`. When the control hierarchy defaulted to Dark theme dictionaries, it applied `#1FFFFFFF` on focus, creating a faint translucent highlight box around the focused line.
  3. Visual Tree Theme Inheritance: When the host OS runs in Dark Mode and Noti is set to Light Mode, relying solely on `{x:Bind ViewModel.CurrentElementTheme, Mode=OneWay}` on `RootGrid` failed to propagate `ElementTheme.Light` down to dynamically materialized blocks (`cellGrid`, `tb`, `LiveEditorPanel`). As a result, `ActualTheme` resolved to `Dark`, triggering Dark theme focus visuals and white caret coloring.
- **Invariant & Fix:**
  1. Reverted all editor background brushes (`TextControlBackground`, `_cachedInputBgBrush`) to pure `Transparent` in both Light and Dark modes. Zero alpha tricks.
  2. Completely stripped `<Border x:Name="BorderElement" ... />` and `<VisualStateManager.VisualStateGroups>` from `BorderlessTextBoxStyle`. With no `BorderElement` in the template, WinUI 3 cannot render any focus highlight box.
  3. Enforced explicit `RequestedTheme = ViewModel.CurrentElementTheme` propagation in code-behind across `RootGrid`, `MainWorkspaceGrid`, `LiveCanvasRootGrid`, `LiveEditorScrollViewer`, `LiveEditorPanel`, `cellGrid`, and every rented `TextBox` (`tb`). This ensures `ActualTheme` resolves to `ElementTheme.Light`, natively painting the caret in `#1C1C1E` without background strips.
