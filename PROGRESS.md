# NightGuard Iteration Progress

## Phase 1 - Recent Guard Records

- Status: completed
- Scope: recent in-memory guard action records, newest-first homepage display limited to 20 records, hooks for restriction state, process blocking, hosts changes.
- Verification: `dotnet build -o .\artifacts\build-check` passed after final review.
- Risk: process and hosts hooks are only invoked from existing restriction paths.

## Phase 2 - Entertainment Rule Templates

- Status: completed
- Scope: one-click templates for common entertainment apps and domains with de-duplication.
- Verification: `dotnet build -o .\artifacts\build-check` passed.
- Risk: template domains are seed data and may need future maintenance.

## Phase 3 - Config Import/Export

- Status: completed
- Scope: JSON export/import with confirmation in UI.
- Verification: `dotnet build -o .\artifacts\build-check` passed.
- Risk: import overwrites current config after confirmation.

## Phase 4 - Startup Self Check

- Status: completed
- Scope: administrator status, hosts write permission, startup task status, current restriction status.
- Verification: `dotnet build -o .\artifacts\build-check` passed.
- Risk: hosts check must not modify hosts content.

## Phase 5 - Safer Test Mode

- Status: completed
- Scope: test mode only targets NightGuardTestApp.exe and does not write hosts.
- Verification: `dotnet build -o .\artifacts\build-check` passed.
- Risk: clicking test mode can terminate NightGuardTestApp.exe only.

## Phase 6 - Single-file Publish

- Status: completed
- Scope: publish win-x64 Release single-file NightGuard.exe to publish/NightGuard.
- Verification: `dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish\NightGuard` passed; `publish/NightGuard/NightGuard.exe` generated.
- Risk: published app still needs administrator launch for hosts/process operations.

## Final Review

- Build: `dotnet build -o .\artifacts\build-check` passed.
- Publish: `dotnet publish ... -o .\publish\NightGuard` passed.
- Generated executable: `publish/NightGuard/NightGuard.exe`.
- Manual launch check: not performed automatically because starting the real guard app can activate tray/timer behavior on the current machine.
- Notes: normal `dotnet build` to `bin\Debug` was blocked by a currently running `NightGuard.exe` process locking existing Debug outputs, so validation used an isolated output directory.

## 2026-05-28 Night Focus Rule Fix

- Status: completed
- Scope: repaired the night-time rule gap for Codex / Cursor / AI sites with a two-stage flow:
  `23:00 - 23:30` reminder only;
  `23:30 - 07:00` night-focus prompt mode.
- Rules:
  - Added default night-focus process list: `Codex.exe`, `Cursor.exe`
  - Added default night-focus domains: `chatgpt.com`, `chat.openai.com`, `claude.ai`, `gemini.google.com`, `perplexity.ai`
  - Reminder is logged once per tool per night
  - Limited mode offers "本晚不再限制", "记录到明天并最小化", and "稍后再提醒我"
  - "记录到明天并最小化" records the action and attempts to minimize the target app main window
  - "稍后再提醒我" suppresses the prompt for 15 minutes
  - hosts failures are logged and surfaced through the homepage status note
  - hosts are restored after leaving the limited window or on exit
- Verification:
  - `dotnet build` passed
  - `dotnet build -o .\artifacts\build-check-nightfocus` passed
  - `dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish\NightGuard` passed
- Manual testing still needed:
  - real reminder timing at `23:00 - 23:30`
  - real "本晚不再限制" behavior through `07:00`
  - real app-window minimization for "记录到明天并最小化"
  - real 15-minute reminder retry timing
  - real hosts interception for AI domains under administrator launch
