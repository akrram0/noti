<div align="center">

<img src="Noti.App/Assets/Noti.png" width="112" height="112" alt="Noti App Icon" />

# Noti ✨

**the minimalist windows 11 notes app that actually has aura.**  
*zero bloat. pure liquid glass vibes. local markdown files you actually own, no cap.*

[![Latest Release](https://img.shields.io/github/v/release/akrram0/noti?style=for-the-badge&color=0A84FF&label=latest%20drop)](https://github.com/akrram0/noti/releases/latest)
[![Platform](https://img.shields.io/badge/Windows%2011-WinUI%203%20%2B%20.NET%208-18181B?style=for-the-badge&logo=windows11)](https://github.com/akrram0/noti)

</div>

---

## why noti hits different 💅

most note apps are either doing *way* too much (300MB electron RAM eaters 💀) or look like Windows 95 (`notepad.exe` we love u but retire pls). **Noti** is built native on WinUI 3 so it opens in 0.1s, sips memory, and looks ridiculously clean on your desktop.

- **🧊 Blurry Glass / Mica Aesthetic** — choose between **Blur** (Desktop Acrylic) or **Mica Alt**, and dial the tint opacity slider all the way down to `0%` if you want full see-through glass.
- **⚡ `/` Slash Menu (zero markdown brainrot required)** — just type `/` anywhere to drop in H1/H2/H3 headings, checklists, callout quotes, code blocks, or tables without memorizing syntax.
- **✨ Floating Pill Toolbar** — highlight any text and boom: a sleek little pill pops up for **Bold**, *Italic*, ~~Strikethrough~~, `Code`, and Links.
- **📊 Interactive Visual Tables** — drop a table from `/` and edit cells directly, press `Tab` to zoom across rows, drag column borders to resize, or hover the edges to hit `+` for new rows/columns.
- **🔗 `@` Smart Note Mentions** — type `@` to link another note on the fly, then click the pill (or `Ctrl + Click`) to teleport straight to it.
- **🎨 Color-Coded Vibes & Nested Folders** — hover any note or folder to slap a color dot on it, and nest folders inside folders (`+` subfolder button right on hover).
- **🚀 Raycast-Style Command Palette (`Ctrl + K`)** — jump to any note, toggle themes, or export `.md` without touching your mouse.
- **🌳 ASCII Tree Generator (`Shift + Enter`)** — build clean `├─` / `└──` file trees automatically while typing.
- **💾 100% Local `.md` Files** — stored right in your Documents (`Noti`). No cloud lock-in, no subscription, no weird proprietary database.

---

## get it rn (takes 5 seconds) ⚡

### option 1: the 1-liner terminal flex (PowerShell)
paste this into PowerShell and let it cook:

```powershell
irm https://raw.githubusercontent.com/akrram0/noti/main/install.ps1 | iex
```

### option 2: manual installer
grab **`Noti-Setup-win-x64.exe`** straight from the [**Latest Release**](https://github.com/akrram0/noti/releases/latest).

*(built-in auto-updater included — whenever a new build drops, Noti lets you update in one click).*

---

## cheat code shortcuts ⌨️

| Shortcut | What it does |
| :--- | :--- |
| `Ctrl + K` | Open the Command Palette (search notes + run actions) |
| `/` | Open the visual Slash Block Menu at your cursor |
| `@` | Link / mention another note |
| `Ctrl + N` | Cook up a fresh note |
| `Ctrl + Shift + N` | New folder |
| `Ctrl + B` / `Ctrl + I` | **Bold** / *Italic* |
| `Ctrl + L` | Toggle `- [ ]` checklist on current line |
| `Shift + Enter` | Auto-continue `├─` / `└──` tree branch |
| `Ctrl + Mouse Wheel` | Zoom text in / out (`Ctrl + 0` to reset) |

---

## tech stack (for the nerds 🤓)

- **UI**: WinUI 3 (`WindowsAppSDK 1.6`) + native DWM `MicaBackdrop` & `DesktopAcrylicBackdrop`
- **Runtime**: `.NET 8` (`win-x64`, unpackaged fast launch)
- **Architecture**: Clean `MVVM` (`Noti.Core`, `Noti.Storage`, `Noti.App`, `Noti.Tests`)
- **CI/CD**: GitHub Actions + Inno Setup (auto-tags every push as `v1.0.<run>` + updates rolling `latest`)

---

<div align="center">
  <sub>built with immaculate vibes ✨</sub>
</div>
