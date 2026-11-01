# μPhotos fork: repository review

Nov 1, 2026 · @ismail özenç

## Summary

The most serious open finding is that the photo library is never disposed when the app closes, so a settings change made in the last half second is lost now that saves are delayed. Two privacy gaps and a per-frame cost in the folder list come next.

Reviewed at commit `432642f`, updated after `7ef7081`. No .NET SDK was available, so nothing was compiled or run; every finding comes from reading the source.

| Area | Open | Most important |
| --- | --- | --- |
| Build and compile | 2 risks | SkiaSharp 4 and Magick.NET 15 not yet built |
| Bugs and behaviour errors | 8 | Settings and index not saved on exit |
| Threading and data safety | 4 | Settings handlers run under a lock on background threads |
| Performance | 5 | Deleted-folder lookup rebuilt per row, per frame |
| Code quality | 5 | `PhotoProvider` is over 1,000 lines |
| User experience | 1 | Monitor roles |
| Feature upgrades | 7 | Duplicate finder view |
| Security, tests and packaging | 6 | Password in source; no CI |

## Build and compile

The reported build errors (`IsSelectedFolder` missing, `SKRect` has no `Id`) and the obsolete `DrawText`, `DrawImage` and `DrawBitmap` calls are fixed in `7ef7081`. The six obsolete `SKPath.AddPath` calls are gone too. Two risks remain until a clean Windows build. Nullable reference checking is off in all projects; turning it on in `Photos.Core` produced about 200 warnings, which need annotating by hand with a compiler at hand.

| Risk | Where | What to check |
| --- | --- | --- |
| SkiaSharp 3.119 → 4.154 (major version) | All drawing code; `Game.cs` and `SkiaWindow.cs` GL setup | Removed or renamed APIs |
| Magick.NET 14.15 → 15.0 (major version) | `DesktopImageLoader.cs` (`MagickImage.Read`, `GetPixelsUnsafe().ToByteArray`) | Changed read and pixel APIs |

## Bugs and behaviour errors

The first row loses data and the next two leak private information; fix those three first.

| # | Severity | Where | Problem | Fix |
| --- | --- | --- | --- | --- |
| 1 | High | `Game.cs` (`OnUnload`), `Loader.cs` | The `PhotoProvider` is never disposed on exit. Settings are saved 500 ms after a change, so a change made just before closing is lost; `SaveFilesIfDirty` in `PhotoProvider.Dispose` never runs either. | Call `photoProvider.Dispose()` (or at least `DB.FlushSettings()` and `DB.SaveFilesIfDirty()`) in `OnUnload`. |
| 2 | High | `FolderWindow.cs` | While private folders are locked, their names, subfolder tree and photo counts ("10 not shown") stay visible in the folder list. | Hide private rows while locked, or show one "Private (locked)" row. |
| 3 | Medium | `PhotoProvider` thumbnail caches | Locking hides photos from views, but their thumbnails stay in the in-memory caches. | Clear `_scaledThumbnails` and `_microCache` for private photos on lock. |
| 4 | Medium | `Game.cs` idle tracking | Idle time is reset only by input in the main window. Using the folder list or preview for 5 minutes locks private folders mid-use. | Reset `_idle` from the folder, preview and slideshow windows' input events too. |
| 5 | Medium | `Game.cs` render loop, `PhotoProvider.OnDiskChanged` | Every disk change starts a rescan that ends with a "library refresh completed" toast; the app's own moves to deleted images trigger it too. | Use a silent rescan for watcher-triggered refreshes; ignore changes inside deleted-images folders. |
| 6 | Medium | `PhotoProvider.RefreshWatchers` | `FileSystemWatcher.Error` is not handled. After a bulk copy overflows the event buffer, the watcher stops reporting and no rescan happens. | Handle `Error`: flag a rescan and recreate the watchers. |
| 7 | Low | `FolderWindow.cs` totals | Folder totals are cached for the life of the window and never recounted, so they go stale when files are added or removed. | Clear `_totals` when a watcher-triggered rescan finishes. |
| 8 | Low | `SlideshowController.cs` | The interval is read when the slideshow starts; changing it in settings has no effect until the slideshow restarts. | Read the setting in `DrawAndPresent`, or push the change to running windows. |

## Threading and data safety

The settings lock fixed lost writes, but it runs change handlers while held, and some of those handlers touch UI objects from background threads.

1. **Handlers under the settings lock.** `Storage.UpdateSettings` calls `OnSettingsUpdate` inside the lock. The filter purge in `FilterService.RefreshFilters` runs on the refresh thread, so the slider handler in `MainView.cs` sets `_sizeSlider.Index` off the render thread. Invoke handlers after leaving the lock, and marshal UI work with `Window.Run`.
2. **Display order swapped while it is read.** `postUpdate` replaces `_displayOrder` and `DispalyGroups` on the refresh thread while the grid, preview, slideshow, folder list and selection read them on the render thread. Swap both as one immutable snapshot, on the render thread.
3. **Deleted-image map.** `_deletedMap` is built lazily with `??=` and reset to `null` from several threads. Harmless today because a rebuild gives the same result; it becomes a bug if the map ever holds expensive state.
4. **Full-image reads on the render thread.** `Indexer.GetFullImage` reads the index and the file on the render thread for the preview, slideshow, compare view and prefetch, while the indexer writes to the index on its own thread.

