# Computer-Use v2 for Antigravity CLI (`computer-use`)

High-speed, pinpoint-accurate **Windows Desktop Automation Skill** for **Google Antigravity CLI (`agy`)**.

Powered by a zero-dependency native C#/.NET bridge (`ComputerBridge v2` -> `computer.exe`), `computer-use` gives AI coding agents sub-150ms control over the live Windows desktop, browsers, Win32/WPF/WinUI apps, Electron apps, and custom GPU/canvas surfaces (like Blender or games).

---

## Why Computer-Use v2?

Traditional agent desktop tools suffer from slow PowerShell startup overhead (800ms+ per action), DPI scaling mismatches on high-DPI/multi-monitor setups, blind coordinate guessing, and inability to interact with the user's interactive desktop (`Winsta0\Default`) when launched from background/headless CLI subshells.

**Computer-Use v2 solves all of these:**
- **~140ms Native Execution**: Compiled directly to a ~90 KB native executable (`computer.exe`) using the built-in Windows .NET Framework compiler (`csc.exe`) — **zero external dependencies or SDK installs required**.
- **Interactive Desktop Bridge (`Winsta0\Default`)**: Automatically attaches threads and launches processes directly onto the active interactive Windows desktop (`OpenDesktop("Default")` + `SetThreadDesktop`), bypassing isolated CLI session pipes.
- **Per-Monitor-V2 DPI Awareness**: Works natively in physical screen pixels across multi-monitor setups with mixed DPI scaling (`SetProcessDpiAwarenessContext(PER_MONITOR_AWARE_V2)`).
- **4 Tiers of 100% Exact Targeting**: Never guess raw pixel coordinates blindly.
- **Single-Process `chain` Pipelines**: Execute multi-step workflows (focus -> click -> wait-for -> type -> screenshot) in a single process that automatically halts on the first `ERROR:`.

---

## How It Works (Architecture)

```
┌────────────────────────────────────────────────────────────────────────────┐
│                        Antigravity CLI Agent (agy)                         │
└─────────────────────────────────────┬──────────────────────────────────────┘
                                      │ (~140ms native call or `chain` batch)
                                      ▼
┌────────────────────────────────────────────────────────────────────────────┐
│                      computer.exe (ComputerBridge v2)                      │
│  • Per-Monitor-V2 DPI Awareness   • 25s Watchdog Timer                     │
│  • Winsta0\Default Desktop Bridge • ForegroundLock Bypass (AttachThread)   │
├─────────────────┬───────────────────┬──────────────────┬───────────────────┤
│  Tier 1: UIA    │  Tier 2: SoM      │  Tier 3: OCR     │  Tier 4: Zoom     │
│  (Uia.cs)       │  (Capture+Uia)    │  (Ocr.cs)        │  (Capture.cs)     │
│  Bulk-cached    │  `snap` renders   │  Reflection-     │  `zoom X Y` 4x    │
│  UIAutomation   │  numbered #1..#N  │  loaded WinRT    │  crop with 10px   │
│  + Chrome/Edge  │  boxes & caches   │  Windows.Media   │  grid labeled in  │
│  IAccessible    │  bounds for       │  .Ocr with 2-3x  │  true physical    │
│  tree wake-up   │  `click-id N`     │  auto-upscaling  │  screen pixels    │
└─────────────────┴───────────────────┴──────────────────┴───────────────────┘
```

### 1. Interactive Desktop & Foreground Lock Bypass (`ComputerBridge.cs`)
- **Desktop Switching**: When invoked from a CLI subshell that isn't attached to the active desktop, `RunOnDesktop` opens `Winsta0\Default` via `OpenDesktop` and binds an STA worker thread via `SetThreadDesktop`.
- **Foreground Activation**: Windows normally blocks background processes from stealing window focus (`ForegroundLockTimeout`). `ForceForeground()` combines `AllowSetForegroundWindow`, `AttachThreadInput`, `BringWindowToTop`, `SwitchToThisWindow`, and a transient `HWND_TOPMOST` -> `HWND_NOTOPMOST` Z-order promotion so `focus <TITLE>` reliably raises any window.
- **Batched Hardware Input**: Mouse clicks, smooth eased multi-waypoint drags, per-notch wheel scrolls (required by Blender/CAD), and Unicode text entry use Win32 `SendInput` packets with hardware scan codes.

