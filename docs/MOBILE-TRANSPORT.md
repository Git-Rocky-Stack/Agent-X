# Agent-X Mobile: Connection and Transport

This documents how the Agent-X Mobile companion (`src/AgentX.Mobile`, .NET MAUI, Android) reaches
the desktop app, and the transport rules it enforces. It records the decision taken for QA findings
**AX-QA-004** (reachability) and **AX-QA-005** (transport security).

## Summary

| Aspect | Behavior |
|---|---|
| Desktop listener | **Loopback only**: `http://localhost:9846/`, an `HttpListener` (HTTP.sys) prefix in the desktop process. It is deliberately not bound to the LAN. |
| Supported connections | An Android emulator through its host alias `10.0.2.2`, or any device (emulator or physical, over USB) through `adb reverse tcp:9846 tcp:9846` and `http://localhost:9846`. |
| Plaintext HTTP | Accepted by `AgentXApiClient` only for loopback hosts (`localhost`, `127.0.0.0/8`, `::1`) and `10.0.2.2`. Android's network security config allows cleartext only for `localhost`, `127.0.0.1` and `10.0.2.2`. Any other host must use HTTPS. |
| LAN or remote | **Not supported.** The client requires HTTPS for every other host, and the desktop does not serve HTTPS. |
| Certificate validation | The platform's default chain validation, with optional SPKI SHA-256 pinning. Nothing sets a pin yet. The client never accepts every certificate. |
| Authentication | Every data route needs the desktop's API token as `Authorization: Bearer <token>`. |

## Why loopback only

The QA audit found two problems with the mobile path as it first shipped:

1. The Settings page told users to enter the desktop's **LAN IP**, but the desktop API binds only to
   `http://localhost:9846/`, so a phone on the LAN could never reach it (AX-QA-004).
2. Had it been reachable, the client would have sent its bearer token and private data over
   **plaintext HTTP**, and over HTTPS it **disabled all certificate validation**
   (`DangerousAcceptAnyServerCertificateValidator`), which anyone on the network could intercept
   (AX-QA-005).

Rather than open the listener on an insecure LAN interface, the desktop stays loopback only and the
mobile client is hardened. Traffic never leaves the device and the computer it is plugged into.

## How to connect

### On the desktop

The Local API is on by default. In Agent-X on the desktop, open **Settings**, section
**Connections**:

- **Enable Local API** turns the listener on or off. The change takes effect when you save settings.
- **API Token** shows the token (masked until you press **Show**). **Copy** copies the real token.
  **Regenerate** replaces it and revokes the old one at once, so a paired phone must be given the
  new token.

### Android emulator

The emulator reaches the host computer's loopback through `10.0.2.2`:

```
http://10.0.2.2:9846
```

`adb reverse tcp:9846 tcp:9846` works on an emulator too; then use `http://localhost:9846`.

### Physical device (USB)

Forward the desktop port over the USB (adb) connection, then connect to localhost:

```
adb reverse tcp:9846 tcp:9846
# then, in the app, set the API URL to:
http://localhost:9846
```

The forward lasts while the device stays connected and the adb server runs. Repeat the command after
reconnecting.

### In the app

Open the app's **Settings** tab, enter the **Agent-X API URL** and the **API Token**, tap **Test
Connection** (it checks the values on screen without applying them), then **Save**. **Reset to
Default** puts `http://localhost:9846` back. The URL is kept in app preferences; the token is kept
in the platform's secure storage.

### Two platform details

Two details decide whether a request reaches the desktop at all:

1. **Android blocks cleartext HTTP** (API 28 and later) unless the app's network security config
   allows it. `Platforms/Android/Resources/xml/network_security_config.xml`, referenced from
   `AndroidManifest.xml`, allows cleartext for exactly `localhost`, `127.0.0.1` and `10.0.2.2`.
