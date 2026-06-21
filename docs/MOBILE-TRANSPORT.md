# Agent-X Mobile — Connection & Transport

This documents the **explicit transport decision** for the Agent-X Mobile companion
(`src/AgentX.Mobile`), per QA findings **AX-QA-004** (reachability) and **AX-QA-005**
(transport security).

## Summary

| Aspect | Decision |
|---|---|
| Desktop listener | **Loopback only** (`http://localhost:9846`). Deliberately *not* broadened to the LAN. |
| Supported mobile connection | Loopback-reachable only: Android emulator (`10.0.2.2`) or a physical device with USB port-forwarding (`adb reverse`). |
| Plaintext HTTP | Allowed **only** to loopback / `10.0.2.2`. Refused to any other host. |
| LAN / remote | Requires **HTTPS** + a pairing-established certificate pin. The secure LAN listener is not yet implemented (see Roadmap). |
| Certificate validation | Platform default chain validation, with optional SPKI-SHA-256 pinning. The previous "accept any certificate" bypass has been removed. |

## Why loopback-only

The QA audit found two problems with the as-shipped mobile path:

1. The Settings page instructed users to enter the desktop's **LAN IP**, but the desktop API
   binds only to `http://localhost:9846/` — so a physical device on the LAN could never reach
   it (AX-QA-004).
2. If reachability were widened, the client sent its bearer token and private data over
   **plaintext HTTP**, and if HTTPS was supplied it **disabled all certificate validation**
   (`DangerousAcceptAnyServerCertificateValidator`) — trivially interceptable (AX-QA-005).

Rather than broaden the listener onto an insecure LAN interface, the desktop stays
loopback-only and the mobile client is hardened. This is secure by construction: traffic
never leaves the device/host.

## How to connect today

### Android emulator
The emulator reaches the host machine's loopback via `10.0.2.2`:

```
http://10.0.2.2:9846
```

### Physical device (USB)
Forward the desktop port over the USB/ADB bridge, then connect to localhost:

```
adb reverse tcp:9846 tcp:9846
# then in the app, set the API URL to:
http://localhost:9846
```

In both cases, paste the API token from **AgentX desktop → Settings → Connections**.

## Security enforced by the client

`AgentXApiClient` (AX-QA-005):

- **Rejects plaintext HTTP to any non-loopback host** with a clear error (`ArgumentException`),
  surfaced in the mobile Settings page instead of crashing.
- **Never blanket-accepts TLS certificates.** When a pairing-established SPKI-SHA-256 pin is set
  (`SetPinnedServerCertificate`), HTTPS leaf certificates must match it (constant-time compare);
  otherwise the platform's default chain validation applies.

## Roadmap: secure LAN transport

To support connecting from a physical device across the LAN, the desktop must:

1. Expose an **HTTPS** listener on a deliberately chosen, user-enabled LAN interface (off by
   default; do not auto-bind).
2. Provide a **pairing handshake** that delivers the server certificate's SPKI hash to the
   mobile client (e.g., a QR code shown in desktop Settings), which the client pins via
   `SetPinnedServerCertificate`.
3. Add an end-to-end device/emulator reachability test.

This is deferred pending product go-ahead — it is a larger feature than the security hardening
that has already landed, and it requires the secure listener + pairing UI on the desktop side.

## CI

`.github/workflows/android-build.yml` installs the `maui-android` workload and builds
`src/AgentX.Mobile` (`net8.0-android`) on every change under it, as a **blocking gate**. The iOS
target is conditioned out on Linux (the full `maui` workload is unsupported there) and remains
deferred (needs a macOS runner).

**Status: green (blocking).** The build is verified clean — Debug **and** Release, 0 warnings — via a
local MAUI build loop, and the CI job is a hard gate (no `continue-on-error`). The mobile code,
including the AX-QA-005 transport hardening, can no longer drift compile-unverified.

### Root cause of the earlier "compile-unverified" status (AX-QA-004)

Two distinct problems, fixed in order:

1. **No Android platform head.** The project had no `Platforms/Android/` at all — added the standard
   scaffolding (`AndroidManifest.xml`, `MainActivity`, `MainApplication`) plus the app icon/splash
   resizetizer assets.
2. **A wrong-API bug — not a reference quirk.** `MauiProgram.cs` called a *non-existent* parameterless
   `builder.UseMaui()`, which fails with `CS1061`. This was initially misread as a missing
   `Microsoft.Maui.Controls.dll` reference and chased through workload/package permutations — but that
   assembly was **always** resolved and `Microsoft.Maui.Hosting` was a global using. `MauiApp.CreateBuilder()`
   and `.UseMauiCommunityToolkit()` resolved precisely because they are real APIs; `.UseMaui()` did not
   because it is not one. The fix is the canonical bootstrap **`builder.UseMauiApp<App>()`** (it invokes
   the internal `UseMaui()` for you), exactly as `dotnet new maui` generates.

Supporting csproj facts: `Microsoft.Maui.Controls` is a **required** explicit `PackageReference` (the
.NET 8 MAUI SDK emits warning `MA002` without it; version `8.0.100` matches the `maui-android` workload
band pinned by `global.json`), and `Microsoft.Extensions.Logging.Debug` backs `builder.Logging.AddDebug()`
in Debug builds.