### 2. Tier 1 — Bulk-Cached Semantic UI Automation (`Uia.cs`)
- **10x Faster Bulk Tree Scans**: Instead of making cross-process COM calls per element property, `CollectFromRoot()` activates a single `UIAutomation CacheRequest` (`Name`, `ControlType`, `BoundingRectangle`, `IsOffscreen`, `AutomationId`, `IsEnabled`) and fetches the entire window subtree in one round-trip.
- **Browser & Electron Accessibility Wake-Up**: Chromium browsers (`Chrome_WidgetWin_*`) and Electron apps build their accessibility trees lazily. `WakeUpBrowserAccessibility()` sends `WM_GETOBJECT` (`AccessibleObjectFromWindow` with `OBJID_CLIENT`) to `Chrome_RenderWidgetHostHWND` child windows so web page DOM elements immediately appear in UIA.
- **Smart Ranking & Auto-Wait**: `click-text` polls up to `timeout` ms for dynamic UI elements to appear and ranks matches by:
  1. Exact normalized match (interactive control: `100`, non-interactive: `85`)
  2. Exact `AutomationId` match (`95`)
  3. Prefix match (`75` / `60`)
  4. Whole-word match (`65` / `50`)
  5. Substring match (`55` / `40`)
  Ties break by shortest name delta, smallest bounding-box area (preferring leaf buttons over large parent panes), and reading order (`Y`, then `X`).

### 3. Tier 2 — Set-of-Marks (SoM) Visual Indexing (`Capture.cs` + `Uia.cs`)
- Running `computer.exe snap [WINDOW]` scans all visible interactive controls (and optionally OCR text via `snap ocr`), draws color-coded numbered bounding boxes (`#1`, `#2`, ...) onto a screenshot, and saves the element table to `%TEMP%\agy-computer-use\last_snap.tsv`.
- Subsequent calls to `click-id <N>`, `hover-id <N>`, `type-id <N> <TEXT>`, or `search-id <N> <TEXT>` look up the exact cached center coordinates for `#N` and click with zero ambiguity.

### 4. Tier 3 — Zero-Dependency Native Windows OCR (`Ocr.cs`)
- For custom-rendered UIs that expose no UIA tree (Blender, Unity/Unreal games, HTML5 `<canvas>`, PDFs, images), `Ocr.cs` invokes the built-in Windows 10/11 `Windows.Media.Ocr.OcrEngine` via runtime WinRT reflection (`Type.GetType("..., ContentType=WindowsRuntime")`) — requiring no external WinMD files or NuGet packages at compile time.
- Small regions are automatically upscaled `2x`–`3x` with bicubic interpolation before recognition so tiny UI labels are read accurately, then mapped back to exact physical screen coordinates.
- `click-ocr <TEXT>` computes the tight bounding box around the matched words within a line and clicks its exact center.

### 5. Tier 4 — True-Screen-Coordinate Capture & Magnified Crop (`Capture.cs`)
- Uses GDI `BitBlt` with `SRCCOPY | CAPTUREBLT` to capture hardware-accelerated and layered/transparent windows.
- Whenever `grid` or `zoom X Y` is used, the coordinate grid labels (`X,Y`) are drawn using **true physical screen coordinates** even when cropped to a window/region or scaled by `4x`. What you read on the grid is the exact `(X, Y)` you pass to `click X Y`.

---

## Repository Structure

```text
computer-use-agy/
├── README.md                 # Documentation, architecture, and CLI reference
├── LICENSE                   # MIT License
├── .gitignore                # Git ignore rules
├── install.ps1               # One-command installer & compiler for Antigravity CLI
├── SKILL.md                  # Antigravity CLI skill definition & agent instructions
└── scripts/
    ├── computer.ps1          # Auto-compiling PowerShell entry point
    ├── ComputerBridge.cs     # Core Win32 P/Invoke, DPI, Winsta0\Default desktop, input
    ├── Capture.cs            # BitBlt capture, true-coordinate grids, Set-of-Marks overlay
    ├── Uia.cs                # Cached UIAutomation, browser wake-up, ranking, snap cache
    ├── Ocr.cs                # WinRT Windows.Media.Ocr engine via reflection
    ├── Actions.cs            # CLI parser, chain pipeline, window/mouse/keyboard actions
    └── computer.exe          # Pre-compiled native x64 binary
```

