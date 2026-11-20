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
- `order_by`: Extended fields: `title_english`, `title_japanese`, `rank`, `popularity`, `added` (`added` works only with approved anime), and new **trend metrics**:
  - `score_change_30d`: Net change in score over the past 30 days.
  - `members_gained_7d`: Net change in members over the past 7 days (weekly momentum).
  - `score_spread`: Standard deviation / score disagreement (surfaces divisive or polarizing titles).
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

#### 🆕 Top anime parameter additions (Nov 2026)
Added to `/top/anime`. Ranking order unchanged:
- `status`, `status_exclude` — Filter/exclude by airing status.
- `type_exclude` — Exclude selected types (opposite of `type`).
- `rating_exclude` — Exclude selected rating buckets server-side.
- `source`, `source_exclude` — Filter/exclude by source material (e.g. `manga`, `light_novel`, `original`).
- `year`, `min_year`, `max_year` — Filter by calendar year the anime started airing.
- `season`, `season_year` — Filter by MAL's debut season and its year.
- `min_score`, `max_score` — Filter by score.
- `min_scored_by`, `max_scored_by` — Filter by number of scoring users.
- `min_reviews`, `max_reviews` — Filter by review count.
- `has_trailer` — Filter by trailer availability.
- `has_relations` — Filter by whether the entry has related anime/manga.
- `streaming_service` — Filter by streaming provider (`crunchyroll`, `netflix`, `hidive`, etc.). Case-insensitive, known aliases accepted. Does not verify regional availability.

#### 🆕 Top manga parameter additions (Nov 2026)
Added to `/top/manga`. Ranking order unchanged:
- `status`, `status_exclude` — Filter/exclude by publication status.
- `type_exclude` — Exclude selected manga types.
- `year`, `min_year`, `max_year` — Filter by calendar year publishing started.
- `min_score`, `max_score` — Filter by score.
- `min_scored_by`, `max_scored_by` — Filter by number of scoring users.
- `solo_author` — Filter by whether one person both wrote and drew it.
- `has_relations` — Filter by whether the manga has related anime/manga entries.
- `min_reviews`, `max_reviews` — Filter by review count.

#### 🆕 Top episodes parameter additions (Nov 2026)
- `anime_ids` — Only episodes from the anime whose MAL IDs you choose (comma-separated).
- `aired_after`, `aired_before` — Filter by episode air date.
- `year`, `min_year`, `max_year` — Filter by air year.
- `min_episode`, `max_episode` — Filter by episode number.
- `filler` — Filter by MAL filler flag.
- `recap` — Filter by MAL recap flag.
- `min_duration`, `max_duration` — Filter by episode length in seconds.
- `min_votes`, `max_votes` — Filter by the number of votes in the episode's discussion poll.
- `max_per_anime` — Limit results to N episodes per anime.
- `exclude_ids` — Exclude anime (and all their episodes) by MAL ID.
- Anime-level filters: `type`, `type_exclude`, `rating`, `rating_exclude`, `status`, `status_exclude`, `source`, `source_exclude`, `genres`, `genres_exclude`, `season`, `season_year`, `min_anime_score`, `max_anime_score`, `min_anime_members`, `max_anime_members`, `min_anime_episodes`, `max_anime_episodes`.
- Sorting: `order_by` adds `rank` (default), `aired`, `duration`, `title`, `title_japanese`, `title_romanji`; `sort` applies as usual.
- `nulls` — Control placement of items missing the sorted value (`first` or `last`). Now also works without `order_by`, on the default ranking.

#### 🆕 Top videos parameter additions (Nov 2026)
- `min_views`, `max_views` — Filter by YouTube view count.
- `min_likes`, `max_likes` — Filter by YouTube like count.
- `min_dislikes`, `max_dislikes` — Filter by estimated dislike count.
- `min_comments`, `max_comments` — Filter by comment count.
- `published_after`, `published_before` — Filter by upload date.
- `uploaded_within` — Only videos uploaded in the last `7d`, `30d`, or `90d`.
- `max_per_anime` — Limit results to N videos per anime.
- Sorting: `order_by` adds `duration`; `nulls` applies; `order_by=title` now puts untitled videos last.

