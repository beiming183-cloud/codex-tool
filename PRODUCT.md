# Codex 工具箱

Windows 桌面工具，帮助用户检查多条代理线路的公网出口、查看 Clash 连接、补齐 Codex 额度浮层，并在指定校园网下自动限制指定代理程序联网。

## Confirmed brief
- Windows first. A portable single EXE, with the user's two existing Codex shortcuts preserved.
- Apple/macOS System Settings is the pinned aesthetic: quiet sidebar, grouped settings, precise typography, a restrained blue accent. Keep Windows window controls and keyboard conventions.
- Arbitrary user-created profiles; editable proxy URL, expected IP, optional group, account home and application binding. Never assume exactly two accounts.
- Custom campus network patterns and executable paths. Automatic outbound restriction on matching networks, with explicit Windows elevation when enabled.
- Shareable clean source and portable builds; keep personal settings, IPs, IDs, tokens, logs and auth files outside the source and public archives.
- Existing live Clash quality fork, Codex windows, shortcuts and campus guard must survive migration. Do not silently activate another guard.
- Default to automatic discovery of actual Codex desktop/CLI instances, their descendants, proxy contexts and observed TCP routes. No assumption of two windows, fixed IPs or prefilled manual profiles. Compare each instance independently and distinguish a current multi-egress observation from normal historical dynamic-IP changes.
- Campus recognition combines connected network information, SSID and education-domain DNS suffixes, with remembered campus/ordinary overrides. Weak name clues remain candidates until confirmed. Automatically discover supported running proxy kernels when enabled.

## Success
Buttons immediately acknowledge work, show actionable results, and allow retry/copy. Chinese text remains crisp at 150% DPI. Profile creation, editing, deletion, import/export and settings persistence work through the real native UI. Unknown observations must remain unknown; an IP probe alone does not prove an application's model route.

## Platform
.NET 8 WPF, Windows x64. Native controls, per-monitor DPI, no web runtime or third-party packages. Configuration in the Windows user data location; optional --data-dir override. Campus guard is an opt-in protected machine installation using Windows Firewall.

## Sharing UX, 2.2
Default pages use plain Chinese: home, quota overlay, campus protection, settings and help. Home has one check action; custom routes and all technical diagnostics use progressive disclosure. Quota locates running desktop windows and scoped authentication automatically, with a standalone fallback when no CDP endpoint is available. Do not restart Codex to enable injection. Preserve installed overlays. Custom Electron state never falls back silently to another account's default login.

## Responsive refinement, 2.3
User-provided Cockpit screenshot is the visual reference for cool blue-gray surfaces, outlined rounded panels and blue selection. Keep the simplified task structure. User-controlled 75-150% layout zoom, persisted local preferences, automatic navigation rail on narrow logical widths, card reflow by width and item count, native maximize/restore, per-monitor DPI handling and bounded owned windows. Windows x64 remains the target; mobile and ARM64 are not additional shipped platforms.
