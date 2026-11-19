# MAL+ v0.7.4

## Broadcast Countdown & Aired Episodes Badges ([#3](https://github.com/idarkalex/MALPlus/issues/3))
- **Poster broadcast badges:** Added combined aired episode and broadcast countdown badges to the bottom-right of anime posters (e.g., `EP 8/12 · 22H` or `EP 1122/? · Airing`). Closes [#3](https://github.com/idarkalex/MALPlus/issues/3).
- **Granular display settings:** Added new toggles in Settings to independently enable or disable the aired episode count and broadcast countdown.
- **Lowercase 'Airing' tag:** Cleaned up the airing status indicator when an episode is currently broadcasting.

## Themes & Display Options ([#2](https://github.com/idarkalex/MALPlus/issues/2))
- **AMOLED Display Profiles:** Added dedicated dark themes including Charcoal (pure black with elevated sheets) and Real Dark (100% true black for OLED/AMOLED screens). Closes [#2](https://github.com/idarkalex/MALPlus/issues/2).
- **Dynamic theme switching:** Theme transitions now dynamically update all active surfaces, chips, shell pagers, and system navigation bars without restarting the app.

## Broadcast Calendar Overhaul & Fixes
- **Unified Squircle Day Chips:** Integrated Option A squircle chips (`Mon`, `Tue`, `Wed`, etc.) with item count indicators and automatic smooth-centering scroll.
- **Airing schedule accuracy:** Verified and synchronized broadcast dates and upcoming episode numbers across all days of the week against official schedules.
- **Freeze & blank screen fix:** Resolved a recursive task loop in countdown resolution that previously caused UI thread starvation and empty calendar days.
- **Thread-safety in card refresh:** Safely marshaled background airing updates to the main Android UI thread.
- **Instant mode switching:** Fast, reliable toggling between global Broadcast Calendar and personal "My Schedule" (episodes airing this week from your library).

## Episode Counter Stepper ([#4](https://github.com/idarkalex/MALPlus/issues/4))
- **Dynamic stepper update:** Tapping `+` or `−` next to watched episodes on the anime details page now immediately updates the counter on screen without requiring navigation back and forth. Closes [#4](https://github.com/idarkalex/MALPlus/issues/4).

## Design System & Navigation
- **Option A Tab Chips:** Applied consistent squircle tab chips across Forums, Articles, Profile, History, Friends, and Clubs.
- **Global lateral swipe:** Smooth tab switching via swipe gestures across stacked and root pages (`ITabSwipeHost`).
- **Modern Settings redesign:** Reorganized and restyled Settings into clean, categorized sections with interactive option sheets.
