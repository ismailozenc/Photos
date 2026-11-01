# μPhotos fork: repository review

Nov 1, 2026 · @ismail özenç

## Summary

The build is green on Windows with SkiaSharp 4 and Magick.NET 15, and all nine bugs from the first two reviews are fixed. What remains is a display-order race, four performance items, and the larger security and feature work.

Reviewed at commit `432642f`, updated after `d4e5d2b`. No .NET SDK is available in the review environment, so findings come from reading the source; the build itself is verified on Windows.

| Area | Open | Most important |
| --- | --- | --- |
| Threading and data safety | 3 | Display order swapped while it is read |
| Performance | 4 | Superseded image decodes run to completion |
| Code quality | 3 | `PhotoProvider` is over 1,000 lines |
| User experience | 1 | Monitor roles |
| Feature upgrades | 7 | Duplicate finder view |
| Security, tests and packaging | 6 | Password in source; no CI |

## Threading and data safety

Settings writes are now serialised and their handlers run outside the lock. What remains is the display order, which is still replaced while other threads read it.

1. **Display order swapped while it is read.** `postUpdate` replaces `_displayOrder` and `DispalyGroups` on the refresh thread while the grid, preview, slideshow, folder list and selection read them on the render thread. Swap both as one immutable snapshot, on the render thread.
2. **Deleted-image map.** `_deletedMap` is built lazily with `??=` and reset to `null` from several threads. Harmless today because a rebuild gives the same result; it becomes a bug if the map ever holds expensive state.
3. **Full-image reads on the render thread.** `Indexer.GetFullImage` reads the index and the file on the render thread for the preview, slideshow, compare view and prefetch, while the indexer writes to the index on its own thread.

## Performance

The decode pipeline does avoidable work on every keypress, and two actions run heavy work on the render thread.

| Where | Cost | Fix |
| --- | --- | --- |
| `AsyncImage` superseded requests | Holding Left or Right in the preview starts a full decode plus a prefetch per keypress; superseded decodes run to completion | Pass a cancellation token into the decoder and skip superseded work |
| Prefetch file reads | The next photo is read from disk on the render thread, which can stutter | Read inside the background task, from a path captured on the render thread |
| `PhotoProvider.DeletedCopyCount` | Walks the whole file list each time the folder list refreshes or the main menu opens | Count during `BuildVisibility` and keep the number |
| `EmptyDeleted` | Deletes every trashed file on the render thread; a large deleted-images folder freezes the app | Run it as a background task with progress |

## Code quality and maintainability

The fork grew quickly through many small edits; the cost shows up as a few very large files and inconsistent formatting.

1. **Large classes.** `PhotoProvider.cs` is over 1,000 lines and owns display order, visibility rules, private folders, deleted images, hidden folders and the folder watcher. Moving visibility, deleted images and watching into their own classes would make each testable.
2. **Settings layout.** `SettingsRecord` has about 20 keys covering private folders, hidden folders, deleted images, ranking and slideshow. Group related keys into small records.
3. **Leftover markers.** Seven `TODO` and `!!!` comments mark known problems that are not tracked anywhere.

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

Correctness first, then speed, then features.

- [ ] Swap the display order as one snapshot, on the render thread
- [ ] Cancel superseded image decodes; read prefetch files on the background thread
- [ ] Run `EmptyDeleted` as a background task with progress
- [ ] Hashed, changeable private password
- [ ] Add a CI build and tests for storage and deleted-image moves
- [ ] Restore `LICENSE.TXT` and the third-party notices
- [ ] Duplicate finder view
