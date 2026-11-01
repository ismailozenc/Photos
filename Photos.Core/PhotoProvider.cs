using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Photos.Lib;
using System.Reflection;
using Photos.Core.StoredTypes;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace Photos.Core
{
    class UpdateOp
    {
        public Action UILockAction, PreLockAction;
    }

    public class PhotoProvider : IDisposable
    {
        public PhotoIndexer Indexer;

        public const int MediumThumbnailSize = 352;

        public FilterService FilterService { get; private set; }

        int _idx;
        public int Idx
        {
            get => _idx;
            set
            {
                if (_idx != value)
                {
                    _idx = value;
                    ClampIdx(ref _idx);
                    IndexChanged?.Invoke();
                }
            }
        }

        public void ClampIdx(ref int idx)
        {
            var size = _displayOrder.Count;
            if (size == 0)
                idx = 0;
            else
            {
                while (idx < 0)
                    idx += size;
                idx %= size;
            }
        }

        ConcurrentQueue<UpdateOp> SystemUpdates = new();
        public event Action IndexChanged;
        Task indexTask, initTask, backgroundLoaderTask;
        volatile bool _shutdown;
        readonly object _lock = new();
        public Storage DB { get; private set; }
        public bool FolderLibraryMode { get; private set; }

        const int MinThumbnailsCacheSize = 5;
        Cache<ulong, SKImage> _scaledThumbnails = new(MinThumbnailsCacheSize) { OnDispose = x => x.Dispose() };
        Cache<ulong, SKImage> _fullSizeThumbnails = new(24) { OnDispose = x => x.Dispose() };
        Cache<ulong, SKImage> _microCache = new(MinThumbnailsCacheSize) { OnDispose = x => x.Dispose() };

        List<FileRecord> _displayOrder;
        public List<FileGroup> DispalyGroups { get; private set; }

        List<FileRecord> _folderViewList;
        public bool IsSingleFolderMode => _folderViewList != null;

        public void ClearIndex()
        {
            RestartBackgroundTasks(() =>
            {
                DB.ClearFiles();
                _fullSizeThumbnails.Clear();
                _scaledThumbnails.Clear();
                _microCache.Clear();
                RefreshDisplayOrderBackgroundOnly(false);
            });
        }

        void SettingsUpdate(SettingsRecord oldV, SettingsRecord newV)
        {
            if (oldV.ThumbnailDrawSize != newV.ThumbnailDrawSize)
            {
                _scaledThumbnails.Clear();
                _scaledThumbnails.Limit = MinThumbnailsCacheSize;
            }
        }

        public PhotoProvider(string dbPath, string editorDir, string fname = null, bool protectUnreadableFolders = true, bool folderLibraryMode = true, Action<PhotoProvider> setupEvents = null)
        {
            
            FolderLibraryMode = folderLibraryMode;
            
            DB = new Storage(dbPath);
            Utils.TraceEnabled = () => DB.Settings.DevMode;

            DB.OnSettingsUpdate += SettingsUpdate;
            Indexer = new PhotoIndexer(DB, protectUnreadableFolders, Path.Combine(editorDir, Utils.LatinAppName));

            FilterService = new(DB);
            FilterService.FilterUpdate += () => EnqueueRefreshDisplayOrder(true);

            Indexer.IndexUpdateRequest += () => {
                RefreshFolderViewList();
                EnqueueRefreshDisplayOrder();
            };

            _errorImage = SKImage.FromBitmap(_errorThumbnail);

            SetFolderModeBackgroundOnly(fname);

            setupEvents?.Invoke(this);

            initTask = Task.Run(() =>
            {
                DB.LoadFiles(_folderViewList);
                RefreshFolderViewList();
                RefreshDisplayOrderBackgroundOnly(false);

                RunIndexerTask(new IndexTaskConfig());
                backgroundLoaderTask = PriorityScheduler.RunBackgroundThread(BackgroundLoader, ThreadPriority.BelowNormal, "loader");
            });

            initTask.DieOnError();
        }

        public volatile int MinPreloadImage, MaxPreloadImage;
        public event Action OnBackgroundWorker;
        void BackgroundLoader()
        {
            try
            {
                while (!_shutdown)
                {
                    var dt = DateTime.UtcNow;
                    UpdateOp task = null;
                    while (SystemUpdates.TryDequeue(out task))
                    {
                        var finished = new TaskCompletionSource();
                        try
                        {
                            task.PreLockAction?.Invoke();
                            if (task.UILockAction != null)
                            {
                                RequestUILock(finished.Task).Wait();
                                task.UILockAction();
                            }
                        }
                        finally
                        {
                            finished.SetResult();
                        }
                    }

                    PreloadScaledImages();
                    OnBackgroundWorker?.Invoke();

                    if ((DateTime.UtcNow - dt).TotalMilliseconds < 3) // nothing was loaded
                        Thread.Sleep(20);
                }
            }
            catch (Exception ex)
            {
                Utils.LogError(ex);
                Environment.Exit(1);
            }
        }

        private void PreloadScaledImages()
        {
            var startDisplayOrder = _displayOrder;
            if (_displayOrder.Count > 0)
            {
                var maxImgIdx = Math.Clamp(MaxPreloadImage, 0, startDisplayOrder.Count - 1);
                var minImgIdx = Math.Clamp(MinPreloadImage, 0, startDisplayOrder.Count - 1);
                var winSize = maxImgIdx - minImgIdx + 1;

                var leftLimit = Math.Max(0, minImgIdx - winSize);
                var rightLimit = Math.Min(startDisplayOrder.Count - 1, maxImgIdx + winSize);

                if (leftLimit <= rightLimit && rightLimit - leftLimit < 1000 && winSize > 0)
                {
                    _scaledThumbnails.Limit = Math.Max(_scaledThumbnails.Limit, (rightLimit - leftLimit) * 2);
                    _microCache.Limit = _scaledThumbnails.Limit * 4;

                    for (int i = minImgIdx; i <= rightLimit && MaxPreloadImage == maxImgIdx && _displayOrder == startDisplayOrder; i++)
                        GetScaledThumbnail(startDisplayOrder[i], false);

                    for (int i = minImgIdx - 1; i >= leftLimit && MaxPreloadImage == maxImgIdx && _displayOrder == startDisplayOrder; i--)
                        GetScaledThumbnail(startDisplayOrder[i], false);
                }
            }
        }

        private void RefreshFolderViewList()
        {
            lock (_lock)
                if (_folderViewList != null)
                    _folderViewList = _folderViewList.Select(x => x.IsIndexed() ? x : DB.TryGetFileBySource(new FileRecordSourceKey(x.Src.Single())) ?? x).ToList(); // use records with metadata from db
        }

        string _lastFolderViewDir;
        private void SetFolderModeBackgroundOnly(string fname, bool isDir = false)
        {
            _folderViewList = null;

            if (fname != null)
            {
                var tmp = new IndexUpdateInfo();
                _lastFolderViewDir = isDir ? fname : Path.GetDirectoryName(fname);
                Indexer.ListDir(null, tmp, _lastFolderViewDir, false);
                _folderViewList = tmp.FileList.OrderBy(x => x.Src[0].FN).ToList();
                RefreshFolderViewList();

                if (_folderViewList.Count == 0) // no images in the directory, fallback to gallery
                    _folderViewList = null;
            }

            FilterService.PersistentFilters = _folderViewList == null;

            RefreshDisplayOrderBackgroundOnly(false);
            if (_folderViewList != null)
            {
                if (isDir)
                    Idx = 0;
                else
                {
                    var fn = Path.GetFileName(fname);
                    var idx = _displayOrder.FindIndex(x => x.Src.Any(x => x.FN.Equals(fn, StringComparison.InvariantCultureIgnoreCase)));
                    Idx = idx >= 0 ? idx : 0;

                    PhotoSelected?.Invoke();
                }
            }
        }

        private void RunIndexerTask(IndexTaskConfig cfg)
        {
            if (indexTask != null && !indexTask.IsCompleted)
                throw new Exception("already running");
            indexTask = PriorityScheduler.RunBackgroundThread(() => Indexer.IndexDirs(cfg, _folderViewList), ThreadPriority.Lowest, "index");
        }

        //const int WorkStatusDelay = 1000;
        public event Action OnDisplayOrderUpdate;

        public IReadOnlyList<string> GetDirs() => DB.Settings.Dirs;
        public void AddDir(string path)
        {
            if (DB.Settings.Dirs.Contains(path))
                return;

            DB.Settings.Dirs = DB.Settings.Dirs.AddToArr(path);
            DB.SaveSettings();

            RestartBackgroundTasks(null, new IndexTaskConfig() { ForceRefreshDisplayAfterIndex = true });
        }
        public void RemoveDir(string d)
        {
            DB.Settings.Dirs = DB.Settings.Dirs.Where(x => x != d).ToArray();
            DB.SaveSettings();

            EnqueueRefreshDisplayOrder();
            RestartBackgroundTasks(null);
        }

        public void UpdateLibraryMaybe(IndexTaskConfig cfg = null)//(bool quick, bool forceMetaRefresh = false)
        {
            cfg ??= new IndexTaskConfig();

            var indexRunning = indexTask?.IsCompleted == true;
            if (initTask.IsCompleted)
            {
                cfg.QuickScanOnly = cfg.QuickScanOnly && !indexRunning;
                RestartBackgroundTasks(() =>
                {
                    if (cfg.ForceMetaRefresh)
                        foreach (var item in DB.ListFiles())
                            item.SetFlag(FileRecordFlags.FlagMetadataRefreshRequired);
                }, cfg);
            }
        }

        public void EnqueueRefreshDisplayOrder(bool skipFilters = false)
        {
            var box = new UpdateOp[1];
            box[0] = new UpdateOp()
            {
                PreLockAction = () => box[0].UILockAction = RefreshDisplayOrderBackgroundOnly(skipFilters),
            };

            SystemUpdates.Enqueue(box[0]);
        }

        Action RefreshDisplayOrderBackgroundOnly(bool skipFilters, bool postponeUIUpdate = false)
        {
            lock (_lock)
            {
                Dictionary<int, int> dirsOrder = null;

                var files = GetLibraryFiles(out var dirs);
                TotalCount = files.Count;

                FilterService.IsHidden = f => IsPrivateHidden(f) || IsFullyTrashed(f) || IsFullyExcluded(f);
                if (!skipFilters)
                    FilterService.RefreshFilters(files, dirs.Where(x => x != null), initTask != null);

                files = files.Where(f => ShowDeleted ? !IsPrivateHidden(f) && HasTrashedSource(f) : IsInLibrary(f)).ToList(); // the open view: deleted images, or the library
                FilterService.FilterFiles(files);

                if (FilterService.CurrentSortMode == SortModeEnum.Filename)
                {
                    var dirArr = dirs.Select((x, i) => (x, i)).Where(x => x.x != null).OrderBy(x => x.x.Directory).Select(x => x.i).ToArray();
                    dirsOrder = new();
                    for (int i = 0; i < dirArr.Length; i++)
                        dirsOrder[dirArr[i]] = i;

                    foreach (var item in files)
                    {
                        item.SrcForSorting = item.Src.FirstOrDefault(x => dirsOrder.ContainsKey(x.D)); // TODO: causes errors where First finds no elements???!!?!?!?
                        if (item.SrcForSorting == default)
                            throw new Exception($"dir not found {item.Src.Length};{_folderViewList==null};{FolderLibraryMode};{dirsOrder.Count};{item.Src.FirstOrDefault().D}");
                    }
                }

                IEnumerable<FileRecord> ordered = FilterService.CurrentSortMode switch
                {
                    SortModeEnum.Date => files.OrderByDescending(x => x.DT),
                    SortModeEnum.Filename => _folderViewList != null ? files : files.OrderBy(x => dirsOrder[x.SrcForSorting.D]).ThenBy(x => x.SrcForSorting.FN),
                    _ => throw new NotImplementedException(),
                };
                var displayOrder = ordered.ToList();

                long lastKey = -1;
                var dispalyGroups = new List<FileGroup>();
                FileGroup group = null;
                for (int i = 0; i < displayOrder.Count; i++)
                {
                    var item = displayOrder[i];
                    long key = FilterService.CurrentSortMode switch
                    {
                        SortModeEnum.Date => item.DT / 1000000,
                        SortModeEnum.Filename => item.SrcForSorting.D, // TODO: show multiple times in each dir??!!!!
                        _ => throw new NotImplementedException(),
                    };

                    if (key != lastKey || group == null)
                    {
                        group = new()
                        {
                            StartIdx = i,
                            Key = FilterService.CurrentSortMode switch
                            {
                                SortModeEnum.Date => FileRecord.TimeFrom(key * 1000000).ToLongDateString(),
                                SortModeEnum.Filename => DB.Directories.GetValue((int)key).Directory, // TODO: show multiple times in each dir??!!!!
                                _ => throw new NotImplementedException(),
                            }
                        };
                        dispalyGroups.Add(group);
                        lastKey = key;
                    }

                    group.Count++;
                }

                var postUpdate = () =>
                {
                    _displayOrder = displayOrder;
                    DispalyGroups = dispalyGroups;
                    ClampIdx(ref _idx);
                    DisplayedCount = _displayOrder.Count;

                    SystemUpdates.Enqueue(new UpdateOp() { PreLockAction = () => OnDisplayOrderUpdate?.Invoke() });
                };

                if (postponeUIUpdate)
                    return postUpdate;

                postUpdate();
                return null;
            }
        }

        /// <summary>The library's rule for showing a photo: not private while locked, not moved to deleted images, not in a hidden folder.</summary>
        public bool IsInLibrary(FileRecord file) => !IsPrivateHidden(file) && !IsFullyTrashed(file) && !IsFullyExcluded(file);

        /// <summary>True when the folder itself is hidden from browse and slideshows (subfolders are not affected).</summary>
        public bool IsExcludedFolder(string folderId) =>
            (DB.Settings.ExcludedFolders ?? Array.Empty<string>()).Contains(folderId, StringComparer.OrdinalIgnoreCase);

        /// <summary>True when this copy lies directly in a folder hidden from browse and slideshows.</summary>
        public bool IsExcludedSource(SourceRecord s)
        {
            if (!IsAnyExcludedFolder)
                return false;
            var dir = DB.Directories.GetValue(s.D);
            return dir != null && IsExcludedFolder($"{dir.SourceId}/{dir.Directory.ToLowerInvariant()}");
        }

        bool IsAnyExcludedFolder => (DB.Settings.ExcludedFolders?.Length ?? 0) > 0;

        /// <summary>A photo is hidden only when every copy is in a hidden folder.</summary>
        public bool IsFullyExcluded(FileRecord file) => file.Src.All(IsExcludedSource);

        /// <summary>Hides or shows several folders in browse and slideshows, with one settings save and one refresh.</summary>
        public void SetFoldersExcluded(IEnumerable<string> folderIds, bool excluded)
        {
            var changed = folderIds.ToList();
            var ids = (DB.Settings.ExcludedFolders ?? Array.Empty<string>())
                .Where(x => !changed.Contains(x, StringComparer.OrdinalIgnoreCase)).ToList();
            if (excluded)
                ids.AddRange(changed);
            DB.Settings = DB.Settings with { ExcludedFolders = ids.ToArray() };
            EnqueueRefreshDisplayOrder();
        }

        public const string DeletedFolderName = "deleted images";

        /// <summary>True while the deleted-images view is shown instead of the library.</summary>
        public bool ShowDeleted { get; private set; }

        public void SetShowDeleted(bool show)
        {
            if (ShowDeleted == show)
                return;
            ShowDeleted = show;
            EnqueueRefreshDisplayOrder();
        }

        Dictionary<string, string> _deletedMap; // trashed path (lower case) -> original path; reset when the setting changes

        Dictionary<string, string> DeletedMap => _deletedMap ??= (DB.Settings.DeletedOriginals ?? Array.Empty<string>())
            .Select(x => x.Split('|')).Where(x => x.Length == 2)
            .GroupBy(x => x[0].ToLowerInvariant()).ToDictionary(g => g.Key, g => g.Last()[1]);

        public bool HasDeletedImages => DeletedMap.Count > 0;

        /// <summary>Folder ids of the deleted-images folders, as the folder list names them.</summary>
        public IEnumerable<string> TrashFolderIds => DeletedMap.Keys
            .Select(p => System.IO.Path.GetDirectoryName(p)).Where(d => d != null).Distinct().Select(d => "0/" + d);

        /// <summary>True when this copy was moved to a deleted-images folder.</summary>
        public bool IsTrashedSource(SourceRecord s)
        {
            var map = DeletedMap;
            return map.Count > 0 && map.ContainsKey(DB.Directories.GetValue(s.D).CombinePath(s).ToLowerInvariant());
        }

        /// <summary>A photo is hidden from the library only when every copy is in the deleted-images folder.</summary>
        public bool IsFullyTrashed(FileRecord file) => file.Src.All(IsTrashedSource);

        /// <summary>True when at least one copy is in the deleted-images folder.</summary>
        public bool HasTrashedSource(FileRecord file) => file.Src.Any(IsTrashedSource);

        /// <summary>Indexes of the copies the current view can act on: local copies that are trashed in the deleted view, and not trashed otherwise.</summary>
        public IEnumerable<int> ActionableSources(FileRecord file)
        {
            for (int i = 0; i < file.Src.Length; i++)
            {
                var dir = DB.Directories.GetValue(file.Src[i].D);
                if (dir.IsLocal() && IsTrashedSource(file.Src[i]) == ShowDeleted && File.Exists(dir.CombinePath(file.Src[i])))
                    yield return i;
            }
        }

        /// <summary>
        /// Moves each copy into its library's deleted-images folder, or back to its original folder when restoring.
        /// The whole batch costs one settings save and one index save. Returns the counts and the last error.
        /// </summary>
        public (int done, int failed, string lastError) RunTrashBatch(IEnumerable<(FileRecord file, int srcIndex)> items, bool restore)
        {
            var added = new List<string>();
            var removed = new List<string>();
            var moved = new List<FileRecord>();
            int done = 0, failed = 0;
            string lastError = null;

            foreach (var (file, srcIndex) in items.ToList())
            {
                var error = restore ? RestoreOne(file, srcIndex, removed) : TrashOne(file, srcIndex, added);
                if (error == null)
                {
                    done++;
                    moved.Add(file);
                }
                else
                {
                    failed++;
                    lastError = error;
                }
            }

            if (done > 0)
            {
                var kept = (DB.Settings.DeletedOriginals ?? Array.Empty<string>())
                    .Where(x => !removed.Any(r => x.StartsWith(r + "|", StringComparison.OrdinalIgnoreCase)));
                DB.Settings = DB.Settings with { DeletedOriginals = kept.Concat(added).ToArray() };
                _deletedMap = null;
                DB.SaveFiles();
            }

            if (IsSingleFolderMode && !restore && done > 0)
                lock (_lock)
                    _folderViewList = _folderViewList.Where(x => !moved.Contains(x)).ToList();

            EnqueueRefreshDisplayOrder();
            return (done, failed, lastError);
        }

        string TrashOne(FileRecord file, int srcIndex, List<string> added)
        {
            file = DB.Current(file);
            var src = file.Src[srcIndex];
            var dir = DB.Directories.GetValue(src.D);
            var fullPath = dir.CombinePath(src);
            if (!dir.IsLocal() || !File.Exists(fullPath))
                return "File not found";

            var trashDir = Path.Combine(LibraryRootOf(fullPath), DeletedFolderName);
            var target = UniquePath(trashDir, src.FN);
            try
            {
                Directory.CreateDirectory(trashDir);
                File.Move(fullPath, target);
            }
            catch (Exception ex)
            {
                return ex.Message;
            }

            var trashDirIdx = DB.Directories.GetIndex(new DirectoryRecord() { Directory = trashDir, SourceId = 0 });
            added.Add($"{target}|{fullPath}");
            DB.ReplaceSource(file, srcIndex, trashDirIdx, Path.GetFileName(target), save: false);
            return null;
        }

        string RestoreOne(FileRecord file, int srcIndex, List<string> removed)
        {
            file = DB.Current(file);
            var src = file.Src[srcIndex];
            var trashed = DB.Directories.GetValue(src.D).CombinePath(src);
            if (!DeletedMap.TryGetValue(trashed.ToLowerInvariant(), out var original))
                return "The original folder is unknown";

            var originalDir = Path.GetDirectoryName(original);
            try
            {
                Directory.CreateDirectory(originalDir);
                var target = UniquePath(originalDir, Path.GetFileName(original));
                File.Move(trashed, target);
                var originalDirIdx = DB.Directories.GetIndex(new DirectoryRecord() { Directory = originalDir, SourceId = 0 });
                DB.ReplaceSource(file, srcIndex, originalDirIdx, Path.GetFileName(target), save: false);
            }
            catch (Exception ex)
            {
                return ex.Message;
            }

            removed.Add(trashed);
            return null;
        }

        /// <summary>Moves a local photo into its library's deleted-images folder. Nothing is overwritten.</summary>
        public string MoveToDeleted(FileRecord file, int srcIndex) =>
            RunTrashBatch(new[] { (file, srcIndex) }, restore: false).lastError;

        /// <summary>Moves a deleted photo back to the folder it came from. A name clash gets a new name.</summary>
        public string RestoreFromDeleted(FileRecord file, int srcIndex) =>
            RunTrashBatch(new[] { (file, srcIndex) }, restore: true).lastError;

        /// <summary>The added library folder that contains the file, or the file's own folder if none does.</summary>
        string LibraryRootOf(string fullPath)
        {
            var root = GetDirs()
                .Select(d => d.TrimEnd('\\', '/'))
                .Where(d => fullPath.StartsWith(d + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(d => d.Length)
                .FirstOrDefault();
            return root ?? System.IO.Path.GetDirectoryName(fullPath);
        }

        static string UniquePath(string dir, string fileName)
        {
            var target = Path.Combine(dir, fileName);
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            for (int i = 1; File.Exists(target); i++)
                target = Path.Combine(dir, $"{stem} ({i}){ext}");
            return target;
        }

        /// <summary>True after the password was entered in this session. Private folders stay hidden until then.</summary>
        public bool PrivateUnlocked { get; private set; }

        /// <summary>Folder ids marked private. They are listed in the folder view.</summary>
        public IReadOnlyList<string> PrivateFolderIds => DB.Settings.PrivateFolders ?? Array.Empty<string>();

        public bool HasPrivateFolders => PrivateFolderIds.Count > 0;

        /// <summary>True when a file must not be shown: it lies in a private folder and the session is locked.</summary>
        public bool IsPrivateHidden(FileRecord file)
        {
            if (PrivateUnlocked || !HasPrivateFolders)
                return false;
            return file.Src.Any(s =>
            {
                var dir = DB.Directories.GetValue(s.D);
                return dir != null && PrivateFolderRules.IsPrivate(PrivateFolderIds, $"{dir.SourceId}/{dir.Directory.ToLowerInvariant()}");
            });
        }

        public bool TryUnlockPrivate(string password)
        {
            if (password != PrivateFolderRules.Password)
                return false;
            PrivateUnlocked = true;
            EnqueueRefreshDisplayOrder();
            return true;
        }

        public void LockPrivate()
        {
            if (!PrivateUnlocked)
                return;
            PrivateUnlocked = false;
            EnqueueRefreshDisplayOrder();
        }

        /// <summary>Marks or unmarks several folders as private, with one settings save and one refresh.</summary>
        public void SetFoldersPrivate(IEnumerable<string> folderIds, bool isPrivate)
        {
            var changed = folderIds.ToList();
            var ids = PrivateFolderIds.Where(x => !changed.Contains(x, StringComparer.OrdinalIgnoreCase)).ToList();
            if (isPrivate)
                ids.AddRange(changed);
            DB.Settings = DB.Settings with { PrivateFolders = ids.ToArray() };
            EnqueueRefreshDisplayOrder();
        }

        public List<FileRecord> GetLibraryFiles(out DirectoryRecord[] dirs, bool realLibOnly = false)
        {
            if (_folderViewList != null && !realLibOnly)
            {
                foreach (var item in DB.Directories.ToArray().Where(x => x != null))
                    item.IsInLib = false;
                
                var idx = DB.Directories.GetIndex(new DirectoryRecord() { Directory = _lastFolderViewDir });
                dirs = new DirectoryRecord[idx + 1];
                dirs[idx] = DB.Directories.GetValue(idx);
                dirs[idx].IsInLib = true;
                return _folderViewList.ToList();
            }

            IEnumerable<FileRecord> files = DB.ListFiles().Where(x => x.Src.Length > 0);
            dirs = DB.Directories.ToArray();

            if (FolderLibraryMode)
            {
                var allDirs = Indexer.GetBuiltInLibraryFolders();
                allDirs.AddRange(GetDirs());

                for (int i = 0; i < dirs.Length; i++) // set IsInLib
                {
                    var item = dirs[i];
                    if (item != null)
                    {
                        item.IsInLib = item.SourceId != 0 || allDirs.Any(d => item.IsSubdirectoryOf(d));
                        if (!item.IsInLib)
                            dirs[i] = null;
                    }
                }

                files = files.Where(x => x.Src.Any(s => DB.Directories.GetValue(s.D).IsInLib));
            }
            else
                foreach (var item in dirs.Where(x => x != null))
                    item.IsInLib = true;

            return files.ToList();
        }



        static SKImage _emptyThumbnail;
        static SKBitmap ToThumbnail(SKPath p) => IconStore.ToBitmap(128, 128, 32, (p, null));
        public SKImage GetScaledThumbnail(FileRecord rec, bool fast = false)
        {
            SKImage res = _scaledThumbnails.Get(rec.Sig);

            if (res == null && !rec.IsIndexed() && !rec.IndexFailed) // still indexing
            {
                rec.PrioTicks = DateTime.UtcNow.Ticks;
                Indexer.CheckFilesPrio = true;
            }

            if (!rec.IsIndexed() || rec.IndexFailed || IsFullScreenDrawMode) // will be fast
                return GetFullThumbnail(rec);

            if (res == null && !fast && initTask.IsCompleted)
            {
                using var stored = GetSlotThumbnail(rec, ThumbnailSizes.SlotFor(DB.Settings.ThumbnailDrawSize));
                var largeImage = stored ?? GetFullThumbnail(rec);
                using var bmp = PhotoIndexer.ScaleImage(largeImage, DB.Settings.ThumbnailDrawSize, GetImageDimsAfterOrientation(rec));
                res = SKImage.FromBitmap(bmp);
                _scaledThumbnails.Put(rec.Sig, res);
            }

            return res;
        }

        public bool IsFullScreenDrawMode => DB.Settings.ThumbnailDrawSize < 0;
        public SKSizeI GetImageDimsAfterOrientation(FileRecord rec)
        {
            var o = DB.GetOrientation(rec);
            SKSizeI res = new(rec.W, rec.H);
            switch (o)
            {
                case SKEncodedOrigin.LeftTop:
                case SKEncodedOrigin.RightTop:
                case SKEncodedOrigin.RightBottom:
                case SKEncodedOrigin.LeftBottom:
                    res = new SKSizeI(rec.H, rec.W);
                    break;
                default:
                    break;
            }

            return res;
        }

        readonly SKBitmap _errorThumbnail = ToThumbnail(IconStore.EmojiDizzy);
        readonly SKImage _errorImage;
        /// <summary>The stored thumbnail of one size, or null if it can not be read.</summary>
        SKImage GetSlotThumbnail(FileRecord file, int slot)
        {
            byte[] buf = null;
            if (file.SizePos != null && file.SizeLen != null && slot < file.SizePos.Length && file.SizeLen[slot] > 0)
                buf = DB.Thumbnails.Read(file.SizePos[slot], file.SizeLen[slot]);
            if (buf == null || buf.Length == 0)
                return null;

            using var data = SKData.CreateCopy(buf);
            return ImageLoader.DecodeThumbnail(data, DB.GetOrientation(file));
        }

        public SKImage GetFullThumbnail(FileRecord file)
        {
            if (file.IndexFailed)
                return _errorImage;

            var res = _fullSizeThumbnails.Get(file.Sig);
            if (res == null && file.IsIndexed() && initTask.IsCompleted)
            {
                var buf = DB.Thumbnails.Read(file.T, file.TS);

                if (buf != null && buf.Length != 0)
                    using (var data = SKData.CreateCopy(buf))
                        res = ImageLoader.DecodeThumbnail(data, DB.GetOrientation(file));

                res ??= SKImage.FromBitmap(_errorThumbnail);
                _fullSizeThumbnails.Put(file.Sig, res);
            }

            res ??= (_emptyThumbnail ??= SKImage.FromBitmap(ToThumbnail(IconStore.HourglassSplit)));
            return res;
        }

        

        public List<FileRecord> GetFiles(IEnumerable<int> idxs)
        {
            var res = new List<FileRecord>();

            if (_displayOrder.Count > 0)
                foreach (var idx in idxs)
                {
                    var candIdx = idx;
                    ClampIdx(ref candIdx);
                    res.Add(_displayOrder[candIdx]);
                }

            return res;
        }

        public FileRecord GetFile(int idx)
        {
            return _displayOrder[idx];
        }

        public void Dispose()
        {
            TerminateIndexerTask();
            _shutdown = true;
            backgroundLoaderTask?.Wait();

            DB.SaveFilesIfDirty();
            DB.Dispose();
            _microCache.Clear();
        }        

        public int DisplayedCount, TotalCount;
        public bool IsInitRunning => !initTask.IsCompleted;
        internal bool IsIndexEmpty => DB.Count == 0 && initTask.IsCompleted;

        public event Action PhotoSelected;
        public void SelectPhoto() => PhotoSelected?.Invoke();
        public void OpenFile(string commFile)
        {
            var isDir = Directory.Exists(commFile);
            var isFile = File.Exists(commFile);
            if (!isFile && !isDir)
                return;

            RestartBackgroundTasks(() => SetFolderModeBackgroundOnly(commFile, isDir));
        }

        internal void CloseSingleFolderMode()
        {
            RestartBackgroundTasks(() => SetFolderModeBackgroundOnly(null));
        }

        private void RestartBackgroundTasks(Action act, IndexTaskConfig cfg = null)
        {
            cfg ??= new IndexTaskConfig();
            SystemUpdates.Enqueue(new UpdateOp()
            {
                PreLockAction = TerminateIndexerTask,
                UILockAction = () =>
                {
                    lock (_lock)
                    {
                        try { act?.Invoke(); }
                        finally { RunIndexerTask(cfg); }
                    }
                }
            });
        }

        void TerminateIndexerTask()
        {
            initTask.Wait();
            Indexer.CancelIndex = true;
            indexTask?.Wait();
            Indexer.CancelIndex = false;
        }

        internal SKImage GetMicroThumbnail(FileRecord file)
        {
            if (!file.IsIndexed())
                return null;

            var img = _microCache.Get(file.Sig);

            if (img == null)
            {
                var buf = DB.MicroThumbnails.Read(file.MT, file.MTS);

                if (buf.Length != 0)
                {
                    img = SKImage.FromEncodedData(buf); // SKBitmap.Decode(buf) fails on android net9

                    var o = DB.GetOrientation(file);
                    if (o != SKEncodedOrigin.TopLeft)
                    {
                        var bmp = SKBitmap.FromImage(img);
                        img.Dispose();
                        img = null;

                        try
                        {
                            ImageLoader.FixOrientation(ref bmp, o);
                            img = SKImage.FromBitmap(bmp);
                        }
                        finally { bmp?.Dispose(); }
                    }

                    if (img != null)
                        _microCache.Put(file.Sig, img);
                }
            }

            return img;
        }

        public Func<Task, Task> RequestUILock;

        internal void InvalidateThumbnail(FileRecord file)
        {
            if (!file.IsIndexed())
                throw new ArgumentException();

            _microCache.Remove(file.Sig);
            _scaledThumbnails.Remove(file.Sig);
            _fullSizeThumbnails.Remove(file.Sig);
        }

    }

    public class FileGroup
    {
        public int StartIdx; public int Count; public string Key;
    }

    public class FullImageData : IDisposable
    {
        public FileRecord File;

        public SourceRecord LoadedSource;
        public DirectoryRecord Directory;
        public string FullPath;
        public SKData Data;

        public SKBitmap Bitmap;
        public SKEncodedOrigin Orientation;
        public int Width, Height;

        public void Dispose()
        {
            Data?.Dispose();
            Bitmap?.Dispose();
        }
    }

}