---

## Installation

### Requirements
- **OS**: Windows 10 or Windows 11 (x64)
- **Compiler**: Built-in .NET Framework 4.x (`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` — pre-installed on every Windows 10/11 machine)
- **Antigravity CLI (`agy`)**: Installed and configured (`~/.gemini/config/skills/`)

### Option 1: One-Command Install (Recommended)

Clone this repository and run `install.ps1` in PowerShell:

```powershell
git clone https://github.com/SidiElvalli/computer-use-agy.git
cd computer-use-agy
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
```

`install.ps1` will:
1. Copy `SKILL.md` and `scripts/*` to `$HOME\.gemini\config\skills\computer-use`
2. Configure `SKILL.md` with your exact user path so the agent invokes `computer.exe` directly
3. Compile `computer.exe` and `ComputerBridge.dll` from the C# source files
4. Run `computer.exe doctor` to verify Per-Monitor-V2 DPI, UIA speed, and Windows OCR availability

### Option 2: Manual Installation

Copy the repository directly into your Antigravity skills folder and trigger a build:

```powershell
git clone https://github.com/SidiElvalli/computer-use-agy.git "$HOME\.gemini\config\skills\computer-use"
powershell -NoProfile -ExecutionPolicy Bypass -File "$HOME\.gemini\config\skills\computer-use\scripts\computer.ps1" --rebuild doctor
```

---

## Usage & Quickstart Examples

Once installed, Antigravity CLI automatically discovers the `computer-use` skill whenever you ask it to interact with desktop apps or browsers. You can also call `computer.exe` directly from PowerShell:

```powershell
$cu = "$HOME\.gemini\config\skills\computer-use\scripts\computer.exe"
```

### 1. Run Diagnostics (`doctor`)
```powershell
& $cu doctor
```

### 2. Multi-Step Pipeline (`chain`)
Run an entire workflow in one process (automatically stops on the first `ERROR:`):
```powershell
& $cu chain `
  "focus Chrome" `
  "search-ui 'Search' 'Google Antigravity CLI'" `
  "wait 1000" `
  "screenshot"
```

### 3. Set-of-Marks Visual Indexing (`snap` -> `click-id`)
```powershell
# Capture a screenshot with numbered boxes (#1, #2, ...) over all interactive controls
& $cu snap Calculator

# Click element #5 from the snapshot
& $cu click-id 5
```

### 4. Semantic UI Automation (`click-text`, `type-ui`, `invoke`)
```powershell
# List visible UI elements in Calculator
& $cu ui Calculator

# Click buttons by accessible name
& $cu chain "focus Calculator" "click-text Seven" "click-text Plus" "click-text Eight" "click-text Equals"
```

### 5. Native Windows OCR (`ocr` & `click-ocr`)
For apps without accessibility trees (Blender, games, canvas apps):
```powershell
# Dump all on-screen text in Blender with exact screen coordinates
& $cu ocr Blender

# Click on-screen text directly via OCR
& $cu click-ocr "Render" Blender
```

---

## Complete Command Reference

