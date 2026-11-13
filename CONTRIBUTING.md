# Contributing to MAL+ (MALPlus)

Thank you for your interest in contributing to **MAL+**! This project is a modern, high-performance MyAnimeList client for Android built with **.NET 11** and **.NET MAUI**.

---

## 🛠️ Development Environment

To build and run MAL+ locally, you will need:
- **.NET 11 SDK** (preview/RC version pinned in `global.json`)
- .NET MAUI Android workload (`dotnet workload install maui-android`)
- **Android SDK** (API platforms 33, 34, 35, and 37 preview)
- **JDK 21** (Microsoft OpenJDK recommended)
- A physical Android device with wireless or USB debugging enabled (recommended; software emulators do not accurately reflect gesture and animation performance).

---

## 🚀 Local Build & Deploy Loop

Always use the incremental build script at the repository root:

```powershell
.\build_and_deploy.ps1 -Device "192.168.1.XXX:PORT"
```

> ⚠️ **Important:**
> - **Never** run `dotnet build -t:Rebuild` during normal development: it discards incremental compilation state and regenerates thousands of source files. Incremental builds take ~40 seconds.
> - Keep `EmbedAssembliesIntoApk=true` in Debug builds to avoid fast-deployment crashes (`SIGABRT`).

---

## 📁 Repository Structure

| Directory | Purpose |
|---|---|
| `MALPlus` | MAUI Android application: XAML pages, native platform controls, ViewPager2 adapters, custom handlers, and Android manifest. |
| `MALPlus.Core` | Platform-independent business logic: MVVM ViewModels, API clients (MAL, Tenrai, AnimeThemes), domain models, and caching layer. |
| `docs` | Technical documentation and architectural migration notes. |

---

## 📋 Contribution Guidelines

1. **Branches and commits:**
   - Active development takes place on the `main` branch.
   - Keep commits focused on a single change with descriptive messages.
2. **Privacy and telemetry:**
   - MAL+ is **100% private and local**. No third-party analytics, crash beacons, or tracking libraries are permitted.
   - All logging must use `AppLog` for on-device storage only.
3. **Shell and Navigation Invariants:**
   - Root navigation (`Discover`, `Anime`, `Manga`, `More`) is constructed in C# and coordinated with the custom `BottomNavBar`. Review `AGENTS.md` before making changes to page navigation or tab switching.
4. **Submitting issues:**
   - Use the official GitHub issue forms: [Report a Bug](.github/ISSUE_TEMPLATE/bug_report.yml) or [Suggest a Feature](.github/ISSUE_TEMPLATE/feature_request.yml).

---

## 📜 License

MAL+ is licensed under the **GNU General Public License v3** (GPLv3). See [LICENSE.md](LICENSE.md).