#### 🆕 Top people parameter additions (Nov 2026)
- `sfw`, `sfw-strict` — Filter out people who only worked on non-SFW entries.
- `credit` — Only people with a voice, staff or author credit (`voice`, `staff`, `author`).
- `position`, `position_exclude` — Only people with, or without, any of these staff jobs, such as `director`, `music`, `producer`.
- `author_role` — Only authors MAL credits for `story` or `art`.
- `airing`, `upcoming`, `publishing` — Only people credited in at least one anime airing now, one not yet aired, or one manga publishing now; or in none.
- `season`, `season_year` — Only people credited in an anime of these debut seasons or years.
- `voice_language`, `voice_language_exclude` — Only voice actors whose main language (the one most of their roles are in) is, or is not, one of these (e.g. `japanese`, `english`).

#### 🆕 Top character parameter additions (Nov 2026)
- `sfw`, `sfw-strict` — Filter out characters that only appear in non-SFW entries.
- `role_only` — Only characters MAL lists as main or supporting and never in the other role.
- `airing`, `upcoming`, `publishing` — Only characters in at least one anime airing now, one not yet aired, or one manga publishing now; or in none.
- `season`, `season_year` — Only characters in an anime of these debut seasons or years.

**Integration opportunities in MAL+:**
- `AnimeTopQuery` currently builds query strings via `GetAnimeApiQuery`/`GetMangaApiQuery`. The new `source`, `season`, `season_year`, `streaming_service`, `year` and score-range params can back richer filter chips in the Top Anime/Manga UI with zero extra plumbing.
- `max_per_anime` on `/top/videos` and `/top/episodes` is immediately useful for the Videos page to avoid one anime dominating the feed.
- `uploaded_within=30d` on `/top/videos` can power a "Recent PVs" surface in Discover.

### 2. Manga Adapted to Anime (`/manga/adapted`)
- **`GET /manga/adapted`**
  - Dedicated endpoint listing exclusively manga series that have received an anime adaptation.
  - **Replaces legacy HTML scraping (`AnimeAdaptedToAnimeQuery`).**
  - Supported parameters:
    - `filter`: `airing` (manga whose adapted anime is currently airing), `upcoming` (manga whose adapted anime is upcoming), or omitted for all adapted manga.
    - `status`: Filter by publication status. Now accepts several comma-separated values (e.g. `status=publishing,complete`, `status=finished`).
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

#### 🆕 Schedule endpoint additions (Nov 2026)
New parameters added to `/schedules`:
- `filter` — Now also accepts **`today`** and **`tomorrow`**, evaluated relative to midnight **Japan time (JST)**. Useful for a "today's airing" view without client-side day calculation.
- `source`, `source_exclude` — Filter by, or exclude, selected source materials (e.g. `manga`, `light_novel`, `original`, `game`).
- `streaming_service` — Only anime with a streaming link from any of these providers (`crunchyroll`, `netflix`, `hidive`, etc.).
- `voice_language` — Only anime with a voice cast credited in any of these languages (e.g. `japanese`, `english`).
- `type_exclude` — Exclude selected anime types (opposite of `type`). E.g. `type_exclude=music,cm,pv`.
- `status`, `status_exclude` — Filter by airing status (`currently_airing`, `finished_airing`, etc.) or exclude selected statuses.
- `rating_exclude` — Exclude selected rating buckets on the server (e.g. `rating_exclude=rx`). Complements the existing `rating_exclude` on seasonal endpoints.
- `exclude_ids` — Comma-separated MAL IDs to omit from results (e.g. exclude already-watched series from the schedule view).

**Sorting:**
- `order_by` — Sort results by `mal_id`, `title`, `title_english`, `title_japanese`, `start_date`, `end_date`, `episodes`, `score`, `scored_by`, `rank`, `popularity`, `members`, `favorites`, or `added`.
- `sort` — `asc` (default) or `desc`. Only applies when `order_by` is set.
- `nulls` — Control placement of entries missing the sorted value (`first` or `last`). Requires `order_by`.

