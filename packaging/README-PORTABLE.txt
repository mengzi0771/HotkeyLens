HotkeyLens 1.1.0 — Portable (Windows x64)

Run HotkeyLens.exe from this folder. No installation and no .NET runtime are required.

Data:
  Scan snapshots and preferences are stored in %LOCALAPPDATA%\HotkeyLens, not in this
  folder. The portable and installed builds therefore share the same user data. Deleting
  this folder does not delete that data.

Security notice:
  This build is not code-signed. Windows SmartScreen or security software may show an
  "Unknown publisher" warning. Verify the SHA256 hash against SHA256SUMS.txt from the
  same GitHub release before running.

  If saving is blocked by application-control software, add HotkeyLens to that product's
  trusted/application list. Do not disable protection just to use the app.

  HotkeyLens does not need administrator rights, a service, a tray icon, or a keyboard
  hook. It scans only when you click Scan.

Project:
  https://github.com/mengzi0771/HotkeyLens