using System.Text.RegularExpressions;
using YTDLPHost.Models;

namespace YTDLPHost.Services
{
    /// <summary>
    /// Removes what a download leaves behind: per-stream files after a merge, partial files and fragments after a cancel,
    /// stray thumbnails, and the temporary cookie file. Moved unchanged from MainViewModel (Phase 1.5e) and now covered by tests.
    /// All methods swallow file-system errors on purpose: a locked or already-deleted file must never break the UI.
    /// </summary>
    public static class DownloadCleaner
    {
        public static void ScrubStrayThumbnails(DownloadTask task)
        {
            try 
            {
                if (string.IsNullOrEmpty(task.OutputPath)) return;
                string dir = Path.GetDirectoryName(task.OutputPath) ?? "";
                string title = Path.GetFileNameWithoutExtension(task.OutputPath) ?? "";
                
                if (Directory.Exists(dir) && !string.IsNullOrEmpty(title))
                {
                    var files = Directory.GetFiles(dir, $"{title}*");
                    string outExt = Path.GetExtension(task.OutputPath).ToLowerInvariant();
                    
                    foreach (var file in files)
                    {
                        string ext = Path.GetExtension(file).ToLowerInvariant();
                        if ((ext == ".webp" || ext == ".jpg" || ext == ".jpeg" || ext == ".png") && ext != outExt)
                        {
                            try { File.Delete(file); } catch { }
                        }
                    }
                }
            } 
            catch { }
        }

        public static void CleanupPartialFiles(DownloadTask task, bool forceDeleteAll = false)
        {
            var stems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // FIX: bulk jobs ("download all uploads", playlists). Cancelling used to wipe EVERY file the job had produced, because the
            // tracked list spanned all items. The runner now moves finished items to task.SettledFiles; TrackedFiles holds only the item
            // that is downloading right now. On a bulk cancel we additionally never delete a file that is already complete - only
            // partial / intermediate leftovers of that one in-flight item.
            bool isBulk = !string.IsNullOrEmpty(task.PlaylistInfo);
            var settled = new HashSet<string>(task.SettledFiles, StringComparer.OrdinalIgnoreCase);

            foreach (var filePath in task.TrackedFiles.Distinct().ToList())
            {
                if (settled.Contains(filePath)) continue;
                if (!forceDeleteAll && filePath.Equals(task.OutputPath, StringComparison.OrdinalIgnoreCase)) continue;

                var fileName = Path.GetFileName(filePath);
                // Per-format intermediates of a multi-stream download ("Title.f137.mp4", "Title.f251.webm") are never a deliverable.
                bool isIntermediate = Regex.IsMatch(fileName, @"\.f[\w-]+\.[^.]+$");

                if (forceDeleteAll && isBulk && !isIntermediate)
                {
                    // A completed file of the in-flight item (e.g. cancelled while yt-dlp sleeps before the next item) is kept;
                    // only its unfinished ".part"/".ytdl" companions are removed.
                    TryDeleteFile(filePath + ".part");
                    TryDeleteFile(filePath + ".ytdl");
                }
                else
                {
                    // The exact file yt-dlp reported as its destination for this stream.
                    TryDeleteFile(filePath);
                    // While a stream is still downloading (exactly the state a cancelled task is in), yt-dlp writes to "<file>.part"
                    // and sometimes a matching "<file>.ytdl" resume-info file; the final name usually doesn't exist yet.
                    TryDeleteFile(filePath + ".part");
                    TryDeleteFile(filePath + ".ytdl");
                }

                var dir = Path.GetDirectoryName(filePath);
                if (string.IsNullOrEmpty(dir)) continue;
                dirs.Add(dir);

                // Multi-stream downloads get per-format suffixes, and task.OutputPath only holds the LAST reported destination, so the
                // leftover sweep below derives a stem from every tracked file.
                var stem = Regex.Replace(fileName, @"\.f[\w-]+\.[^.]+$", "");
                stem = Path.GetFileNameWithoutExtension(stem);
                if (!string.IsNullOrEmpty(stem)) stems.Add(stem);
            }

            if (!forceDeleteAll) return;

            // Belt-and-braces sweep: leftover fragment/resume files of THIS in-flight item only. Settled items are never swept - a
            // short stem such as "Song" would otherwise also match the finished "Song (remix)" next to it.
            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var stem in stems)
                {
                    string[] files;
                    try { files = Directory.GetFiles(dir, $"{stem}*"); }
                    catch { continue; }

                    foreach (var file in files)
                    {
                        if (settled.Contains(file)) continue;
                        if (file.EndsWith(".part", StringComparison.OrdinalIgnoreCase) ||
                            file.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase) ||
                            file.EndsWith(".frag", StringComparison.OrdinalIgnoreCase) ||
                            file.Contains(".part-Frag", StringComparison.OrdinalIgnoreCase) ||
                            file.Contains(".temp.", StringComparison.OrdinalIgnoreCase))
                        {
                            TryDeleteFile(file);
                        }
                    }
                }
            }
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        public static void CleanupCookieFile(DownloadTask task)
        {
            if (!string.IsNullOrEmpty(task.CookieFilePath) && File.Exists(task.CookieFilePath)) try { File.Delete(task.CookieFilePath); } catch { }
        }
    }
}