**Integration opportunity in MAL+:**
- `AnimeSchedulesQuery.GetScheduleAsync()` currently fetches all pages and filters client-side. The `filter=today` / `filter=tomorrow` shortcut can back a "Today's Airing" summary card in Discover.
- `order_by=score&sort=desc` on the schedule could power a "Best airing today" surface without extra ranking logic.
- `exclude_ids` with the user's completed/dropped IDs could auto-hide finished shows from the airing view.

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
  - `sfw`, `sfw-strict` on `/characters/{id}/full` — Filter out non-SFW anime and manga, and voice actors who only appear in non-SFW anime.
  - `sfw`, `sfw-strict` on `/characters/{id}/voices` — Filter out voice actors who only appear in non-SFW anime.
- **`GET /people`:** Search voice actors and creators by `anime`, `manga`, `characters`, `credit` (`voice`, `staff`, `author`), `position`, `voice_language`, `min_voice_roles`.
- **`GET /producers`:** Search studios by `role` (`studio`, `producer`, `licensor`), `worked_with`, `airing`, `min_anime`, `debut_after`, `established_after`.
- **`GET /magazines`:**
  - `sfw`, `sfw-strict` — Count only manga that pass the SFW filter.

### 7. Community Reviews, News & Articles
- **`GET /news` & `GET /articles`:**
  - `tag`: Filter by tag name (now accepts several comma-separated values).
  - `tag_exclude`: Exclude posts with any of these tags.
  - `anime_ids`, `manga_ids`: Restrict items to specific anime or manga IDs.
  - `people_ids`, `character_ids`: Restrict articles to specific people or characters (`/articles` only).
  - `author_username`: Filter by author.
  - `published_after`, `published_before`: Date range filtering.
  - `order_by`: Sort by `published_at` (default), `comments`, `title`, or `mal_id`.
  - `sfw`, `sfw-strict` on `/news/tags` and `/articles/tags` — Count only posts that pass the SFW filter.

- **`GET /news/{id}` & `GET /articles/{id}` (Text format):**
  - `format`: Return text as `markdown`, `html`, or `both` (default is `both`).
  - Response envelope: `data.content.markdown` and/or `data.content.html`.
  - **MAL+ Integration:** Directly integrated into `MalArticleQuery` (`MALPlus.Core/XShared/Comm/Articles/MalArticleQuery.cs`). When opening any MAL news or featured article, the app requests `format=html` to obtain sanitized, responsive article HTML directly from Tenrai without downloading the entire desktop page or running fragile web scrapers.

- **`GET /anime/{id}/reviews` & `GET /manga/{id}/reviews` (Title Reviews):**
  - **Reviewer & Post Filters:**
    - `score`, `min_score`, `max_score`: Filter by reviewer's given score (1-10).
    - `published_after`, `published_before`: Filter by date posted. Now also take an ISO 8601 timestamp with a time zone (e.g. `2026-11-20T20:00:00+01:00`).
    - `sentiment`: Filter by reviewer sentiment (accepts several values).
    - `min_reactions`, `max_reactions`: Filter by review's total reaction count.
    - `reaction`, `min_reaction_count`: Require specific reaction kinds and minimum counts.
  - **Review Sorting:**
    - `order_by`: Sort by `score` (reviewer score) or one of the seven reaction kinds (`nice`, `love_it`, `funny`, `confusing`, `informative`, `well_written`, `creative`), highest first. The selected sort breaks ties.
    - `nulls`: Control placement of reviews missing the sorted value (`first` or `last`).

