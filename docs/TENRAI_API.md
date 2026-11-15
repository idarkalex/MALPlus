# Tenrai API — Reference & Integration Guide for MAL+

Tenrai (`https://api.tenrai.org/v1`) is a free, high-performance unofficial REST API for the MyAnimeList catalogue. It adheres to the **Jikan v4 schema**, enabling drop-in compatibility while offering specialized extensions, batch endpoints, and advanced filtering options.

---

## ⚙️ Core API Guidelines

### Authentication & Rate Limits
- **Public Requests:** No authentication or API keys required.
- **Server Key:** The `X-Server-Key` header is intended strictly for server-side environments and **must never be embedded in the mobile client**.
- **Public Rate Limits:**
  - 120 requests per minute (RPM)
  - 4 requests per second (RPS)
  - 40,000 requests per day (RPD) per client IP
- **Backoff & Throttling:** On `429 Too Many Requests`, clients must honor the `Retry-After` header. `TenraiClient` in MAL+ automatically enforces a 500 ms spacing between requests and handles retries with exponential backoff.

### Conventions & Envelope
- **Unknown/Missing Values:** Single unknown fields return `null`. Empty lists/objects return `[]` or `{}`.
- **Scores:** Unknown scores default to `0`.
- **Timestamps:** Dates and timestamps use ISO 8601 in **UTC**.
- **Error Response Shape:**
  ```json
  {
    "status": 404,
    "type": "BadResponseException",
    "message": "Resource not found",
    "error": "Not Found",
    "path": "/v1/anime/..."
  }
  ```

### Content Filtering (SFW)
- `?sfw=true`: Filters out adult content (`R+`, `Rx`, Hentai, Erotica).
- `?sfw-strict=true`: Applies strict filtering, additionally excluding Ecchi content.

---

## 📦 Block 1: Endpoints Integrated in MAL+

The following endpoints are currently active in MAL+ and mapped to their respective query and service classes:

### Catalogue & Details
| Endpoint | Description | MAL+ Source Implementation |
|---|---|---|
| `GET /anime?q={query}&sfw&genres=&order_by=` | Anime search with filters | `AnimeSearchQuery` |
| `GET /manga?q={query}&sfw` | Manga search with filters | `MangaSearchQuery` |
| `GET /anime/{id}` | Lightweight anime details (basic fields) | `AnimeDetailsMalQuery.FetchFromTenraiAsync` |
| `GET /manga/{id}` | Lightweight manga details | `AnimeDetailsMalQuery` (manga path) |
| `GET /anime/{id}/full` | Comprehensive anime details (includes themes, relations, streaming, external) | `AnimeGeneralDetailsQuery.BuildFromFullData` |
| `GET /manga/{id}/full` | Comprehensive manga details | `AnimeGeneralDetailsQuery` |
| `GET /anime/{id}/themes` | Opening and ending theme songs | `AnimeDetailsMalQuery` |
| `GET /anime/{id}/episodes` | Full episode list (with titles, air dates, filler/recap flags) | `AnimeEpisodesQuery` |
| `GET /anime/{id}/characters` | Character roster with voice actor credits | `AnimeCharactersStaffQuery` |
| `GET /manga/{id}/characters` | Manga character roster | `AnimeCharactersStaffQuery` |
| `GET /anime/{id}/staff` | Production staff and credited roles | `AnimeCharactersStaffQuery` |
| `GET /anime/{id}/reviews` | User reviews for a title | `AnimeReviewsQuery.FetchReviewsFromTenraiAsync` |

### Discovery & Schedules
| Endpoint | Description | MAL+ Source Implementation |
|---|---|---|
| `GET /seasons/{year}/{season}` | Seasonal anime listing for a specific season | `AnimeSeasonalQuery` |
| `GET /seasons/now` | Currently airing season listing | `AnimeSeasonalQuery` |
| `GET /seasons/previous` | Preceding season listing | `AnimeSeasonalQuery` |
| `GET /seasons/upcoming` | Upcoming announced season listing | `AnimeSeasonalQuery` |
| `GET /seasons` | Available seasons archive | `AnimeListViewModel` |
| `GET /anime?producers={id}&order_by=score` | Anime filtered by studio or producer | `AnimeGenreStudioQuery` |
| `GET /anime?genres={id}` | Anime filtered by genre | `AnimeGenreStudioQuery` |

