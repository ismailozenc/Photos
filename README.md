# μPhotos fork: repository review

Nov 1, 2026 · @ismail özenç

## Summary

Reviewed at commit `0687b1e` ("csv removed"): 72 tracked files, 46 C# files, about 13,000 lines. The repo has no tests and has not been compiled in this review, so line numbers are from source reading only.

| Area | Count | Most important |
| --- | --- | --- |
| User experience | 10 | Selection has no range select or select-all |
| Feature upgrades | 12 | Shift-click range select, Empty deleted images |
| Security, build, packaging | 5 | Private folders are UI-only; password in source |

## User experience improvements

Most gaps are about selection and the multi-monitor windows, which were added quickly and lack the small conveniences users expect.

| Area | Improvement |
| --- | --- |
| Selection | Shift-click for ranges, Ctrl+A to select all, Escape to clear, and a visible "N selected" count with a clear button. |
| Selection | Keep the selection across refreshes by storing photo signatures, not grid positions. |
| Double-click | Use the Windows setting (`SystemInformation.DoubleClickTime`) instead of the fixed 450 ms in `ThumbnailView.cs:168`. |
| Slideshow | Show a small pause/next hint on first start. |
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
- **Packages.** Magick.NET 14.15.0 and MessagePack 3.1.9 were flagged as vulnerable by NuGet earlier in this work. Run `dotnet list package --vulnerable` and update.
- **README.** It describes features this fork removed: mobile sync, country/city/camera filters and the project website. Rewrite it for the fork's features.
- **Repo name.** `git push` reports the repository moved to `ismailozenc/Photos`; update the `origin` URL.

## Suggested order of work

Remaining work, in priority order.

- [ ] Hashed, changeable private password; auto-lock
- [ ] Update NuGet packages; rewrite the README
- [ ] Add a test project for the pure helpers
- [ ] Shift-click, Ctrl+A, Escape for selection
- [ ] Empty deleted images; duplicate finder
