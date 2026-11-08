# FsHealth

A native, offline glucose and blood pressure journal for **Android, iOS, and macOS through Mac Catalyst**, written in **F# with Fabulous for .NET MAUI 10**. The interface uses warm off-white surfaces, teal accents, large measurement values, and native date/time pickers. It starts empty and never mixes sample measurements with personal data.

## What it does

- Today: latest glucose and blood pressure readings, recent entries, and trend previews.
- Log glucose in mg/dL or mmol/L, with fasting/before meal/after meal/bedtime context.
- Log systolic/diastolic pressure and optional pulse.
- Record date, time, and notes; edit readings and confirm deletions.
- Journal: newest-first history, reading-type filters, and incremental display of older entries.
- Trends: 7, 30, or 90 local calendar days, individual measurement charts, and arithmetic averages.
- Settings: change glucose display units, export the complete journal as CSV through the native share sheet, or delete all readings after confirmation.
- Encrypt the local journal using AES-256-GCM, with its random key in MAUI SecureStorage (Android encrypted storage / Apple Keychain).

This is a **manual journal**. It records measurements from a separate meter/cuff. Bluetooth devices, CGM feeds, HealthKit, Health Connect, diagnosis, treatment recommendations, reminders, and cloud accounts are outside this version. Input bounds detect entry mistakes and are not clinical target ranges.

## Pinned versions

Verified against package feeds on November 8, 2026:

| Component | Version |
| --- | --- |
| .NET SDK | 10.0.400 feature band; latest patch 10.0.403 recommended |
| Target frameworks | net10.0-android; net10.0-ios; net10.0-maccatalyst |
| Fabulous.MauiControls | 10.0.4 |
| Microsoft.Maui.Controls / Compatibility | 10.0.120 |
| FSharp.Core | 10.1.403 |
| Fantomas | 8.1.1 |