| Category | Command | Description |
| :--- | :--- | :--- |
| **Pipeline** | `chain "<step1>" "<step2>" ... [--keep-going]` | Executes multiple actions in one process; aborts on first `ERROR:`. |
| **Set-of-Marks** | `snap [WINDOW] [max=150] [all] [ocr] [out=PATH]` | Saves screenshot with numbered boxes (`#1..#N`) + prints element table. |
| | `click-id <N> [right\|double\|ctrl\|shift\|alt]` | Clicks element `#N` from the last `snap`. |
| | `hover-id <N>` | Moves cursor to element `#N` from the last `snap`. |
| | `type-id <N> <TEXT>` / `search-id <N> <TEXT>` | Clicks `#N`, selects all, types `TEXT` (+ `Enter` for `search-id`). |
| **Semantic UIA** | `ui [WINDOW] [filter=TEXT] [max=160]` | Lists visible UI elements with `center=X,Y`, `rect`, and `AutomationId`. |
| | `find <TEXT> [WINDOW] [type=Button]` | Lists top ranked UIA matches with scores and coordinates. |
| | `click-text <TEXT> [WINDOW] [type=...] [index=N] [timeout=1200]` | Polls until found, ranks best match, clicks center, verifies hit. |
| | `invoke <TEXT> [WINDOW] [type=...] [index=N]` | Triggers UIA `Invoke`/`Toggle`/`Select` pattern without moving mouse. |
| | `type-ui <NAME> <TEXT> [WINDOW]` | Finds input matching `NAME`, clicks, `Ctrl+A`, types `TEXT`. |
| | `search-ui <NAME> <TEXT> [WINDOW]` | Same as `type-ui` followed by `Enter`. |
| | `set-ui <NAME> <TEXT> [WINDOW]` | Sets control value directly via UIA `ValuePattern`. |
| | `read-ui <NAME> [WINDOW]` | Reads value/state of the matching UI element. |
| **Windows OCR** | `ocr [WINDOW] [region=x,y,w,h] [find=TEXT] [words] [zoom=N]` | Reads on-screen text via `Windows.Media.Ocr` with screen coordinates. |
| | `click-ocr <TEXT> [WINDOW] [region=x,y,w,h] [index=N]` | Finds text on screen via OCR and clicks the matched word bounds. |
| **Observe** | `screenshot [grid] [window=T] [region=x,y,w,h] [scale=N] [fmt=png\|jpg]` | Captures full desktop, window, or region with true screen grid labels. |
| | `zoom <X> <Y> [radius=80] [scale=4]` | Captures a 4x magnified crop around `(X, Y)` with a 10px coordinate grid. |
| | `windows` | Lists all visible top-level windows (`title \| rect \| center \| state \| hwnd`). |
| | `monitors` | Lists all monitors, virtual desktop bounds, and per-monitor DPI. |
| | `cursor` | Prints cursor coordinates and the UI element currently underneath. |
| | `pixel <X> <Y>` | Returns `#RRGGBB rgb(R,G,B)` at screen pixel `(X, Y)`. |
| | `doctor` | Checks DPI awareness, desktop state, UIA scan speed, and OCR engine. |
| **Mouse** | `click <X> <Y> [right\|middle\|double\|triple\|ctrl\|shift\|alt]` | Clicks physical screen pixel `(X, Y)` and reports hit element. |
| | `move <X> <Y> [speed=ms]` | Moves cursor (`speed>0` interpolates smoothly). |
| | `drag <X1> <Y1> <X2> <Y2> [X3 Y3 ...] [duration=ms]` | Smooth eased drag across two or more waypoints. |
| | `mouse-down [X Y] [right\|middle]` / `mouse-up [X Y]` | Presses or releases mouse button for custom gestures. |
| | `scroll <up\|down\|left\|right> [ticks=3] [X Y]` | Sends per-notch hardware wheel events. |
| **Keyboard & Sync** | `type <TEXT...>` | Types arbitrary Unicode text via batched `SendInput` packets. |
| | `paste <TEXT...>` | Pastes `TEXT` via `Ctrl+V` while preserving previous clipboard contents. |
| | `clip-get` / `clip-set <TEXT...>` | Reads or writes the Windows clipboard. |
| | `key <COMBO> [COMBO2...]` | Sends key combos (`ctrl+shift+t`, `win+r`, `down*5`, `backspace*10`). |
| | `wait-for <TEXT> [WINDOW] [timeout=5000]` | Waits until a UI element matching `TEXT` appears. |
| | `wait-gone <TEXT> [WINDOW] [timeout=5000]` | Waits until a UI element matching `TEXT` disappears. |
| | `wait-window <TITLE> [timeout=8000]` | Waits until a window matching `TITLE` appears. |
| **Windows** | `focus <TITLE>` | Restores and brings window to the foreground. |
| | `window <TITLE> <maximize\|minimize\|restore\|close\|center\|move X Y W H>` | Controls window state and geometry. |
| | `launch <APP_OR_PATH> [args...]` | Launches an interactive desktop application on `Winsta0\Default`. |

---

## License

Released under the [MIT License](./LICENSE).