2. **HTTP.sys checks the Host header.** The listener is registered for `http://localhost:9846/`, and
   HTTP.sys answers a request whose `Host` header names another host with
   `400 Bad Request - Invalid Hostname`. When the API URL uses `10.0.2.2`, `AgentXApiClient`
   therefore sends `Host: localhost:9846` on every request.

## What the app reports

Every call returns a typed `ApiResult` with a status, and the pages show its message instead of an
empty list:

| Status | Meaning |
|---|---|
| Unauthorized ("Not paired") | The desktop answered 401 or 403: no token, a mistyped one, or one that was regenerated. |
| Unreachable ("Cannot reach Agent-X") | No HTTP answer: the desktop app or its Local API is off, the URL is wrong, `adb reverse` is missing, the platform blocked the request, or it timed out (15 seconds). |
| NotFound | The desktop answered 404: an unknown document or conversation, or a route this desktop build does not have. |
| Error | The desktop answered with another error status (the message from its `{success, data, error}` envelope is shown), or sent a response the app cannot read. |

The token saved at pairing is loaded from secure storage before the client's first request, so a
page that loads during startup is never sent without it.

## Security enforced by the client

`AgentXApiClient` (`src/AgentX.Mobile/Services/AgentXApiClient.cs`):

- **Rejects plaintext HTTP to any non-loopback host** with an `ArgumentException`. Settings shows
  the message instead of crashing, and an invalid URL is never saved.
- **Never accepts every TLS certificate.** When an SPKI SHA-256 pin is set
  (`SetPinnedServerCertificate`), an HTTPS leaf certificate must match it (constant-time compare);
  otherwise the platform's default chain validation applies. Nothing calls
  `SetPinnedServerCertificate` yet, because the desktop has no HTTPS listener to pin.
- **Sends the bearer token per request.** The `Authorization` header is set on each request
  message, never on the shared client's default headers, so pairing in Settings cannot race a
  request that is already in flight.

## Not supported yet

Connecting a phone across the LAN would need all of the following, none of which exists today:

1. An **HTTPS** listener on the desktop, bound to a LAN interface the user chooses (off by default).
2. A **pairing step** that gives the phone the server certificate's SPKI hash to pin with
   `SetPinnedServerCertificate`.
3. An end-to-end test on a device or emulator.

## CI

`.github/workflows/android-build.yml` installs the `maui-android` workload and builds
`src/AgentX.Mobile` (`net8.0-android`, Release) on pushes to `main` and on pull requests that change
the mobile project, `Directory.Build.props`, `global.json` or the workflow itself. It is a
hard gate (no `continue-on-error`). The mobile project is not in `AgentX.sln`, so the desktop build
never needs the MAUI workloads. The iOS target is conditioned out on Linux (the full `maui` workload
is not supported there) and is not built in CI; it needs a macOS runner.

A green build does not prove the pages load. Every page used to crash on first display with
"StaticResource not found" because its converters were added from code-behind after
`InitializeComponent` had already resolved the keys; they are now declared in `App.xaml`. The build
cannot see that class of failure, and there is no device or emulator smoke test yet.

### Root cause of the earlier "compile-unverified" status (AX-QA-004)

Two separate problems, fixed in order:

1. **No Android platform head.** The project had no `Platforms/Android/` folder. The standard
   scaffolding (`AndroidManifest.xml`, `MainActivity`, `MainApplication`) and the app icon and
   splash assets were added.
2. **A call to an API that does not exist.** `MauiProgram.cs` called a parameterless
   `builder.UseMaui()`, which fails with `CS1061`. This was first misread as a missing
   `Microsoft.Maui.Controls.dll` reference, but that assembly was always resolved; `.UseMaui()`
   simply is not a public API. The fix is the standard bootstrap, `builder.UseMauiApp<App>()`, as
   `dotnet new maui` generates it.

Supporting project facts: `Microsoft.Maui.Controls` 8.0.100 is a required explicit
`PackageReference` (the .NET 8 MAUI SDK warns with `MA002` without it), and
`Microsoft.Extensions.Logging.Debug` backs `builder.Logging.AddDebug()` in Debug builds.
