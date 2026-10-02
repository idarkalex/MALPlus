# Tenrai API — Referencia de uso en MAL+

Tenrai (`https://api.tenrai.org/v1`) is a free **Unofficial** REST API for the full MyAnimeList public catalogue.
It implements the **Jikan v4 schema**, so it can replace Jikan with only a base-URL swap.

This file documents which endpoints MAL+ uses today, which structured endpoints exist that could replace
fragile HTML scraping / third-party feeds, and the exact fields we verified live (2026-09-01).

## Auth & Rate limits (verified from docs)
- Public requests need **no credentials**.
- `X-Server-Key` raises limits but is for server-side only — **MUST NOT be embedded in the mobile client**.
- Public limits: **120 RPM / 4 RPS / 40,000 RPD** per IP.
- On `429` honor `Retry-After`. `TenraiClient` already spaces requests 500ms and retries 4x.

## JSON conventions
- Single-value unknown field → `null`; empty list/nested object → `[]`/`{}`.
- Score unknown → `0`. Dates/timestamps → ISO8601 **UTC**.
- Error envelope: `{ status, type, message, error, path }`.

## SFW filtering
- `?sfw` filters out `R+`/`Rx`/Hentai/Erotica. `?sfw-strict` also filters Ecchi.

---

## Endpoints used by MAL+ today (mapped to source files)

### Catalogue / details
| Endpoint | Field | Use in MAL+ |
|---|---|---|
| `GET /anime?q=&sfw&genres=&order_by=` | search | `AnimeSearchQuery` (`GetPaginatedAsync("anime?...")`) |
| `GET /manga?q=&sfw` | manga search | `MangaSearchQuery` |
| `GET /anime/{id}` | anime details (36 keys, **no** relations/theme/external) | `AnimeDetailsMalQuery.FetchFromTenraiAsync` |
| `GET /manga/{id}` | manga details | `AnimeDetailsMalQuery` (manga) |
| `GET /anime/{id}/full` | full details (**41 keys**, incl. relations/theme/external/streaming/moreinfo) | `AnimeGeneralDetailsQuery.BuildFromFullData` (currently only ~15 fields read) |
| `GET /manga/{id}/full` | manga full details | `AnimeGeneralDetailsQuery` |
| `GET /anime/{id}/themes` | OP/ED strings | `AnimeDetailsMalQuery` (separate 2nd call) |
| `GET /anime/{id}/episodes` | episode list: `mal_id,url,title,title_japanese,title_romanji,aired(ISO UTC),filler,recap,forum_url,score` | `AnimeEpisodesQuery.GetEpisodes` / `GetLastEpisodesAsync` |
| `GET /anime/{id}/characters` | characters (with `voice_actors`) | `AnimeCharactersStaffQuery` |
| `GET /manga/{id}/characters` | manga characters | `AnimeCharactersStaffQuery` |
| `GET /anime/{id}/staff` | staff (with `positions`) | `AnimeCharactersStaffQuery` |
| `GET /anime/{id}/reviews` | reviews | `AnimeReviewsQuery.FetchReviewsFromTenraiAsync` |

### Discovery
| Endpoint | Use in MAL+ |
|---|---|
| `GET /seasons/{year}/{season}` (or `/seasons/now`) | `AnimeSeasonalQuery` |
| `GET /seasons` | `AnimeListViewModel` (available seasons list) |
| `GET /anime?producers=&order_by=score` / `?genres=` | `AnimeGenreStudioQuery` |

---

## Endpoints NOT used today (structured replacements available)

These structured endpoints would eliminate fragile HTML scraping or third-party feeds:

