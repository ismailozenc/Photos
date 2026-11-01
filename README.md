# μPhotos fork: repository review

Nov 1, 2026 · @ismail özenç

## Summary

Reviewed at commit `0687b1e` ("csv removed"): 72 tracked files, 46 C# files, about 13,000 lines. The repo has no tests and has not been compiled in this review, so line numbers are from source reading only.

| Area | Count | Most important |
| --- | --- | --- |
| User experience | 1 | Monitor roles |
| Feature upgrades | 7 | Duplicate finder view |
| Security, build, packaging | 5 | Private folders are UI-only; password in source |

## User experience improvements

Most gaps are about selection and the multi-monitor windows, which were added quickly and lack the small conveniences users expect.

| Area | Improvement |
| --- | --- |
| Monitor roles | Let users pick which screen is main, folders and preview, instead of guessing from shape; remember the choice per display. |

## Feature upgrades

The best-value upgrades build on data the app already has: signatures for duplicates and ratings.

| Feature | Effort | Why |
| --- | --- | --- |
| Duplicate finder view | Small | Copies are already one record with several sources; list photos with 2+ copies and offer "keep one". |
| Rating stars on thumbnails and a rating filter | Small | Ratings are stored but only visible in the photo view. |
| Slideshow settings: interval, order (folder, date, random), transition | Medium | Interval and order are fixed today. |
| Copy or move selection to a folder | Medium | Replaces the copy-to-clipboard button that was removed. |
| Drag photos out to Explorer | Medium | Common expectation on Windows. |
| Configurable private-folder password (hashed) | Small | See Security. |
| Folder roles and layouts saved per monitor set | Medium | Home and office monitor setups differ. |

## Security, build and packaging

Private folders only hide photos in the app's own views; anyone with access to the PC can still see them, so treat the feature as a privacy screen, not protection.

- **Password in source.** `PrivateFolderRules.cs:14` holds `"1234"` as a constant. Store a salted hash (PBKDF2) in settings and add a "Change password" item.
- **Thumbnails on disk.** Private photos' thumbnails sit unencrypted in `large.bin` and `tiny.bin`, and the files stay readable in Explorer. Say so in the UI, or encrypt the private thumbnails.
- **Packages.** Checked against published advisories: MessagePack 3.1.12, Magick.NET-Q8 15.0.0 and SkiaSharp 4.154.0 are past the fixed versions. Transitive and test packages are not checked. Confirm with `dotnet list package --vulnerable --include-transitive` on Windows.
- **README.** It describes features this fork removed: mobile sync, country/city/camera filters and the project website. Rewrite it for the fork's features.
- **Repo name.** `git push` reports the repository moved to `ismailozenc/Photos`; update the `origin` URL.

## Suggested order of work

Remaining work, in priority order.

- [ ] Hashed, changeable private password; auto-lock
- [ ] Update NuGet packages; rewrite the README
- [ ] Duplicate finder view
