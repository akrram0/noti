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

- **🧊 Floating Liquid-Glass & Mica Aesthetic** — choose between **Blur** (true per-pixel Desktop Acrylic) or **Mica Alt**, dial the surface tint opacity all the way down to `0%`, and enjoy a floating `12px` rounded sidebar with a soft elevation shadow that stays crisp over any wallpaper.
- **✍️ In-Place Live WYSIWYG Canvas + Source Mode (`Ctrl + E`)** — write directly in a clean, distraction-free Live canvas where headings, checklists, callout quotes, code blocks, tables, and images render in-place, or flip to raw Markdown **Source** mode anytime.
- **⚡ `/` Slash Menu (zero markdown brainrot required)** — type `/` anywhere to drop in `H1` / `H2` / `H3` headings, checklists, callout quotes, code blocks, interactive tables, photos, or block alignment (`Left` / `Center` / `Right`).
- **🖼️ Drag, Drop & Paste Photos** — paste from clipboard (`Ctrl + V`), drag & drop images into the window, or use `/image` to embed photos directly into your notes (automatically stored inside your vault's local `assets/` folder).
- **🌊 Smooth Caret Gliding & Fluid Word-Pop** — optional spring-animated cursor gliding and floating word-pop feedback as you type, built as a zero-interference overlay so native selection and cursor physics stay 100% intact.
- **✨ Floating Pill Toolbar** — highlight any text and a sleek glass pill pops up for **Bold**, *Italic*, ~~Strikethrough~~, `Code`, and Links.
- **📊 Interactive Visual Tables** — drop a table from `/` and edit cells directly, press `Tab` to fly across cells, drag column borders to resize, or hover the edges to hit `+` for new rows/columns.
- **🔗 `@` Smart Note Mentions** — type `@` to link another note on the fly, then click the mention pill (or `Ctrl + Click`) to teleport straight to it.
- **🎨 Tactile Note Cards & Nested Folders** — soft translucent cards for every note, right-click or hover to tag notes/folders with custom accent colors, pin important notes to the top, and nest subfolders infinitely.
- **🚀 Raycast-Style Command Palette (`Ctrl + K`)** — jump to any note, switch themes, create notes, or export `.md` without touching your mouse.
- **🌳 ASCII Tree Generator (`Shift + Enter`)** — build clean `├─` / `└──` directory and hierarchy trees automatically while typing.
- **💾 100% Local `.md` Vault** — plain Markdown files stored right in your Documents (`Noti`) or any custom vault folder you pick. No cloud lock-in, no subscription, no proprietary database.

---

## get it rn (takes 5 seconds) ⚡

### option 1: the 1-liner terminal flex (PowerShell)
paste this into PowerShell and let it cook:

```powershell
irm https://raw.githubusercontent.com/akrram0/noti/main/install.ps1 | iex
```

### option 2: manual installer
grab **`Noti-Setup-win-x64.exe`** straight from the [**Latest Release**](https://github.com/akrram0/noti/releases/latest).

*(built-in one-click auto-updater included — whenever a new build drops, an update pill appears in the top bar & Settings → Vault to download, install, and relaunch automatically).*

---

## cheat code shortcuts ⌨️

| Shortcut | What it does |
| :--- | :--- |
| `Ctrl + K` | Open the Command Palette (search notes + run actions) |
| `/` | Open the visual Slash Block Menu (`H1`–`H3`, `Checklist`, `Quote`, `Code`, `Table`, `Image`, `Align`) |
| `@` | Link / mention another note (`Click` or `Ctrl + Click` to jump) |
| `Ctrl + N` | Cook up a fresh note |
| `Ctrl + Shift + N` | Create a new folder |
| `Ctrl + \` | Toggle / collapse the floating sidebar |
| `Ctrl + E` | Switch between **Live** and **Source** editor modes |
| `Ctrl + ,` | Open Settings (`Appearance`, `Profile`, `Vault`, `Shortcuts`) |
| `Ctrl + O` | Import a `.md` or `.txt` file into your vault |
| `Ctrl + Shift + S` | Export the current note as a `.md` file |
| `Ctrl + B` / `Ctrl + I` | **Bold** / *Italic* |
| `Ctrl + L` | Toggle `- [ ]` checklist on current line |
| `Shift + Enter` | Auto-continue `├─` / `└──` ASCII tree branch |
| `Ctrl + Mouse Wheel` | Zoom text in / out (`Ctrl + 0` to reset) |

---

## tech stack (for the nerds 🤓)

- **UI & Composition**: WinUI 3 (`WindowsAppSDK 1.6`) + native DirectComposition `SystemBackdrop` (`DesktopAcrylicController` & `MicaController`), custom monochrome vector line-art icons, and smooth Win32 `WM_NCLBUTTONDOWN` caption dragging
- **Runtime & Lifecycle**: `.NET 8` (`win-x64`, self-contained unpackaged launch) with single-instance Win32 mutex + `WM_COPYDATA` instant activation
- **Architecture**: Clean `MVVM` (`Noti.Core`, `Noti.Storage`, `Noti.App`, `Noti.Tests`)
- **CI/CD & Auto-Update**: GitHub Actions + Inno Setup (publishes `Noti-Setup-win-x64.exe` + `version.json` to the single rolling `Latest` release and automatically prunes older releases/tags)

---

<div align="center">
  <sub>built with immaculate vibes ✨</sub>
</div>