| Endpoint | Replaces | Notes (verified) |
|---|---|---|
| **`GET /schedules`** | `mylovelyvps.xyz airing.json` + AniList `AnimeAniListScheduleQuery` | Returns **~297 airing series** with full object: `mal_id,title,images,status="Currently Airing",airing=true,broadcast{day,time,timezone,string},aired{from,to,string},genres,score`. Today the app used a 129-item feed lacking titles + an AniList GraphQL fallback. |
| **`GET /anime/{id}/full` `.relations` / `.theme` / `.background` / `.external` / `.streaming` / `.moreinfo` / `.explicit_genres` / `.demographics`** | `AnimeRelatedQuery` (HTML scraper) + separate `/themes` call + missing detail fields | Single call already returns all of these; `BuildFromFullData` ignores them. |
| **`GET /top/anime`** | `AnimeTopQuery` HTML scraper (`topanime.php`) | Full object per entry (`mal_id,title,images,type,episodes,score,rank,members,genres,status`). `filter=airing|upcoming|bypopularity|favorite`, `type=tv|movie|ova|special|ona|music|cm|pv|tv_special`. |
| **`GET /top/manga`** | `AnimeTopQuery` HTML scraper (`topmanga.php`) | `filter=publishing|upcoming|bypopularity|favorite`, `type=manga|novel|lightnovel|oneshot|doujin|manhwa|manhua`. NOTE: singular type values (scraper used plural). |
| `GET /anime/{id}/relations` | (or use `.relations` from `/full`) | Available standalone too. |
| `GET /anime/{id}/statistics` | — | Not used. |
| `GET /anime/{id}/pictures` | — | Not used. |
| `GET /anime/{id}/news` / `articles` | MAL articles scrape | Available. |
| `GET /anime/{id}/recommendations` | `AnimeDirectRecommendationsQuery`/`AnimePersonalizedRecommendationsQuery` (scrapers) | Available structured. |
| `GET /genres/anime` / `/genres/manga`, `/magazines` | — | Available. |

---

## Verified JSON shapes (live, 2026-09-01)

### `GET /anime/{id}/full` (41 keys)
```
mal_id, url, images{jpg,webp}, trailer, approved, titles[], title, title_english,
title_japanese, title_synonyms, type, source, episodes, status, airing, aired{from,to,prop,string},
duration, rating, score, scored_by, rank, popularity, members, favorites, synopsis, background,
season, year, broadcast{day,time,timezone,string}, producers[], licensors[], studios[],
genres[], explicit_genres[], themes[], demographics[], relations[{relation,entry[]}],
theme{openings[],endings[]}, external[{name,url}], streaming[], moreinfo
```
`/anime/{id}` (simple) has the same minus `relations, theme, external, streaming, moreinfo`.

Relations entry objects: `{ mal_id, type: "anime"|"manga", name, url, media_type, images{jpg} }`.

### `GET /schedules` (paginated; `filter=monday|...|sunday`, `sfw`)
Each item is the full anime object incl. `broadcast.day` (`"Mondays"`), `broadcast.time` (`"21:00"`),
`broadcast.timezone` (`"Asia/Tokyo"`), `broadcast.string` (`"Mondays at 21:00 (JST)"`), and
`aired.from`/`aired.to` ISO4217 datetimes.

### `GET /anime/{id}/episodes`
```
data: [ { mal_id, url, title, title_japanese, title_romanji, duration, aired: "2026-07-06T00:00:00+00:00",
         score, filler, recap, synopsis, replies, forum_url, images } ]
pagination: { last_visible_page, has_next_page }
```
`aired` is a **UTC ISO datetime** — ideal for exact next-episode countdown.

### `GET /top/anime` / `GET /top/manga`
Pagination `{ last_visible_page, has_next_page, current_page, items{...} }`; each entry is the full
catalogue object (same keys as `/anime/{id}`).

---

## Rate-limit budget notes
- `/schedules` is cached 8h in `AiringInfoProvider` → ~1 request per 8h. Cheap.
- Switching `AnimeDetailsMalQuery` from 2 calls (`/anime/{id}` + `/themes`) to 1 (`/full`) reduces load.
- All other calls run through `TenraiClient`'s 500ms spacing + 4x retry (within 4 RPS / 120 RPM).

Refer also to the refactor summary in `AGENTS.md` and the cache-version bump rule (`mal_details_v3_*` →
`v4` when fields added; no in-place migration).

---

## The four remaining HTML scrapers (audited 2026-10-01, deliberate)

