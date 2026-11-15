## Navigation & Shell
- Unified Tab Header Chips: Standardized squircle 8dp chip styling across all multi-tab screens (Articles, Forums, Profile, Search, History, Friends, Clubs, Messages, Notifications, Recommendations).
- Global Lateral Swiping: Enabled horizontal swipe navigation (ITabSwipeHost) across multi-page sections.
- Root Transition & Bottom Bar: Placed the persistent shared bottom navigation bar outside the root pager to eliminate flickering during section switches.
- Edge-to-Edge Experience: Improved native window insets handling for consistent full-screen layout.

## Messaging & Notifications
- Thread Resolution: Resolved message duplication and missing content in thread history.
- Immediate Preview: Messages now display preview content immediately upon selection without requiring a manual refresh.
- Unified Navigation: Added lateral swiping and standard header chips to Messages (Inbox/Sent) and Notifications (All/Airing/Replies/Messages/Others).
- Direct Interaction: Switched list item selection to reliable gesture recognizers on item containers.

## Media & Discover
- Enhanced Video Player: Integrated ExoPlayer support with landscape auto-rotation and interactive media controls for trailers and seasonal themes.
- Discover Layout Stability: Maintained root tabs in memory (OffscreenPageLimit = 3) to prevent blank pages after navigating through More sub-panels.
- Poster Loading: Added asynchronous background poster resolution for History entries without blocking UI rendering.

## Performance & Stability
- Enforced SdkOnly link mode on Android builds to eliminate JIT latency spikes.
- Normalized route resets so bare navigation routes consistently restore the primary user library.
- Hardened HTML and query parsers against malformed network responses.