- **`GET /reviews/anime` & `GET /reviews/manga` (Global Reviews Catalogue):**
  - Full cross-title reviews catalogue supporting all reviewer/post filters above plus:
    - `published_after`, `published_before`: Date range filtering (supports timestamps with time zones).
    - `anime_ids`, `manga_ids`: Restrict reviews to specific MAL IDs.
    - `exclude_ids`: Omit reviews belonging to selected MAL IDs.
    - `max_per_entry`: Cap number of reviews returned per anime/manga (e.g. `max_per_entry=1` for diverse feeds).
    - `type`, `type_exclude`: Filter/exclude by entry format (`tv`, `movie`, `manga`, etc.).
    - `status`, `status_exclude`: Filter/exclude by airing/publishing status.
    - `genres`, `genres_exclude`: Filter/exclude by genre, theme, or demographic IDs.
    - **Anime Review Filters (`/reviews/anime`):**
      - `rating`, `rating_exclude`: Filter by, or exclude, selected age ratings (`g`, `pg`, `pg_13`, `r17`, `r`, `rx`).
      - `season`, `season_year`: Filter by the anime's debut season and year.
      - `source`, `source_exclude`: Filter/exclude by source material.
      - `producers`, `producers_exclude`: Filter/exclude by producer/studio MAL IDs.
    - **Manga Review Filters (`/reviews/manga`):**
      - `magazines`, `magazines_exclude`: Filter by, or exclude, magazine or serialization-platform MAL IDs.
      - `authors`, `author_role`: Filter by author MAL IDs, optionally restricted to `story` or `art` role.

### 8. Interest Stacks (`/stacks`, `/anime/{id}/stacks`, `/manga/{id}/stacks`)
Thematic user-curated interest stacks from MyAnimeList:
- `author_username`: Only stacks by this curator.
- `has_notes`, `min_noted_entries`: Filter by whether/how many entries have a curator note.
- `has_author_scores`, `min_scored_entries`: Filter by whether/how many entries have a curator score.
- `min_entries`, `max_entries`: Filter by stack entry count.
- `min_restacks`, `max_restacks`: Filter by restack (bookmark/save) count.
- `created_after`, `created_before`: Filter by stack creation date.
- `max_per_author`: Show at most this many stacks from each curator.
- `anime_ids`, `manga_ids`: Restrict stacks to those containing any of these anime or manga (`/stacks` only).
- `order_by`: Adds `entry_count`, `title`, and `mal_id` (plus relevance).

### 9. Trend Metrics & Momentum Sorting (Nov 2026)
Added to `/anime`, `/manga`, `/manga/adapted`, `/schedules`, and all seasonal lists (`/seasons/{year}/{season}`, `/seasons/now`, `/seasons/previous`, `/seasons/upcoming`):

- **`order_by=members_gained_7d`**: Net change in member count over the past 7 days. Reflects real-time community momentum and current viewing trends. (Integrated in MAL+ Discover Hub as the primary driver for "Trending this week").
- **`order_by=score_change_30d`**: Net change in community rating over the past 30 days. Surfaces sleeper hits and rapidly improving series.
- **`order_by=score_spread`**: Standard deviation of user ratings. Highlights controversial, divisive, or conversation-sparking titles.

*Note: Trend metrics are computed daily from Tenrai's historical analytics database.*

### 10. Random Pick Endpoints Overhaul (`/random/*`) (Nov 2026)
Tenrai provides single-shot randomized discovery endpoints with full filter parity:

#### `GET /random/anime`
- `q`: Search query string; returns a randomized entry chosen from the matching search results.
- `letter`: Restricts picks to titles starting with this alphanumeric character.
- `status`: Accepts multiple status values (e.g. `status=currently_airing,finished_airing`).
- `min_id`, `max_id`: Only pick from entries whose MAL ID is in this numerical range.
- `type_exclude`, `status_exclude`, `rating_exclude`, `source_exclude`, `producers_exclude`: Server-side exclusions.
- `streaming_service`: Filter by platform (`crunchyroll`, `netflix`, `hidive`, etc.).
- `voice_language`: Filter by credited voice cast language (e.g. `english` dubs).
- `has_relations`: `true` for franchise entries, `false` for standalone/new IPs.
- Numerical bounds: `min_scored_by`, `max_scored_by`, `min_members`, `max_members`, `min_episodes`, `max_episodes`, `min_reviews`, `max_reviews`, `min_favorites`, `max_favorites`.
- Dates: `season`, `season_year`, `start_date`, `end_date`, `added_after`, `added_before`.