Standing rule: **Tenrai > MAL API > HTML scrape**, and HTML is only allowed when the other two
genuinely cannot answer the question. Four queries still scrape MAL HTML. Every one of them was
checked against both alternatives with live requests, and each was kept because the data the UI
needs does not exist elsewhere. Do not "fix" these by pointing them at an endpoint that merely
returns 200.

| Query | Why it stays HTML |
|---|---|
| `AnimeRecomendationsQuery` | Needs **pairs** (A → B) plus the user's written reason. Tenrai has no pairs. |
| `AnimePersonalizedRecommendationsQuery` | Pairs personalised from the user's own list. Nothing else has it. |
| `AnimeAdaptedToAnimeQuery` | The global "manga with an anime adaptation" listing. No endpoint exists. |
| `PopularVideosQuery` | `myanimelist.net/watch/promotion` promos. Not in MAL API at all. |

### Trap 1 — invented filter parameters are ignored, not rejected
`/manga?adapted=true`, `?has_anime=true`, `?source=anime`, `?adaptation=anime` all return
**HTTP 200 with the generic top manga list** (first entry "Monster"). A migration written against
one of these looks like it works and silently serves the wrong data. Always check the payload,
not the status code.

`/anime?source=manga` *is* honoured (returns One Piece for `status=airing`), but that yields the
**anime**, whereas this screen must show **manga**. Wrong direction of the relation.

### Trap 2 — `recommendations.php` pairs vs Tenrai's per-anime list
`AnimeRecomendationsQuery` parses `recommendations.php?s=recentrecs&t=anime`, which yields **100
pairs**, each with the recommender's own sentence and username.

Measured on the same entry (`Another`, id 11111), MAL pair 1 of the page was:

```
A origen   : Another  [anime/11111]
descripcion: "Again, poor students trying to survive some mystery-horror scenario.
              Gore and screams, all just like 'Another'."
```

Tenrai `GET /anime/11111/recommendations` returns **106 items for that single anime**:

```
Higurashi no Naku Koro ni   votes=79
Shiki                       votes=50
Corpse Party: Tortured Souls votes=48
...
item keys:  entry, url, votes
entry keys: mal_id, url, images, title
```

| | MAL HTML | Tenrai |
|---|---|---|
| Items | 100 **pairs** | 106, for **one** anime |
| Written reason | yes | **no** |
| Recommender username | yes | **no** |
| Votes | no | **yes** |
| A → B pair | yes | **no** |

So Tenrai is *richer* (votes) but cannot render this screen at all: the feature is "pairs and why".
Switching sources is a **product change**, not a data-source swap.

### Trap 3 — the MAL API v2 does have forum endpoints (correcting an earlier claim)
Forums were previously documented here as having no API. That was wrong. Verified live:

| Endpoint | Result |
|---|---|
| `GET /v2/forum/boards` | 200 but `data: []` (**empty**) |
| `GET /v2/forum/topics?board_id=1` | 200, 10 items (`id,title,created_at,number_of_posts,...`) |
| `GET /v2/forum/topic/{id}` | **404** — no message bodies |

Forums therefore stay HTML: the API can list boards/topics but **cannot deliver post content**, so
migrating would half-work and degrade the forums.

### Trap 4 — these MAL API v2 endpoints do not exist (404, not 401)
`/anime/{id}/recommendations`, `/anime/{id}/videos`, `/anime/{id}/reviews`,
`/users/@me/animerecommendations`, `/manga/adapted`, `/watch/promotion` all return **404** with a
valid client id. Auth-required endpoints return **401** (`/users/@me` → `invalid_token`), so 404
means "no such endpoint".

Note `/anime/{id}/reviews` is 404 in MAL API but **is** available on Tenrai, which is why
`AnimeReviewsQuery` already uses Tenrai.

### Unauthenticated response shape
Without an OAuth token, MAL API v2 answers `/anime/{id}` with only `id,title,main_picture` — no
score, genres, studios or stats. Any new query built on it needs a token, so "just use the API"
is never a way around a login problem.
