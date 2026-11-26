# MAL+ v0.8.0

## Visual Studio & Interactive Live Preview
- **First-Launch & Update Showcase:** A guided visual setup welcomes first-time users and returning updaters, letting you tailor how MAL+ looks right from the start.
- **Live Interactive Preview:** Displays real anime posters inside a live app frame that updates immediately as you switch themes, grid columns, spacing, poster badges, title language, or start page.
- **Featured Access in Settings:** Reopen **Visual Studio (LIVE PREVIEW)** anytime from the top of **Settings** or **Appearance & Display**.

## Live Theme Switching & Redesigned Settings
- **Instant Runtime Themes:** Switching between **Midnight Blue**, **Dark AMOLED**, and **Real Dark** now updates active pages, cards, navigation bars, bottom sheets, and system bars immediately without restarting the app.
- **Subcategory Settings Hub:** Organized Preferences into focused categories (*Appearance & Display*, *Poster Cards & Badges*, *Library & Tracking*, *Broadcast Calendar*, *Sharing & Social*, and *Data & Storage*) and fixed selector sheets exiting subcategories.

## Poster Grid & Visual Polish
- **Grid Columns & Optional Spacing:** Pick your preferred mobile grid density (**Auto**, **2**, **3**, **4**, or **5** columns) and toggle between seamless (`0dp`) or spaced (`8dp`) poster grids, including Related cards.
- **Cleaner Default Posters:** Posters start minimal with just the library status/progress badge; score chips, format tags, release season, aired episodes, and countdown badges can all be toggled live.
- **Unified Details Styling:** Aligned colors, typography, star icons, spoiler blocks, and trailer controls across Anime Details, Characters, Staff, Reviews, and Recommendations, plus stronger lower-hero contrast for readable white titles over bright artwork.

## Social Share Cards, Feeds & Discovery
- **Cleaner Recommendation Cards:** *Recommend to friends* share cards omit empty personal progress bars and display total episode/chapter counts cleanly.
- **In-Page Share Preview:** Sharing from lists or details keeps the bottom navigation bar visible underneath.
- **Feeds, Random & Calendar:** Added activity filters in Friends Activity, shared Random Anime/Manga discovery controls, and refactored Calendar/Discover flows.

## Windows Desktop Edition & Diagnostics
- **Windows Desktop Edition:** Added native WinUI 3 / .NET 11 MAUI desktop support with a dark-themed Inno Setup installer (`MALPlus-Setup-v0.8.0.exe`), self-contained single-file packaging, multi-column desktop grids, AnimeThemes OP/ED playback, OAuth login fixes, and cross-platform in-app auto-updates.
- **Desktop Live Preview & Smooth Scrolling:** Fixed desktop live preview poster aspect ratios (supporting up to 10 posters on wider windows) and eliminated mid-scroll grid resets, recycled artwork duplication, and partial rows on long lists.
- **Privacy-First Local Diagnostics:** Replaced remote telemetry with 100% local on-device diagnostic logging, startup tracing, and scroll/cache optimizations.
