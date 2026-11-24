# MAL+ v0.7.7

## Windows Desktop Edition (Public Preview)
- **Dedicated Windows Installer:** Introducing the official modern dark-themed Inno Setup installer (`MALPlus-Setup-v0.7.7.exe` / `MALPlus-Setup-x64.exe`) with customizable installation paths, Start Menu shortcuts, and desktop icons.
- **Self-Contained Single-File Architecture:** Packages runtime components and dependencies into a unified executable (`MALPlus.exe`), running natively on WinUI 3 and .NET 11 MAUI without requiring external .NET or WebView2 dependencies.
- **High-Density Desktop Grids:** Expanded multi-column responsive poster layouts tailored for desktop screens, displaying 8+ items per row with smooth container virtualization and fast image decoding.
- **Integrated AnimeThemes Player:** Native desktop creditless OP/ED music video playback with interactive controls.
- **Reliable Uninstaller:** Fully cleans up installation folders, program files, and temporary caches, with an interactive prompt to optionally preserve or purge stored user credentials and offline databases.

## Authentication & Login Flow
- **OAuth Authorization Fix:** Resolved the `400 Bad Request` issue on MyAnimeList's consent dialog by allowing standard browser navigation through the OAuth approval lifecycle.
- **Keyboard Enter Support:** Pressing Enter in the username or password fields now triggers sign-in directly without needing mouse interaction.

## UI Polish & Navigation
- **Topic Header Clean-up:** Removed redundant mobile refresh icons from forum thread headers when running on Windows desktop.
- **Responsive Layout Stability:** Resolved infinite layout measurement loops and stutter when scrolling large anime and manga libraries ("All" category).
- **Batch Collection Rendering:** Optimized `SmartObservableCollection` to deliver atomic change notifications, preventing WinUI 3 container recycling desynchronization and interface freezes.

## Auto-Update Engine
- **Cross-Platform In-App Updates:** Integrated GitHub release discovery that automatically detects new versions, streams downloads with live progress indication, and launches the Windows installer seamlessly.

---

# MAL+ v0.7.6

## Official Website
- **MAL+ Web is Live:** Explore features, view documentation, and download the latest updates at [malplus.vercel.app](https://malplus.vercel.app).

## Social Sharing & Activity Updates (#6)
- **Floating Share Button:** An unobtrusive button appears at the bottom-right whenever you log an episode, update chapter progress, or change a status, letting you quickly share your milestone.
- **Customizable Social Settings:** New dedicated section in Settings to adjust the button duration (10s–30s), customize sentence templates for anime and manga milestones, or add an optional hashtag.
- **Unified Share Menu in Details:** Consolidated all sharing and copy tools into the header Share button (recommend to friends, progress, link only, and clipboard actions).
- **Poster Menu Integration:** Added a "Share" option to the grid flyout menu when holding down any anime or manga poster in your list.
- **Smart Score Detection:** Entries without a score automatically omit score text rather than showing placeholders or 0/10.
- **Cleaner Menus:** Removed duplicate share and copy actions from the three-dots menu to keep it focused on entry details.

## Library Sorting & Per-List Memory (#5)
- **Per-List Sort Memory:** Sorting preferences are now remembered independently for Anime and Manga lists based on community feedback.
- **Reset Per-List Sorts:** Added a quick reset option in Settings to return all custom list orderings back to default.
- **Poster Options Sheet:** Polished back navigation and dismiss transitions across compact sheets.

## Visual Branding & App Experience
- **Refreshed App Icons:** Updated high-resolution launcher icons across all screen densities.
- **Release Notes Reader:** Revamped the in-app changelog dialog with styled headers, formatted bullet points, and expanded dimensions.
- **Optimized Assets:** Cleaned up splash screen and vector assets for a lighter and crisper app package.
