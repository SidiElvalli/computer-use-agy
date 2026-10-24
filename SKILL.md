---
name: computer-use
description: >-
  High-speed, pinpoint-accurate Windows desktop automation (Claude Code / Codex grade).
  Controls mouse, keyboard, windows, and web/desktop UI elements via native ComputerBridge v2 (computer.exe),
  cached Windows UIAutomation, Set-of-Marks visual indexing (snap/click-id), and Windows OCR (ocr/click-ocr).
  Use whenever the user asks for "computer use" or to operate/click/type/scroll in desktop applications or browsers on their live Windows screen.
---

# Computer Use v2 (Windows — Native High-Speed Engine)

Native binary: `$HOME\.gemini\config\skills\computer-use\scripts\computer.exe`
(Auto-built by [computer.ps1](./scripts/computer.ps1) from [ComputerBridge.cs](./scripts/ComputerBridge.cs), [Capture.cs](./scripts/Capture.cs), [Uia.cs](./scripts/Uia.cs), [Ocr.cs](./scripts/Ocr.cs), and [Actions.cs](./scripts/Actions.cs)).

> [!IMPORTANT]
> **Performance & Accuracy Rules (Read First):**
> 1. **Call `computer.exe` directly** instead of spawning `powershell.exe` whenever possible (~140ms latency).
> 2. **Always batch multi-step workflows with `chain`**: Focus, waits, clicks, typing, and final verification happen in **one single process** and automatically abort at the first `ERROR:` so later steps never click blindly.
> 3. **Four tiers of 100% exact targeting (never guess raw coordinates blindly):**
>    - **Tier 1 — Semantic UIA (`click-text`, `type-ui`, `search-ui`, `invoke`, `set-ui`)**: Bulk-cached UIAutomation tree with smart ranking, auto-wait polling (`timeout=1200`), and post-click hit verification.
>    - **Tier 2 — Set-of-Marks (`snap` -> `click-id N`, `type-id N`)**: Renders numbered bounding boxes (`#1`, `#2`, ...) onto a screenshot and caches exact element coordinates so you can click by ID (`click-id 5`).
>    - **Tier 3 — Native Windows OCR (`ocr`, `click-ocr`, `snap ocr`)**: Reads on-screen text with exact bounding boxes for apps without UIA trees (Blender, games, canvas/WebGL, remote desktops).
>    - **Tier 4 — Magnified Crop (`zoom X Y`)**: Captures a 4x magnified, 10px-gridded crop around `(X, Y)` with true screen-coordinate labels for sub-pixel visual targeting.

```powershell
& "$HOME\.gemini\config\skills\computer-use\scripts\computer.exe" <action> [args]
```

---

## 1. Multi-Step Pipeline (`chain`) — Fastest & Safest Workflow
Execute a sequence of desktop actions in a single process. **Stops automatically at the first `ERROR:`** (pass `--keep-going` to continue past errors), and uses `wait-for` / `wait-window` instead of blind `wait` sleeps:
```powershell
& "$HOME\.gemini\config\skills\computer-use\scripts\computer.exe" chain `
  "focus 'MSI Center'" `
  "click-text 'Features'" `
  "wait-for 'User Scenario' 'MSI Center' timeout=3000" `
  "click-text 'User Scenario'" `
  "screenshot"
```

Browser search in one call:
```powershell
& "$HOME\.gemini\config\skills\computer-use\scripts\computer.exe" chain `
  "focus Chrome" `
  "search-ui 'Search' 'Antigravity CLI'" `
  "wait 1000" `
  "screenshot"
```

---

## 2. Set-of-Marks Visual Indexing (`snap` & `click-id`)
- **`snap [WINDOW] [max=150] [all] [ocr] [out=PATH]`**: Captures a screenshot with numbered bounding boxes (`#1`, `#2`, ...) over every interactive element (plus OCR text boxes if `ocr` is passed) and prints the indexed list.
- **`click-id <N> [right|middle|double|triple|ctrl|shift|alt]`**: Clicks element `#N` from the last `snap`.
- **`hover-id <N>`**: Moves the cursor to element `#N` from the last `snap`.
- **`type-id <N> <TEXT>`** / **`search-id <N> <TEXT>`**: Clicks element `#N`, selects all, types `TEXT` (and presses `Enter` for `search-id`).

---

## 3. Semantic UI Automation (Bulk-Cached, Ranked, Auto-Waiting)
- **`ui [WINDOW] [filter=TEXT]`**: Lists visible UI elements in reading order with exact `center=X,Y`, `rect=X,Y,W,H`, and `id=AutomationId`.
- **`find <TEXT> [WINDOW] [type=Button]`**: Shows the top ranked matches with scores and coordinates without clicking.
- **`click-text <TEXT> [WINDOW] [type=Button] [index=N] [timeout=1200] [right|double|ctrl|shift|alt]`**: Waits up to `timeout` ms for the element to appear, ranks exact > `AutomationId` > prefix > word > substring (preferring smaller leaf controls over large containers), clicks its exact clipped center, and reports what element was hit. If not found, suggests the closest visible elements.
- **`invoke <TEXT> [WINDOW] [type=...] [index=N]`**: Triggers UIA `Invoke` / `Select` / `Toggle` / `ExpandCollapse` directly **without moving the mouse** (works even on background windows).
- **`type-ui <NAME> <TEXT> [WINDOW]`** / **`search-ui <NAME> <TEXT> [WINDOW]`**: Finds an input control matching `NAME`, clicks it, selects all, types `TEXT` (and presses `Enter` for `search-ui`).
- **`set-ui <NAME> <TEXT> [WINDOW]`**: Sets a control's value directly via UIA `ValuePattern` without simulated typing.
- **`read-ui <NAME> [WINDOW]`**: Reads the current text/toggle/selection value of a UI element.

