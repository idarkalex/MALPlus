## Discover & Exploration Redesign
- Redesigned Discover Hub: Transformed Discover with native vector category cards, borderless modern styling, and optimized vertical spacing.
- Direct Search Access: Integrated functional search bar directly into Discover that transitions smoothly into search results.
- Daily Hidden Gem: Converted the featured recommendation into a daily rotating gem with clean single-line descriptions and responsive layout.
- Trending & Recommendations Visual Overhaul: Redesigned DiscoveryResultsPage and RecommendationsPage with unified EmSheet containers, squircle artwork corners, circular action chips, and streamlined subtitles.

## Global Search Experience
- Dedicated "All" Global Tab: Introduced a top-level "All" search tab categorizing results across Anime, Manga, Characters, Staff, and Studios with clean dividers and 10-item previews.

## Top Lists & Subcategories Stability
- In-Place Filter Navigation: Changing subcategories (e.g., OVAs, Popular, Movies) in Top Anime and Top Manga now updates the view model in place, avoiding redundant Shell navigation and keeping the persistent bottom bar visible.
- Subcategory Stream Order Fix: Filtered Top lists (Popular, Favourited, Airing, Upcoming, etc.) now strictly preserve the source category order across paginated loads rather than re-sorting by global score rank.
- Query Parameter Correction: Resolved malformed query string parameter issues on Top Manga Popular and Favourited endpoints.
- Left-Aligned Dropdown Popup: Aligned the top selector dropdown popup completely flush against the display edge with tailored corner geometry and zero offset gaps.

## Network & Freshness
- Tenrai Seasonal Optimizations: Added server-side content filter support (`rating_exclude=rx`) and updated API documentation.
- Cache Invalidation: Bumped internal top list cache versions to immediately serve cleanly sorted data.