## Performance

The folder list does avoidable work every frame, and the decode pipeline does avoidable work every keypress.

| Where | Cost | Fix |
| --- | --- | --- |
| `FolderWindow.cs` row drawing | `TrashFolderIds` rebuilds a list from every deleted-image path, several times per visible row, 60 times a second | Cache it per refresh, like `_deletedCount` |
| `AsyncImage` superseded requests | Holding Left or Right in the preview starts a full decode plus a prefetch per keypress; superseded decodes run to completion | Pass a cancellation token into the decoder and skip superseded work |
| Prefetch file reads | The next photo is read from disk on the render thread, which can stutter | Read inside the background task, from a path captured on the render thread |
| `PhotoProvider.DeletedCopyCount` | Walks the whole file list each time the folder list refreshes or the main menu opens | Count during `BuildVisibility` and keep the number |
| `EmptyDeleted` | Deletes every trashed file on the render thread; a large deleted-images folder freezes the app | Run it as a background task with progress |

## Code quality and maintainability

The fork grew quickly through many small edits; the cost shows up as a few very large files and inconsistent formatting.

1. **Large classes.** `PhotoProvider.cs` is over 1,000 lines and owns display order, visibility rules, private folders, deleted images, hidden folders and the folder watcher. Moving visibility, deleted images and watching into their own classes would make each testable.
2. **Comment fragments.** About 150 comment-only lines like `//    {` and `//try` remain from the commented-out code removal, for example in `ImageLoader.cs` and `DesktopImageLoader.cs`.
3. **Mixed line endings.** 22 C# files use LF and 34 use CRLF, and there is no `.gitattributes`. Add one with `* text=auto` and `*.cs eol=crlf`.
4. **Settings layout.** `SettingsRecord` has about 20 keys covering private folders, hidden folders, deleted images, ranking and slideshow. Group related keys into small records.
5. **Leftover markers.** Seven `TODO` and `!!!` comments mark known problems that are not tracked anywhere.

## User experience improvements

| Area | Improvement |
| --- | --- |
| Monitor roles | Let users pick which screen is main, folders and preview, instead of guessing from shape; remember the choice per display. |

## Feature upgrades

The best-value upgrades build on data the app already has: signatures for duplicates and ratings.

| Feature | Effort | Why |
| --- | --- | --- |
| Duplicate finder view | Small | Copies are already one record with several sources; list photos with 2+ copies and offer "keep one". |
| Rating stars on thumbnails and a rating filter | Small | Ratings are stored but only visible in the photo view. |
| Slideshow order (folder, date, random) and transitions | Medium | Order and transitions are fixed today; the interval is now a setting. |
| Copy or move selection to a folder | Medium | Replaces the copy-to-clipboard button that was removed. |
| Drag photos out to Explorer | Medium | Common expectation on Windows. |
| Configurable private-folder password (hashed) | Small | See Security. |
| Folder roles and layouts saved per monitor set | Medium | Home and office monitor setups differ. |

## Security, tests and packaging

Private folders only hide photos in the app's own views; anyone with access to the PC can still see them, so treat the feature as a privacy screen, not protection.

- **Password in source.** `PrivateFolderRules.cs` holds `"1234"` as a constant. Store a salted hash (PBKDF2) in settings and add a "Change password" item.
- **Thumbnails on disk.** Private photos' thumbnails sit unencrypted in `large.bin` and `tiny.bin`, and the files stay readable in Explorer. Say so in the UI, or encrypt the private thumbnails.
- **No CI.** A GitHub Actions workflow on `windows-latest` running `dotnet build` and `dotnet test` on each push would catch compile errors the moment they land.
- **Thin tests.** `Photos.Tests` (xUnit v3) has 10 tests covering private-folder rules, the slideshow planner and two ranking cases. Storage upgrades, visibility rules and deleted-image moves are untested, and those are where data can be lost.
- **No installer or licence.** `setup.iss`, `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT` were removed. The original project is MIT-licensed, which usually means a fork keeps its licence and copyright notice.
- **README and repo name.** There are no build or usage instructions for someone new to the fork, and `git push` reports the repository moved to `ismailozenc/Photos`; update the `origin` URL.

## Suggested order of work

Build first, then stop data loss and privacy leaks, then speed; features come after.

- [ ] Build on Windows with SkiaSharp 4 and Magick.NET 15; fix any API breaks
- [ ] Flush settings and save the index on exit (bug 1)
- [ ] Hide private folder names and counts while locked; clear their cached thumbnails (bugs 2 and 3)
- [ ] Count input in every window toward the idle lock (bug 4)
- [ ] Make watcher rescans silent, ignore deleted-images moves, handle watcher errors (bugs 5 and 6)
- [ ] Run settings handlers outside the lock, on the render thread
- [ ] Cache `TrashFolderIds` per refresh; cancel superseded decodes
- [ ] Hashed, changeable private password
- [ ] Add a CI build and tests for storage and deleted-image moves
- [ ] Add `.gitattributes` and remove the comment fragments
- [ ] Duplicate finder view