The current MAUI implementation of Fabulous is `Fabulous.MauiControls`; the old `Fabulous.Maui` package is not the current .NET 10 package. See [Fabulous’s MAUI guide](https://fabulous-dev.github.io/Fabulous/docs/tutorials/maui/) and [MAUI’s release policy](https://dotnet.microsoft.com/en-us/platform/support/policy/maui).

## Build and run

Install the latest .NET 10.0.4xx SDK patch and the appropriate MAUI workload. `global.json` selects the latest installed patch in that feature band; the local Android build was verified with SDK 10.0.401. Android needs the workload-compatible Android SDK and JDK 21. iOS and Mac Catalyst need **macOS and Xcode compatible with the installed Apple workloads**. Physical iOS devices and signed Mac distributions also require signing credentials. See [MAUI platform requirements](https://learn.microsoft.com/en-us/dotnet/maui/supported-platforms?view=net-maui-10.0) and [workload/Xcode release mappings](https://github.com/dotnet/maui/wiki/Release-Versions).

Run commands from the repository root. `FsHealth.slnx` contains the shared MAUI app, core, and tests. All three targets are enabled by default; the platform-only switches below let restore/build work with only that platform's workload installed.

### Rider

Install and enable JetBrains' [Rider Android Support](https://plugins.jetbrains.com/plugin/12056-rider-android-support) plugin in **Settings → Plugins**, then restart Rider. The .NET Android workload alone does not add Rider's Android run/debug integration. Without this plugin, Rider may show only the iOS and macOS run configurations even though `net10.0-android` is enabled and builds successfully. See [JetBrains' MAUI setup guide](https://www.jetbrains.com/help/rider/MAUI.html).

After restarting, select the **Android** configuration for **FsHealth.App** in the run configuration selector. If it was not created automatically, use **Run → Edit Configurations → + → Android**, select `src/FsHealth.App/FsHealth.App.fsproj`, and select the Android target framework. Choose a connected device or an Android emulator to run the app. If **Android** is absent from the configuration types, check that the plugin is installed and enabled.

In **Settings → Build, Execution, Deployment → Android**, set **Java Development Kit Location** to JDK 21. On this development machine it is `C:\Program Files\Android\openjdk\jdk-21.0.8`, and **Android SDK Location** is `C:\Program Files (x86)\Android\android-sdk`. Rider's legacy Xamarin environment setup can select its bundled `jdk8`; that causes `XA0031` before compilation/deployment even when command-line builds succeed. Changing Rider's Java path fixes that IDE-specific failure; changing the app's target frameworks does not.

### Android (Windows or macOS)

```text
dotnet workload install maui-android
dotnet build src/FsHealth.App/FsHealth.App.fsproj -c Debug -f net10.0-android -p:FsHealthAndroidOnly=true
dotnet build src/FsHealth.App/FsHealth.App.fsproj -c Debug -f net10.0-android -p:FsHealthAndroidOnly=true -t:Run
```

Start an Android emulator or connect an authorized development device before using `-t:Run`. If SDK discovery fails, pass `-p:AndroidSdkDirectory="<Android SDK path>" -p:JavaSdkDirectory="<JDK 21 path>"` to the build command.

The minimum Android version is API 23 (Android 6). The debug APK is generated under `src/FsHealth.App/bin/Debug/net10.0-android/` and uses a development signature. Assemblies are embedded so it can be installed independently of the debugger. Release packaging produces APK/AAB; supply your own release keystore before distributing.

### iOS (Mac)

```text
dotnet workload install maui-ios
dotnet build src/FsHealth.App/FsHealth.App.fsproj -c Debug -f net10.0-ios -r iossimulator-arm64 -p:FsHealthIosOnly=true
dotnet build src/FsHealth.App/FsHealth.App.fsproj -c Debug -f net10.0-ios -r iossimulator-arm64 -p:FsHealthIosOnly=true -t:Run
```

Use `iossimulator-x64` for an Intel Mac. The minimum iOS version is 15. Simulator Keychain entitlements and the required Apple privacy manifest are included. Physical-device builds use `ios-arm64` and require your bundle identifier, Apple development team, provisioning, and signing identity. Configure those locally; no credentials are stored in this repository. Native iOS execution and signing must be verified on a Mac.

### Mac Catalyst (Mac)

```text
dotnet workload install maui-maccatalyst
dotnet build src/FsHealth.App/FsHealth.App.fsproj -c Debug -f net10.0-maccatalyst -r maccatalyst-arm64 -p:FsHealthMacCatalystOnly=true
dotnet build src/FsHealth.App/FsHealth.App.fsproj -c Debug -f net10.0-maccatalyst -r maccatalyst-arm64 -p:FsHealthMacCatalystOnly=true -t:Run
```

Use `maccatalyst-x64` for an Intel Mac. The deployment target is Mac Catalyst 17.0 (macOS 14 Sonoma or later), matching the current .NET 10 Apple SDK minimum. iOS and Mac Catalyst share a registered `MauiUISceneDelegate` and privacy manifest, with separate platform hosts and scene manifests. Both use a single window, keeping one journal owner. See [Microsoft's Apple scene lifecycle guidance](https://devblogs.microsoft.com/dotnet/migrate-dotnet-maui-apps-uikit-scene-lifecycle/).

The Mac target includes App Sandbox and an app-specific Keychain access group in `Platforms/MacCatalyst/Entitlements.plist`. Configure a signing identity and provisioning profile compatible with that group on your Mac; unsigned compilation does not verify Keychain access. If the bundle identifier changes, rebuild with the matching signing configuration. The app uses the same encrypted journal, native share sheet, and Fabulous interface on all platforms. See [Mac Catalyst entitlements](https://learn.microsoft.com/en-us/dotnet/maui/mac-catalyst/entitlements?view=net-maui-10.0).

To create a release app for Apple Silicon, use `dotnet publish src/FsHealth.App/FsHealth.App.fsproj -c Release -f net10.0-maccatalyst -r maccatalyst-arm64 -p:FsHealthMacCatalystOnly=true` with your signing settings. Repeat with `maccatalyst-x64` for Intel. Distribution outside the Mac App Store also requires the appropriate Developer ID signing and notarization; store distribution uses your Mac App Store configuration.

### Tests and formatting

These checks need only .NET 10; mobile workloads are not needed:

```text
dotnet tool restore
dotnet fantomas src tests scripts --check
dotnet test tests/FsHealth.Core.Tests/FsHealth.Core.Tests.fsproj -c Release
```

Or use the F# script: `dotnet fsi scripts/Build.fsx check`. Other script actions are `format`, `android`, `ios`, and `maccatalyst`. `dotnet fsi scripts/Build.fsx maccatalyst` selects the Mac's architecture automatically. An optional runtime argument is supported, for example `dotnet fsi scripts/Build.fsx maccatalyst maccatalyst-x64` or `dotnet fsi scripts/Build.fsx ios iossimulator-x64`.

## Design

`src/FsHealth.Core` contains the domain DUs, validated measurements, canonical glucose values, projections, CSV encoder, versioned JSON codec, authenticated encryption, atomic file replacement, and pure MVU update function. Effects describe loading, persistence, sharing, and obtaining a fresh timestamp/identifier. The MAUI project interprets effects and renders Fabulous widgets. Mutable I/O and canvas operations stay at those boundaries.

The model commits a changed journal **only after persistence succeeds**. A failed save retains the editable draft and previous committed data. Writes are serialized by the state machine. Failed decryption or unsupported/corrupt data blocks writes and preserves the existing file. A pending encrypted file is renamed over the original only after its write completes.

## Data and privacy

- The app has no backend, analytics, or sensor permissions. Android backups are disabled. On iOS and Mac Catalyst, encrypted app files and Keychain may be subject to the device’s backup or iCloud settings; this app does not implement synchronization. See [MAUI SecureStorage’s platform behavior](https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/secure-storage?view=net-maui-10.0).
- The encrypted journal is `journal.fsh` in the private app data directory. The key is not saved alongside it. Export regularly: losing the device/key or uninstalling the app can make data unrecoverable.
- CSV export is **unencrypted**, includes notes and timestamps with UTC offsets, escapes quotes/newlines, and mitigates spreadsheet formula injection. Export only to a location or person you intend to share with.
- One exported CSV remains in the app cache until the next export or OS cleanup. Deleting journal entries does not delete exported/shared copies. There is no import/restore interface in this version.
- Timestamps are stored as `DateTimeOffset`, displayed in the device’s local time, and validated against the actual save time. Unit preferences never reinterpret saved canonical glucose values. Conversion uses 18 mg/dL per mmol/L.

Before release, exercise logging, edit/cancel/delete, persistence after relaunch, decimal-comma input, both units, scrolling/keyboard/safe areas, screen readers, rotation/window resizing, and CSV sharing on Android, iOS, and Mac Catalyst. On a Mac, verify Keychain access and signed launch on the intended architecture. Verify signing and current store requirements with your own developer accounts.
