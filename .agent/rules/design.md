# Noti Design System (`.agent/rules/design.md`)

Derived from the project's two visual reference mockups (Main Editor view and Settings popover view) and Apple/macOS dark-first design principles.

## 1. Color Palette (Exact Hex & RGBA Tokens)

### Dark Theme (Default)
- **Canvas / Window Background (`bg-canvas`)**: `#000000`
- **Elevated Surface / Selected Card (`bg-surface-active`)**: `#1C1C1E`
- **Hover Surface (`bg-surface-hover`)**: `#121214`
- **Input Background (`bg-input`)**: `#121212`
- **Settings Card Surface (`bg-flyout`)**: `#161618`
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
- **Settings Card Surface (`bg-flyout`)**: `#F9F9FB`
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
- **Backdrop Blur**: `blur(20px)` (`1px` specular rim stroke)

---

## 2. Spacing Scale (8pt Grid)
All padding, margins, and layout gaps must strictly use values from this scale:
- `4px` — Tight inline gaps (title-to-snippet vertical stack gap, toggle internal padding)
- `8px` — Compact element spacing (list item gaps, icon-to-label gap, settings card to bottom bar gap)
- `16px` — Standard container padding (sidebar outer padding, card internal padding, settings panel padding)
- `24px` — Section separation
- `32px` — Editor vertical padding
- `48px` — Titlebar height and editor horizontal padding
- `64px` — Large structural offsets

---

## 3. Border-Radius Tiers & Usage Rules
Never apply one uniform border-radius across unrelated component types:
- **`sm` (`6px`)**: Compact inline chips, keyboard shortcut badges.
- **`md` (`12px`)**: Note list item selection cards (`Welcome` active state card).
- **`lg` (`16px`)**: Inline Settings panel card above the bottom bar (`Settings` / `Dark mode` container) and outer window frame rounding.
- **`full` (`9999px` / `18px` on `36px` controls, `14px` on `28px` toggle)**: Search pill input, circular liquid-glass icon buttons (Sidebar toggle, `+` New Note button, Settings gear button), and the Dark mode pill toggle switch + circular thumb.

---

## 4. Typography Scale
- **Font Family**: `Segoe UI Variable Text`, `-apple-system`, `SF Pro Text`, `Inter` (strictly max 2 weights per screen: Regular `400` and SemiBold `600`).
- **Hierarchy Levels**:
  - **Editor Body (`16px`, Regular `400`, line-height `28px`)**: Primary writing canvas text.
  - **Note Card Title & Settings Option Label (`14px`, SemiBold `600`, line-height `20px`)**: Sidebar note title (`Welcome`, `Shopping`, `Ideas`) and `"Dark mode"` label.
  - **Window Title / Search Input (`14px`, Regular `400` / SemiBold `600`, line-height `20px`)**: Top bar `"Noti"` label and search box text.
  - **Snippet, Section Header & Status Metadata (`12px`, Regular `400` / SemiBold `600`, line-height `16px`)**: Sidebar single-line note snippet preview, `"Settings"` card header, `"3 notes"` counter, `"17 words"` counter, and `"Saved"` status indicator.

---

## 5. Layout & Composition Hierarchy
1. **Primary Action & Dominant Visual Weight**: The **Main Editor** canvas occupies `1*` (all remaining horizontal width, ~73% of a `1040×640` window) with generous `32px` top / `48px` horizontal breathing room and zero borders.
2. **Secondary Navigation**: Collapsible **Sidebar** (`280px` fixed width when expanded, `0px` when collapsed), separated by a `1px` hairline (`#161616`).
3. **Inline Settings Card**: Docked directly above the sidebar bottom bar (`16px` horizontal margin, `8px` bottom margin, `16px` internal padding, `lg` `16px` corner radius, `#161618` fill, `#262626` `1px` border), containing the `"Settings"` header (`12px`) and `"Dark mode"` row (`14px`) with a `48×28px` pill toggle switch (`full` radius).
4. **Top Chrome Bar**: `48px` height with left-aligned liquid-glass sidebar toggle (`36×36px`) + `"Noti"` label, separated by a `1px` bottom hairline (`#161616`).
5. **Bottom Status Row**: `48px` height aligned across both panes — left pane shows the `36×36px` liquid-glass Settings gear button + note count; right pane shows word count (left) and autosave status (right).
