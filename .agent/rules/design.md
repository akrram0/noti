# Noti Design System (`.agent/rules/design.md`)

Derived from the project's visual reference mockups and Apple/macOS dark-first design principles.

## 1. Color Palette (Exact Hex & RGBA Tokens)

### Dark Theme (Default)
- **Canvas / Window Background (`bg-canvas`)**: `#000000`
- **Elevated Surface / Selected Card (`bg-surface-active`)**: `#1C1C1E`
- **Hover Surface (`bg-surface-hover`)**: `#121214`
- **Input Background (`bg-input`)**: `#121212`
- **Settings Modal Surface (`bg-flyout`)**: `#161618`
- **Modal Backdrop (`bg-backdrop`)**: `rgba(0, 0, 0, 0.72)` (`#B8000000`)
- **Structural Hairline Divider (`border-subtle`)**: `#161616`
- **Input & Settings Card Border (`border-control`)**: `#262626`
- **Primary Text (`text-primary`)**: `#F5F5F7`
- **Secondary / Snippet / Status Text (`text-secondary`)**: `#8E8E93`
- **Muted / Chrome Title Text (`text-muted`)**: `#A1A1A6`

### Light Theme (Secondary via Settings Toggle)
- **Canvas / Window Background (`bg-canvas`)**: `#FFFFFF`
- **Elevated Surface / Selected Card (`bg-surface-active`)**: `#F2F2F7`
- **Hover Surface (`bg-surface-hover`)**: `#F9F9FB`
- **Input Background (`bg-input`)**: `#F2F2F7`
- **Settings Modal Surface (`bg-flyout`)**: `#F9F9FB`
- **Modal Backdrop (`bg-backdrop`)**: `rgba(0, 0, 0, 0.44)` (`#70000000`)
- **Structural Hairline Divider (`border-subtle`)**: `#E5E5EA`
- **Input & Settings Card Border (`border-control`)**: `#D1D1D6`
- **Primary Text (`text-primary`)**: `#1C1C1E`
- **Secondary / Snippet / Status Text (`text-secondary`)**: `#6C6C70`
- **Muted / Chrome Title Text (`text-muted`)**: `#8E8E93`

### Liquid-Glass / Translucent Interactive Control Tokens
- **Glass Fill Default (`glass-fill`)**: `rgba(255, 255, 255, 0.08)` (Dark) / `rgba(0, 0, 0, 0.05)` (Light)
- **Glass Fill Hover (`glass-fill-hover`)**: `rgba(255, 255, 255, 0.14)` (Dark) / `rgba(0, 0, 0, 0.09)` (Light)
- **Glass Fill Pressed (`glass-fill-pressed`)**: `rgba(255, 255, 255, 0.05)` (Dark) / `rgba(0, 0, 0, 0.12)` (Light)
- **Glass Rim Border (`glass-border`)**: `rgba(255, 255, 255, 0.22)` (Dark) / `rgba(0, 0, 0, 0.14)` (Light)
- **Toggle Track Fill (`toggle-track`)**: `rgba(255, 255, 255, 0.16)` (Dark) / `rgba(0, 0, 0, 0.10)` (Light)
- **Toggle Thumb Fill (`toggle-thumb`)**: `rgba(255, 255, 255, 0.22)` (Dark) / `#FFFFFF` (Light)

---

## 2. Spacing Scale (8pt Grid)
All padding, margins, and layout gaps must strictly use values from this scale:
- `4px` — Tight inline gaps (title-to-snippet vertical stack gap, toggle internal padding)
- `8px` — Compact element spacing (list item gaps, icon-to-label gap)
- `16px` — Standard container padding (sidebar outer padding, card internal padding)
- `24px` — Modal internal padding and section separation
- `32px` — Editor & preview vertical padding
- `48px` — Titlebar height, status bar height, and editor/preview horizontal padding
- `64px` — Large structural offsets / mode switcher segment width

---

## 3. Border-Radius Tiers & Usage Rules
Never apply one uniform border-radius across unrelated component types:
- **`sm` (`6px`–`8px`)**: Compact inline chips, folder badge, action pills (`Import .md`, `Export .md`), subtle icon buttons.
- **`md` (`12px`)**: Note list item selection cards, settings section containers, code/tree block cards, floating word-pop typing badge.
- **`lg` (`16px`)**: Full Settings Modal card (`600×520px`) and outer window frame rounding.
- **`full` (`9999px` / `18px` on `36px` controls, `16px` on `32px` mode switcher)**: Search pill input, circular liquid-glass icon buttons, `Write | Split | View` mode switcher pill, and toggle switches.

---

## 4. Typography Scale
- **Font Family**: `Segoe UI Variable Text`, `-apple-system`, `SF Pro Text`, `Inter` (strictly max 2 weights per screen: Regular `400` and SemiBold `600`; `Cascadia Mono` / `Consolas` for code and tree blocks).
- **Hierarchy Levels**:
  - **Editor & Preview Body (`16px` default, configurable `13–22px`, Regular `400`, line-height `1.65`)**: Primary writing canvas and rendered Markdown body.
  - **Settings Modal Header (`18px`, SemiBold `600`)**: Modal title.
  - **Note Card Title, Folder Name & Settings Option Label (`13–14px`, SemiBold `600`, line-height `20px`)**: Sidebar file tree items and setting rows.
  - **Snippet, Section Header & Status Metadata (`11–12px`, Regular `400` / SemiBold `600`, line-height `16px`)**: Sidebar note snippet preview, section headers, note counter, word counter, and autosave indicator.

---

## 5. Layout, Composition & Motion Hierarchy
1. **Primary Action & Dominant Visual Weight**: The **Main Canvas** occupies `1*` (`Write`, `Split` 50/50, or `View` 100% rendered Markdown) with `32px` vertical / `48px` horizontal padding and zero borders.
2. **Secondary Navigation (Nested File Tree Sidebar)**: `280px` width when expanded, smoothly animated (`Width` `280` $\leftrightarrow$ `0`, `TranslateX` `0` $\leftrightarrow$ `-32`, `Opacity` `1` $\leftrightarrow$ `0` over `220ms` `CubicEase` `EaseOut`). Supports nested folders (`14px` per depth level), collapsible folder chevrons, Pin-to-top, and Delete.
3. **Top Chrome Bar (`48px`)**: Left sidebar toggle + `"Noti"` + active folder badge; Center `Write | Split | View` switcher with a `64px` sliding pill indicator; Right `Import .md` and `Export .md` pills.
4. **Full Settings Modal (`600×520px` centered over backdrop)**: Smooth fade + scale (`0.92` $\leftrightarrow$ `1.0`) + vertical glide (`16px` $\leftrightarrow$ `0`) entrance and exit.
5. **Micro-Interactions**: All buttons and list items animate scale on hover (`1.04–1.06`) and press (`0.90–0.93`). Typing in the editor triggers a subtle vertical micro-spring and floating word-pop chip (`0.86` $\rightarrow$ `1.0` scale).