---

## 4. Native Windows OCR (`ocr` & `click-ocr`)
For custom-rendered UIs (Blender, games, canvas apps, PDFs/images):
- **`ocr [WINDOW] [region=x,y,w,h] [find=TEXT] [words] [zoom=N]`**: Runs `Windows.Media.Ocr` on the window or region (auto-upscaling small regions for tiny UI fonts) and prints recognized lines/words with exact screen `center=X,Y`.
- **`click-ocr <TEXT> [WINDOW] [region=x,y,w,h] [index=N] [right|double]`**: Finds on-screen text via OCR and clicks the exact tight bounding box of the matched words.

---

## 5. Observe, Zoom & Inspect
- **`screenshot [grid] [window=TITLE] [region=x,y,w,h] [scale=N] [fmt=png|jpg] [quality=85] [cursor=0|1] [out=PATH]`**: Captures the full virtual desktop, a specific window, or a region. All grid labels use **true screen coordinates** even when cropped or scaled.
- **`zoom <X> <Y> [radius=80] [scale=4] [out=PATH]`**: Captures a magnified crop around `(X, Y)` with a fine 10px/25px coordinate grid labeled in real screen pixels—ideal for verifying small icons before clicking.
- **`windows`**: Lists all visible top-level windows (`title | rect | center | state | hwnd`). All window commands also accept `hwnd=<HANDLE>`.
- **`monitors`**: Lists all monitors, virtual desktop bounds, and per-monitor DPI scaling.
- **`cursor`**: Prints `cursor=X,Y -> [ControlType] 'Name' in 'Window'`.
- **`pixel <X> <Y>`**: Returns the exact `#RRGGBB rgb(R,G,B)` color at screen pixel `(X, Y)`.
- **`doctor`**: Verifies native bridge health, Per-Monitor-V2 DPI awareness, UIA scan speed, and OCR availability.

---

## 6. Direct Mouse Control (SendInput + Hit Verification)
All coordinates are physical screen pixels across the full virtual desktop. Every click verifies the final cursor position and reports the UI element hit under `(X, Y)`:
- **`click <X> <Y> [right|middle|double|triple|ctrl|shift|alt|nohit] [speed=ms]`**
- **`double <X> <Y>`** / **`triple <X> <Y>`** / **`right-click <X> <Y>`**
- **`move <X> <Y> [speed=ms]`**: Moves cursor (`speed=0` is instant; `speed>0` interpolates smoothly) and reports the element under `(X, Y)`.
- **`drag <X1> <Y1> <X2> <Y2> [X3 Y3 ...] [button=left|right|middle] [mod=ctrl|shift|alt] [duration=ms]`**: Smooth multi-waypoint drag.
- **`mouse-down [X Y] [right|middle]`** / **`mouse-up [X Y] [right|middle]`**: Explicit button press/release for custom gestures.
- **`scroll <up|down|left|right> [ticks=3] [X Y]`**: Sends per-notch hardware wheel events (compatible with Blender, CAD, and games).
- **`click-window <TITLE>`**: Restores window, raises it to the top, and clicks its center.

---

## 7. Keyboard, Clipboard & Synchronization
- **`type <TEXT...>`**: Types arbitrary Unicode text in batched `SendInput` packets (~10x faster than per-key loops, supports `\n` and `\t`).
- **`paste <TEXT...>`**: Preserves the user's existing clipboard text, pastes `TEXT` via `Ctrl+V`, and restores the previous clipboard.
- **`clip-get`** / **`clip-set <TEXT...>`**: Reads or writes the Windows clipboard directly.
- **`type-into <X> <Y> <TEXT...>`** / **`search-at <X> <Y> <TEXT...>`**: Clicks `(X, Y)`, presses `Ctrl+A`, types `TEXT` (and presses `Enter` for `search-at`).
- **`key <COMBO> [COMBO2...]`**: Hardware scan-code shortcuts with repeat syntax (`down*5`, `backspace*10`, `ctrl+shift+t`, `ctrl+l`, `alt+f4`, `win+r`, `f1`..`f12`, media keys, punctuation). Rejects unknown key names with `ERROR:`.
- **`wait-for <TEXT> [WINDOW] [timeout=5000]`** / **`wait-gone <TEXT> [WINDOW] [timeout=5000]`** / **`wait-window <TITLE> [timeout=8000]`**: Condition polling so chains proceed the instant UI updates.
- **`focus <TITLE>`**: Restores and raises window to the foreground (uses `AttachThreadInput` + `SwitchToThisWindow` + `TOPMOST`/`NOTOPMOST` Z-order raise to bypass Windows ForegroundLock).
- **`window <TITLE> <maximize|minimize|restore|close|center|move X Y W H>`**: Manages window state and geometry.
- **`launch <APP_OR_PATH> [args...]`**: Launches an application on the interactive desktop.