#### Seasonal Anime Endpoints (`/seasons/*`) Improvements & Parameters
Added to `/seasons/{year}/{season}`, `/seasons/now`, `/seasons/previous`, and `/seasons/upcoming`:
- `filter_exclude`: Comma-separated types to exclude (e.g. `filter_exclude=music,cm,pv`).
- `status`: Multiple status filter (e.g. `status=currently_airing,finished_airing`). *Note: not available on `/seasons/upcoming`.*
- `status_exclude`: Statuses to exclude (e.g. `status_exclude=not_yet_aired`). *Note: not available on `/seasons/upcoming`.*
- `rating_exclude`: Ratings to exclude (e.g. `rating_exclude=rx` to omit adult content on the server without client-side dropping).
- `source` & `source_exclude`: Filter by source material (e.g. `manga`, `light_novel`, `original`, `game`).
- `has_trailer=true|false`: Filter by presence of trailer.
- `has_relations=true|false`: Filter entries with relationships to other entries (`has_relations=false` for standalone/new IPs).
- `streaming_service`: Filter by platform (`crunchyroll`, `netflix`, `hidive`, etc.).
- `producers` & `producers_exclude`: Filter by studio, producer, or licensor MAL IDs.
- `staff`: Filter by credited MAL person IDs.
- `characters` & `character_role`: Filter by character MAL IDs and specify role (`main` or `supporting`).
- `voice_actors` & `voice_language`: Filter by voice actor MAL IDs and language.
- `broadcast_day`: Day of week in Japan (`monday`..`sunday`), or dynamic keywords `today` and `tomorrow` (evaluated relative to midnight JST).
- `continuing`: Accepts `only` for continuing multi-cour/perpetual TV series (e.g. *One Piece*), allowing clean separation from seasonal debuts.
- Numerical range bounds: `min_score`, `max_score`, `min_scored_by`, `max_scored_by`, `min_reviews`, `max_reviews`, `min_favorites`, `max_favorites`.

**Seasonal Anime Sorting & Nulls:**
- `order_by`: Extended fields: `title_english`, `title_japanese`, `rank`, `popularity`, `added` (`added` works only with approved anime).
- `nulls`: Controls placement of items missing the sorted property (`first` or `last`). Requires `order_by`.

**Integration in MAL+:**
- `AnimeSeasonalQuery` automatically requests `order_by=popularity` by default, ensuring the first page rendered by `FirstPageReady` contains the most relevant shows immediately.
- `AnimeSeasonalQuery` and `ContentFilter` incorporate `rating_exclude=rx` on seasonal requests when `Settings.ContentFilter == ContentFilterMode.Mature`, letting Tenrai filter adult content on the wire.
- Distinct SHA1 cache hashing via `CacheTagFor(year, season, extraQuery)` ensures all filtered seasonal variations remain isolated without cache collisions.

---

## 🚀 Block 2: Available Endpoints for Future Integration

The following endpoints and capabilities are available in Tenrai API and can be integrated into MAL+ to replace legacy scrapers, enhance discovery, or optimize network payloads:

### 1. Top Rankings & Curated Highlights
- **`GET /top/episodes`**
  - Browse anime episodes ranked by community discussion-poll ratings.
  - Parameters: Default weighted ranking (average score + vote volume), `order_by=score` (minimum 100 votes), `order_by=votes`, `order_by=comments`.
- **`GET /top/recommendations`**
  - Community-recommended anime and manga pairs.
  - Replaces fragile HTML pair scrapers.
- **`GET /top/videos`**
  - Anime promotional videos (PVs) and creditless music videos ranked by YouTube statistics.
  - Replaces external video scrape routines.
- **`GET /top/anime` & `GET /top/manga`**
  - Native paginated rankings with extended parameter filters:
    - `filter`: `airing`, `upcoming`, `bypopularity`, `favorite`
    - `min_members`, `max_members`, `min_episodes`, `min_chapters`, `min_volumes`
    - `genres`, `genres_exclude`, `sort=asc|desc`
    - `exclude_ids`: Exclude titles already in user's library (up to 1,000 IDs).

### 2. Manga Adapted to Anime (`/adapted`)
- **`GET /adapted`**
  - Dedicated endpoint listing exclusively manga series that have received an anime adaptation.
  - **Replaces legacy HTML scraping (`AnimeAdaptedToAnimeQuery`).**
  - Supported parameters:
    - `exclude_ids`: Exclude up to 1,000 IDs.
    - `type_exclude`, `status_exclude`, `genres_exclude`
    - `magazines`, `magazines_exclude`: Filter by magazine or serialization platform.
    - `solo_author`: `true` for single creator, `false` for multi-creator teams.
    - `year`, `min_year`, `max_year`
    - Bounds: `min_members`, `max_members`, `min_volumes`, `max_volumes`, `min_chapters`, `max_chapters`, `min_scored_by`, `max_scored_by`, `min_favorites`, `max_favorites`, `min_reviews`, `max_reviews`.
    - Sorting: `order_by` (`title_english`, `title_japanese`, `nulls` handling).

