# 🛠️ MAL+ Development & Build Guide

Welcome! This guide is written to help anyone—including developers with **zero prior Android or mobile experience**—get up and running with building, running, and contributing to **MAL+**.

---

## 📋 Table of Contents
1. [Tech Stack Overview](#1-tech-stack-overview)
2. [Prerequisites & Environment Setup](#2-prerequisites--environment-setup)
   - [Step 1: Install .NET 11 SDK](#step-1-install-net-11-sdk)
   - [Step 2: Install .NET MAUI Workload](#step-2-install-net-maui-workload)
   - [Step 3: Install Java Development Kit (JDK 21)](#step-3-install-java-development-kit-jdk-21)
   - [Step 4: Install Android SDK & Command-Line Tools](#step-4-install-android-sdk--command-line-tools)
3. [Connecting an Android Device (Recommended)](#3-connecting-an-android-device-recommended)
4. [Building and Deploying the App](#4-building-and-deploying-the-app)
   - [The Quick Development Loop (`build_and_deploy.ps1`)](#the-quick-development-loop-build_and_deployps1)
   - [Manual Command-Line Build](#manual-command-line-build)
5. [Project Architecture & Where to Start](#5-project-architecture--where-to-start)
6. [Common Pitfalls & Troubleshooting (FAQ)](#6-common-pitfalls--troubleshooting-faq)

---

## 1. Tech Stack Overview

MAL+ is written in C# and XAML on top of **.NET 11** and **.NET MAUI (Multi-platform App UI)** targeting Android.

| Component | Technology | Description |
|---|---|---|
| **Language & Runtime** | C# 13 / .NET 11 | High-performance managed runtime |
| **UI Framework** | .NET MAUI | Native Android views (`ViewPager2`, `RecyclerView`) |
| **Media Engine** | AndroidX Media3 (ExoPlayer) | Seamless OP/ED video playback |
| **Architectural Pattern** | MVVM | Clean separation of business logic and UI |

---

## 2. Prerequisites & Environment Setup

### Step 1: Install .NET 11 SDK
MAL+ pins a preview .NET 11 SDK in `global.json` (`11.0.100-rc.1`+ with `allowPrerelease: true`).

1. Download the **.NET 11 SDK** installer or binaries from the [.NET Download Archive](https://dotnet.microsoft.com/download/dotnet/11.0).
2. Verify installation in your terminal:
   ```bash
   dotnet --version
   ```
   *Tip: Ensure your `global.json` is respected and reports an 11.x preview version.*

### Step 2: Install .NET MAUI Workload
Install the MAUI Android workload into your .NET 11 SDK:
```bash
dotnet workload install maui-android
```
*(On Windows, run your terminal as Administrator if required).*

### Step 3: Install Java Development Kit (JDK 21)
Android compilation requires **JDK 21**. We recommend **Microsoft OpenJDK 21** or **Eclipse Temurin 21**:
- [Download Microsoft Build of OpenJDK 21](https://learn.microsoft.com/en-us/java/openjdk/download)
- Set the `JAVA_HOME` environment variable pointing to your JDK directory:
  - **Windows:** `setx JAVA_HOME "C:\Program Files\Microsoft\jdk-21.0.x"` (or your custom path).
  - Ensure `%JAVA_HOME%\bin` is in your `PATH`.

### Step 4: Install Android SDK & Command-Line Tools
You do **not** need Android Studio running in the background to build MAL+, but you need the Android SDK platform and tools:

1. **Option A (Via Android Studio):**
   - Install Android Studio.
   - Open *Settings > Appearance & Behavior > System Settings > Android SDK*.
   - In **SDK Platforms**, install:
     - `Android 13.0 (API 33)`
     - `Android 14.0 (API 34)`
     - `Android 15.0 (API 35)`
   - In **SDK Tools**, ensure **Android SDK Build-Tools 35+**, **Android SDK Command-line Tools**, and **Android SDK Platform-Tools** (`adb`) are installed.
2. **Option B (Standalone command-line tools):**
   - Download the Command line tools from [developer.android.com](https://developer.android.com/studio#command-tools).
3. Set your environment variables:
   - `ANDROID_HOME`: Path to your Android SDK (e.g., `%LOCALAPPDATA%\Android\Sdk` on Windows).
   - Add `%ANDROID_HOME%\platform-tools` to your `PATH` so `adb` is accessible anywhere.
4. Verify `adb`:
   ```bash
   adb version
   ```

---

## 3. Connecting an Android Device (Recommended)

Physical Android devices provide the truest representation of smooth 120Hz gesture navigation, swipe interactions, and ExoPlayer rendering.

### Enable Developer Options on your phone:
1. Go to **Settings > About Phone**.
2. Tap **Build Number** 7 times until you see *"You are now a developer!"*.
3. Go to **Settings > Developer Options** (or *System > Developer Options*).
4. Enable **USB Debugging** (and optionally **Wireless Debugging**).

### Connect via USB or Wireless ADB:
- **USB:** Plug your phone into your computer. Accept the *"Allow USB debugging?"* prompt on your screen.
- **Wireless Debugging:**
  - Turn on *Wireless Debugging* in Developer Options.
  - Look at the IP address and port shown (e.g., `192.168.1.50:43539`).
  - Connect from your terminal:
    ```bash
    adb connect 192.168.1.50:43539
    ```
- Check that your device is detected:
  ```bash
  adb devices
  ```
  *(It should output your device as `device`, not `unauthorized`).*

---

## 4. Building and Deploying the App

### The Quick Development Loop (`build_and_deploy.ps1`)

We provide a streamlined PowerShell script at the root of the repository that performs an optimized incremental compile, transfers the APK via adb, and launches it on your device in ~40 seconds.

```powershell
# Automatically detects your device and tools:
.\build_and_deploy.ps1

# Or specify a target wireless device if multiple are connected:
.\build_and_deploy.ps1 -Device "192.168.1.50:43539"
```

Useful switches for `build_and_deploy.ps1`:
- `-SkipBuild`: Skips compilation and re-installs/launches the existing APK.
- `-SkipInstall`: Compiles without deploying.
- `-NoLaunch`: Installs without automatically starting the activity.

### Manual Command-Line Build

If you want to build using the standard .NET CLI:

```bash
# Debug APK:
dotnet build MALPlus/MALPlus.csproj -f net11.0-android37.0 -p:Configuration=Debug

# Release APK (optimized with R8/linker):
dotnet build MALPlus/MALPlus.csproj -f net11.0-android37.0 -p:Configuration=Release
```

Once built, manually install the APK located at `MALPlus/bin/Debug/net11.0-android37.0/com.idarkalex.malplus-Signed.apk`:
```bash
adb install -r MALPlus/bin/Debug/net11.0-android37.0/com.idarkalex.malplus-Signed.apk
```

---

## 5. Project Architecture & Where to Start

MAL+ is divided into two main projects:

```
MALPlus/
├── MALPlus.Core/             # Platform-agnostic C# business logic
│   ├── XShared/
│   │   ├── BL/               # AiringInfoProvider, seed import, data caches
│   │   ├── Comm/             # API queries (MAL, AniList, AnimeThemes, Tenrai)
│   │   ├── ViewModels/       # MVVM ViewModels (AnimeDetails, Calendar, Library, etc.)
│   │   └── Utils/            # Settings, time zone conversions, string helpers
│
└── MALPlus/                  # MAUI Application (Android presentation)
    ├── Controls/             # Custom UI controls (PosterCard, PosterGrid, BottomNavBar)
    ├── Views/                # XAML Pages (DiscoverPage, AnimeListPage, CalendarPage, etc.)
    ├── Services/             # ThemeService, ExoPlayer handlers, popups
    └── Platforms/Android/    # Android Manifest, MainActivity, native ViewPager adapters
```

### Where to start if you want to contribute:
- **No Android/mobile experience?** Start in **`MALPlus.Core`**!
  - It contains pure C# logic: parsing API responses, calculating countdowns, filtering lists, improving offline caching, or adding new sort/filter options.
  - You can write unit tests or test ViewModels without touching Android-specific APIs.
- **Want to improve UI/XAML?** Look into **`MALPlus/Views`** and **`MALPlus/Controls`**.
  - All styling uses centralized colors and modern squircle chips (`TabChipHelper`).
  - XAML hot reload and layouts follow standard MAUI patterns.

---

## 6. Common Pitfalls & Troubleshooting (FAQ)

### Q: Why did the app crash with `SIGABRT` right after launch?
**Cause:** In .NET MAUI Debug builds, if assemblies are not embedded in the APK (`EmbedAssembliesIntoApk=false`), the fast-deployment daemon fails to synchronize assemblies into the app's private directory.
**Fix:** Keep `EmbedAssembliesIntoApk=true` in `MALPlus.csproj` (this is the default).

### Q: Why does compilation take 5+ minutes when I run `dotnet build -t:Rebuild`?
**Cause:** `Rebuild` discards MSBuild's incremental compilation state and forces regeneration of thousands of C# and XAML dependency files.
**Fix:** **Never run `-t:Rebuild`**. Run standard incremental builds (`build_and_deploy.ps1` or `dotnet build`). Incremental builds take ~40 seconds.

### Q: `adb` says `device offline` or `connection refused` on wireless debugging.
**Cause:** When Android reconnects to Wi-Fi or developer options are restarted, Android assigns a **new random port** to the wireless debug session.
**Fix:** Check your phone's *Wireless Debugging* screen for the updated port number and run:
```bash
adb connect 192.168.1.XXX:NEW_PORT
```

### Q: How do I view live logs while debugging?
Filter logcat to MAL+ application tags:
```bash
adb logcat -s MALPLUS:V DOTNET:I AndroidRuntime:E
```

---

## 💬 Getting Help
If you get stuck at any step:
- Open an issue or join the discussion on [GitHub Issues](https://github.com/idarkalex/MALPlus/issues).
- We're excited to help new contributors get their development environment running!
