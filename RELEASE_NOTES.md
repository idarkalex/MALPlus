# Release notes

Notes published with the GitHub Release for each `v*` tag. The workflow attaches
`RELEASE_NOTES.md` verbatim as the release body, so keep the newest version at the top.

## What's new in 2.0.0

### The MAUI port

This is the first release of the standalone **MAL+** app, an independent .NET MAUI rewrite.

* Rebuilt on .NET 11 / .NET MAUI (Android only), with a dark-first interface.
* Discover home page: seasonal, top anime, top manga, adapted to anime, news, and your personal
  watching / reading / completed rows.
* Random anime and random manga with filters.
* Airing countdowns computed from real episode timestamps and refreshed by magnitude, so a
  weekly show is not re-fetched every minute.
* In-app video overlay for trailers, openings and endings.
* One shared poster-grid control, so every list renders the same 3-column layout.
* Bottom navigation with launcher shortcuts and pull-to-refresh throughout.

### Attribution

MAL+ is a port of [MALClient by Drutol](https://github.com/Drutol/MALClient), forked by
[idarkalex](https://github.com/idarkalex/MALClient). Released under the GNU GPL v3.