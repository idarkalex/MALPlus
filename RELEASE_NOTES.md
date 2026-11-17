## Video Playback & Fullscreen

- **Portrait lock:** The app is now locked to portrait at all times. Rotating the device no longer tilts the whole UI sideways.
- **Rotation-to-fullscreen for ExoPlayer:** While an AnimeThemes video is playing, rotating the device to landscape automatically enters fullscreen (`SensorLandscape` orientation). Rotating back to portrait exits fullscreen.
- **Fullscreen close button:** A dedicated ✕ button is now overlaid on the fullscreen view (added per-instance inside `LoggingWebChromeClient.OnShowCustomView`), so it only appears when the custom view is actually active. This also fixes the double-button issue that appeared in previous builds.
- **YouTube WebView freeze fix:** Calling `_fullscreenCallback.OnCustomViewHidden()` on `OnHideCustomView` prevents the WebView from locking up after exiting the YouTube player's native fullscreen.
- **ExoPlayer buffer fix:** Corrected the parameter order in `SetBufferDurationsMs` (`minBufferMs`, `maxBufferMs`, `bufferForPlaybackMs`, `bufferForPlaybackAfterRebufferMs`) — the previous inversion caused an `IllegalArgumentException` at startup and stuttery playback; videos now load and play smoothly.

## Season OP/ED Themes

- **Sort order:** Series that have OP/ED themes available are now sorted to the top of the list; series with no themes fall to the bottom.
- **Extended coverage:** The GraphQL query now paginates up to 5 pages (previously 1), surfacing significantly more series per season.
- **Title matching fallback:** Added a secondary title-matching pass so more series are correctly linked to their theme entries even when the primary title differs.
