# μPhotos fork: repository review

Nov 1, 2026 · @ismail özenç

## Summary

The breaking indexing bug is fixed: GPS photos failed to index once `cities.csv` was removed, and all GPS handling is now gone. The other high-priority items are that the compare view ignores private, deleted and hidden folders, and that bulk delete rewrites the whole index once per photo.

Reviewed at commit `0687b1e` ("csv removed"): 72 tracked files, 46 C# files, about 13,000 lines. The repo has no tests and has not been compiled in this review, so line numbers are from source reading only.

| Area | Count | Most important |
| --- | --- | --- |
| Errors and likely bugs | 9 | Compare view ignores private, deleted and hidden folders |
| Code quality and performance | 8 | Full-image decode on the render thread |
| User experience | 10 | Slideshow Escape closes one screen only |
| Feature upgrades | 12 | Shift-click range select, Empty deleted images |
| Security, build, packaging | 7 | Private folders are UI-only; installer paths broken |

## Errors and likely bugs

The first two rows break or leak data today; fix them first.

| # | Severity | Where | Problem | Fix |
| --- | --- | --- | --- | --- |
| 1 | Resolved | `Photos.Core/StoredTypes.cs` | GPS photos failed to index after `cities.csv` was removed. | GPS handling, the city lookup, `Spatial.cs` and the stored GPS values are removed. |
| 2 | High | `Photos.Core/CompareView.cs:57` | `LiveSignatures()` reads `DB.ListFiles()` directly. Private photos (while locked), deleted photos and hidden folders all appear in the compare view and in the ranking. | Build the set from the display order, or apply the same three checks. |
| 3 | High | `Photos.Core/Storage.cs:171`, `PhotoProvider.cs:495` | Each moved copy calls `SaveFiles()` (full `index.bin` rewrite) and saves settings. Deleting 500 photos writes the index 500 times. | Batch: mark dirty and save once after `RunOnCopies`. |
| 4 | Medium | `PreviewWindow.cs:54`, `SlideshowWindow.cs:106` | Full images are read and decoded on the main render thread. Each preview click or slide change freezes every window for the decode time. | Decode on a background task; show the previous image or the thumbnail until ready. |
| 5 | Medium | `WinFormsDialogs.cs:30`, `:63` | Dialogs block the render thread with `thread.Join()`. While a dialog is open no window redraws, which can leave secondary monitors black. | Keep rendering while the dialog runs; poll for its result each frame. |
| 6 | Medium | `SlideshowWindow.cs:151` | Escape closes only the focused screen; the other screens keep running and the folder/preview windows only return when all are closed. | Escape on any screen stops the whole slideshow. |
| 7 | Low | `PhotoProvider.cs:495`, `:530` | `DeletedOriginals` is never pruned. Files removed or moved outside the app leave stale entries, and the deleted-images row can stay visible with nothing in it. | Prune entries whose trashed file no longer exists, at startup. |
| 8 | Low | `FilterService.cs:182` | Folder rows with only hidden photos are kept. Clicking one shows an empty grid. | Mark such rows as hidden-only, or skip them in the filter. |
| 9 | Low | `DB.Settings = DB.Settings with {…}` (many places) | Read-modify-write of settings from the UI thread and background tasks can drop a change (last write wins). | Add a single `UpdateSettings(Func<SettingsRecord, SettingsRecord>)` with a lock. |

## Code quality and performance

The biggest wins are moving image decodes off the render thread and making the per-photo visibility checks cheap; both matter at the 100k-photo scale the original app targets.

1. **Visibility checks per refresh.** `IsTrashedSource`, `IsPrivateHidden` and `IsExcludedSource` (`PhotoProvider.cs`) build a path string and lower-case it for every copy of every photo on each refresh. Precompute sets of directory indexes once per refresh and test `s.D` against them.
2. **Background decode with prefetch** for the preview, slideshow and compare windows (bug 4). Prefetch the next slide while the current one is shown.
3. **One settings writer.** Replace the scattered `DB.Settings = DB.Settings with {…}` calls with one locked update method (bug 9), and debounce `SaveSettings`.
4. **Batch index saves** for multi-photo actions (bug 3): one `SaveFiles()` and one refresh per action.
5. **Folder list refresh.** `FolderWindow.Advance` rebuilds and sorts all rows every 250 ms. Rebuild only on `OnDisplayOrderUpdate`.
6. **Folder totals** walk the whole tree once per folder row, so a deep library is read many times. Walk each library root once and sum per folder.
7. **Nullable reference types are off** in all projects. Turning them on in `Photos.Core` would catch the null paths around `DB.Directories.GetValue` and `Settings` arrays.
8. **No tests.** `RankingEngine`, `PrivateFolderRules`, `SlideshowPlanner`, `MonitorRoles` and `UniquePath` are pure functions and easy to cover with an xUnit project.