### 3. Schedules (`/schedules`)
- **`GET /schedules`**
  - Full weekly airing broadcast schedule with precise timezones (`broadcast.day`, `broadcast.time`, `broadcast.timezone`, `broadcast.string`).
  - Parameters: `filter` (`monday` ... `sunday`), `type`, `rating`, `genres`, `genres_exclude`, `exclude_ids`.
  - Replaces external third-party schedule JSON feeds.

### 4. High-Performance Batch Lookups
- **`GET /anime/{id}/full?ids={id1},{id2},...` & `GET /manga/{id}/full?ids=...`**
  - Request full details for up to **25 IDs in a single HTTP request** (1 primary + 24 batched).
  - Supports `sfw=true` across all batched titles.
  - Supports `mode=deleted` and `mode=denied` archive lookups for offline cache reconciliation.

### 5. Advanced Search & Filtering Parameters
Available on both `/anime` and `/manga`:
- `ids` & `exclude_ids`: Restrict or exclude searches to a specific set of up to 1,000 IDs (ideal for list cross-referencing).
- `min_scored_by` & `max_scored_by`: Filter by number of scoring users.
- `min_members` & `max_members` / `min_favorites` & `max_favorites`
- `year`, `min_year`, `max_year` (accepts multiple years, up to 25).
- `characters` & `character_role`: Filter titles where specific character IDs appear as main or supporting.
- `producers` & `producers_exclude` (anime) / `authors` & `authors_exclude` (manga).
- `broadcast_day`: Filter by air day in Japan (`today`, `tomorrow`, or day of the week).
- `streaming_service`: Filter by platform (`crunchyroll`, `netflix`, `hidive`, etc.).
- `has_trailer=true|false` & `has_relations=true|false`.
- Sorting extensions: `title_japanese`, `title_english`, `order_by=added` (with `sort=desc`).

### 6. Expanded Character, People & Producer Endpoints
- **`GET /characters`:** Search with filters: `anime`, `manga`, `role`, `role_only`, `appears_in=anime|manga|both`, `airing`, `voice_actors`, `voice_language`, `min_appearances`, `min_main_roles`.
- **`GET /people`:** Search voice actors and creators by `anime`, `manga`, `characters`, `credit` (`voice`, `staff`, `author`), `position`, `voice_language`, `min_voice_roles`.
- **`GET /producers`:** Search studios by `role` (`studio`, `producer`, `licensor`), `worked_with`, `airing`, `min_anime`, `debut_after`, `established_after`.

### 7. Community Reviews, News & Articles
- **`GET /news` & `GET /articles`:** Search with `order_by=published_at|comments`, `published_after`, `published_before`, and `author_username`.
- **`GET /reviews/anime` & `GET /reviews/manga`:** Filter reviews directly by reviewer `username`.

---

## 🛠️ Block 3: HTML Scraper Audit & Replacement Roadmap

MAL+ adheres to the hierarchy: **Tenrai API &gt; Official MAL API &gt; HTML Scraping**. HTML scraping is strictly reserved for cases where structured API data does not exist.

With recent Tenrai capabilities, the status of legacy scrapers is as follows:

| Feature / Legacy Scraper | Legacy Method | Modern Tenrai Replacement | Migration Status |
|---|---|---|---|
| **Manga Adapted to Anime** (`AnimeAdaptedToAnimeQuery`) | HTML scraping | `GET /adapted` | **Ready for migration:** Fully supported by Tenrai with rich filtering and sorting. |
| **Popular & Promotional Videos** (`PopularVideosQuery`) | HTML scraping (`/watch/promotion`) | `GET /v1/top/videos` | **Ready for migration:** Tenrai provides structured YT video entries. |
| **Community Recommendations** (`AnimeRecomendationsQuery`) | HTML scraping (`recommendations.php`) | `GET /top/recommendations` | **Ready for migration:** Provides structured recommended pairs. |
| **Weekly Airing Schedules** (`AiringInfoProvider`) | Third-party feeds | `GET /schedules` | **Ready for migration:** Provides official broadcast timestamps and timezone data. |
| **Forums & Discussions** | HTML scraping | None (stays HTML) | **Kept as HTML:** The official MAL API does not expose post message bodies (returns 404/empty). |