#### `GET /random/manga`
- `q` & `letter`: Random search results or starting letter.
- `status`: Accepts multiple status values.
- `min_id`, `max_id`: Only pick from entries whose MAL ID is in this numerical range.
- `type_exclude`, `status_exclude`, `magazines_exclude`, `authors_exclude`: Clean exclusions.
- `magazines`: Serialized in specific magazine/platform IDs.
- `solo_author`: `true` for single creator (written & drawn by same person), `false` for multi-author collaborations.
- `has_relations`: Filter by franchise/relation presence.
- Numerical bounds: `min_scored_by`, `max_scored_by`, `min_members`, `max_members`, `min_chapters`, `max_chapters`, `min_volumes`, `max_volumes`, `min_reviews`, `max_reviews`, `min_favorites`, `max_favorites`.
- Dates: `start_date`, `end_date`.

#### `GET /random/characters`
- `letter`: Character name initial.
- `min_id`, `max_id`: Only pick from characters whose MAL ID is in this numerical range.
- Profile completeness: `has_about`, `has_image`, `has_nicknames`, `has_kanji`, `has_family_name`.
- Entry & Role: `anime` or `manga` ID filter, `role` (`main` or `supporting`), `appears_in` (`anime`, `manga`, or `both`).
- Status & Airing: `airing`, `upcoming`, `publishing` (in currently broadcasting anime or publishing manga), `season`, `season_year`.
- Voice Cast: `voice_language`, `voice_language_exclude`, `has_voice_actor`.
- Content: `sfw`, `sfw-strict`.

#### `GET /random/people`
- `letter`: Person name initial.
- `min_id`, `max_id`: Only pick from people whose MAL ID is in this numerical range.
- Bio completeness: `has_about`, `has_image`, `has_birthday`, `has_website`, `has_alternate_names`, `has_family_name`.
- Entries & Roles: `anime` or `manga` ID filter, `credit` / `credit_exclude` (`voice`, `staff`, `author`), `position` / `position_exclude` (e.g. `director`).
- Career & Timing: `airing`, `upcoming`, `publishing`, `season`, `season_year`, `active_after`, `active_before`, `debut_after`, `last_credit_before`.
- Voice: `voice_language`, `voice_language_exclude`.
- Content: `sfw`, `sfw-strict`.

---

## 🛠️ Block 3: HTML Scraper Audit & Replacement Roadmap

MAL+ adheres to the hierarchy: **Tenrai API &gt; Official MAL API &gt; HTML Scraping**. HTML scraping is strictly reserved for cases where structured API data does not exist.

With recent Tenrai capabilities, the status of legacy scrapers is as follows:

| Feature / Legacy Scraper | Legacy Method | Modern Tenrai Replacement | Migration Status |
|---|---|---|---|
| **News & Articles Reader** (`MalArticleQuery`) | HTML scraping (`div.news-container`) | `GET /news/{id}?format=html`<br>`GET /articles/{id}?format=html` | **Migrated & Integrated:** Tenrai provides clean, sanitized article HTML directly in `data.content.html` without loading heavy web pages. |
| **Manga Adapted to Anime** (`AnimeAdaptedToAnimeQuery`) | HTML scraping | `GET /manga/adapted` | **Migrated & Integrated:** Tenrai REST API `GET /manga/adapted` completely replaces HTML scraping. Supports `filter=airing`, `filter=upcoming`, and all adapted manga with multi-status support and automatic SFW filtering. |
| **Popular & Promotional Videos** (`PopularVideosQuery`) | HTML scraping (`/watch/promotion`) | `GET /v1/top/videos` | **Ready for migration:** Tenrai provides structured YT video entries. |
| **Community Recommendations** (`AnimeRecomendationsQuery`) | HTML scraping (`recommendations.php`) | `GET /top/recommendations` | **Ready for migration:** Provides structured recommended pairs. |
| **Weekly Airing Schedules** (`AiringInfoProvider`) | Third-party feeds | `GET /schedules` | **Ready for migration:** Provides official broadcast timestamps and timezone data. |
| **Interest Stacks Collections** | None / Web view | `GET /stacks`<br>`GET /anime/{id}/stacks`<br>`GET /manga/{id}/stacks` | **Ready for UI:** First-class API available for curated anime/manga stacks. |
| **Forums & Discussions** | HTML scraping | None (stays HTML) | **Kept as HTML:** The official MAL API does not expose post message bodies (returns 404/empty). |
