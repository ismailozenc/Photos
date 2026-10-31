using System;
using System.Collections.Generic;
using System.Linq;

namespace Photos.Core
{
    /// <summary>
    /// Private folders. A folder id is "sourceId/folder path in lower case", the same id the folder list uses.
    /// A marked folder covers its subfolders too, and a source's root row covers the whole source.
    /// </summary>
    public static class PrivateFolderRules
    {
        /// <summary>Password for private folders. Fixed for now.</summary>
        public const string Password = "1234";

        public static bool IsPrivate(IEnumerable<string> marked, string folderId) => marked.Any(m => Covers(m, folderId));

        public static bool Covers(string marked, string folderId)
        {
            int markedSlash = marked.IndexOf('/');
            int folderSlash = folderId.IndexOf('/');
            if (markedSlash < 0 || folderSlash < 0)
                return false;
            if (!string.Equals(marked[..markedSlash], folderId[..folderSlash], StringComparison.Ordinal))
                return false;

            var root = marked[(markedSlash + 1)..];
            if (root.Length == 0)
                return true; // the source root covers the whole source

            var path = folderId[(folderSlash + 1)..];
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return false;
            return path.Length == root.Length || path[root.Length] == '\\' || path[root.Length] == '/';
        }
    }
}