## User experience improvements

Most gaps are about selection and the multi-monitor windows, which were added quickly and lack the small conveniences users expect.

| Area | Improvement |
| --- | --- |
| Selection | Shift-click for ranges, Ctrl+A to select all, Escape to clear, and a visible "N selected" count with a clear button. |
| Selection | Keep the selection across refreshes by storing photo signatures, not grid positions. |
| Double-click | Use the Windows setting (`SystemInformation.DoubleClickTime`) instead of the fixed 450 ms in `ThumbnailView.cs:168`. |
| Slideshow | Escape on any screen stops all screens (bug 6). Show a small pause/next hint on first start. |
| Slideshow | Make the 5-second interval a setting; it is fixed in `SlideshowWindow.cs` and `PhotoView.cs`. |
| Folder list | Keyboard keys work only after clicking into that window; show which window has focus. |
| Folder list | Collapse and expand subfolders; long libraries are a flat list today. |
| Deleted images | Show the count on the folder row and in the menu, and add an "Empty deleted images" action with confirmation. |
| Private folders | Lock again automatically after idle time or when the app is minimised. |
| Monitor roles | Let users pick which screen is main, folders and preview, instead of guessing from shape; remember the choice per display. |

## Feature upgrades

The best-value upgrades build on data the app already has: signatures for duplicates and ratings.

| Feature | Effort | Why |
| --- | --- | --- |
| Duplicate finder view | Small | Copies are already one record with several sources; list photos with 2+ copies and offer "keep one". |
| Rating stars on thumbnails and a rating filter | Small | Ratings are stored but only visible in the photo view. |
| Shift-click range select and Ctrl+A | Small | Completes multi-select. |
| Empty deleted images, restore all | Small | Finishes the deleted-images workflow. |
| Slideshow settings: interval, order (folder, date, random), transition | Medium | Interval and order are fixed today. |
| Copy or move selection to a folder | Medium | Replaces the copy-to-clipboard button that was removed. |
| Drag photos out to Explorer | Medium | Common expectation on Windows. |
| Automatic rescan with `FileSystemWatcher` | Medium | New files appear without "Check for new photos". |
| Camera RAW (`.cr2`, `.nef`, `.arw`, `.raf`) via Magick.NET | Medium | Magick.NET is already a dependency. |
| Configurable private-folder password (hashed) | Small | See Security. |
| Folder roles and layouts saved per monitor set | Medium | Home and office monitor setups differ. |

## Security, build and packaging

Private folders only hide photos in the app's own views; anyone with access to the PC can still see them, so treat the feature as a privacy screen, not protection.

- **Password in source.** `PrivateFolderRules.cs:14` holds `"1234"` as a constant. Store a salted hash (PBKDF2) in settings and add a "Change password" item.
- **Thumbnails on disk.** Private photos' thumbnails sit unencrypted in `large.bin` and `tiny.bin`, and the files stay readable in Explorer. Say so in the UI, or encrypt the private thumbnails.
- **Compare view leak.** Bug 2 shows private photos while locked.
- **Packages.** Magick.NET 14.15.0 and MessagePack 3.1.9 were flagged as vulnerable by NuGet earlier in this work. Run `dotnet list package --vulnerable` and update.
- **Installer.** `setup.iss` and the publish profile were removed. Add an installer again if you need one.
- **README.** It describes features this fork removed: mobile sync, country/city/camera filters and the project website. Rewrite it for the fork's features.
- **Repo name.** `git push` reports the repository moved to `ismailozenc/Photos`; update the `origin` URL.

## Suggested order of work

Fix the indexing crash before the next scan, then the data leaks, then speed; features come after.

- [x] Remove the city/country lookup, `Spatial.cs`, `cities.csv` and all GPS handling (bug 1)
- [ ] Apply private, deleted and hidden checks in the compare view (bug 2)
- [ ] Batch index and settings saves for multi-photo actions (bug 3)
- [ ] Decode preview and slideshow images in the background (bug 4)
- [ ] Keep rendering while dialogs are open (bug 5)
- [ ] Escape stops the slideshow on all screens (bug 6)
- [ ] Hashed, changeable private password; auto-lock
- [ ] Update NuGet packages; rewrite the README
- [ ] Add a test project for the pure helpers
- [ ] Shift-click, Ctrl+A, Escape for selection
- [ ] Empty deleted images; duplicate finder
