using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.IO.Compression;

namespace SpaceLeft
{
    // Data Structures
    [Serializable]
    public class ScanResult
    {
        public long ScanDateTicks { get; set; }
        public string RootPath { get; set; }
        public List<DirEntry> Directories { get; set; }
        public List<FileEntry> Files { get; set; }

        public ScanResult()
        {
            Directories = new List<DirEntry>();
            Files = new List<FileEntry>();
        }
    }

    [Serializable]
    public class DirEntry
    {
        public string Path { get; set; }
        public long TotalSize { get; set; }
    }

    [Serializable]
    public class FileEntry
    {
        public string Path { get; set; }
        public long Size { get; set; }
    }

    public static class Storage
    {
        public static void Save(ScanResult result, string filename)
        {
            string tempFile = filename + ".tmp";
            
            try
            {
                // Write to temp file first
                using (var fs = new FileStream(tempFile, FileMode.Create))
                using (var gz = new GZipStream(fs, CompressionMode.Compress))
                using (var bw = new BinaryWriter(gz, System.Text.Encoding.UTF8))
                {
                    bw.Write(1); // Version
                    bw.Write(result.ScanDateTicks);
                    bw.Write(result.RootPath);
                    
                    bw.Write(result.Directories.Count);
                    foreach (var d in result.Directories)
                    {
                        bw.Write(d.Path);
                        bw.Write(d.TotalSize);
                    }

                    bw.Write(result.Files.Count);
                    foreach (var f in result.Files)
                    {
                        bw.Write(f.Path);
                        bw.Write(f.Size);
                    }
                }
                
                // Only replace the original file if write was successful
                if (File.Exists(filename))
                    File.Delete(filename);
                File.Move(tempFile, filename);
            }
            catch
            {
                // Clean up temp file if it exists
                if (File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { }
                }
                throw;
            }
        }

        public static ScanResult Load(string filename)
        {
            var result = new ScanResult();
            try
            {
                using (var fs = new FileStream(filename, FileMode.Open))
                using (var gz = new GZipStream(fs, CompressionMode.Decompress))
                using (var br = new BinaryReader(gz, System.Text.Encoding.UTF8))
                {
                    int version = br.ReadInt32(); // Version
                    result.ScanDateTicks = br.ReadInt64();
                    result.RootPath = br.ReadString();

                    int dirCount = br.ReadInt32();
                    
                    for (int i = 0; i < dirCount; i++)
                    {
                        try
                        {
                            string path = br.ReadString();
                            long size = br.ReadInt64();
                            result.Directories.Add(new DirEntry
                            {
                                Path = path,
                                TotalSize = size
                            });
                        }
                        catch (Exception ex)
                        {
                            throw new Exception("Error reading directory " + i + " of " + dirCount + ": " + ex.Message);
                        }
                    }

                    int fileCount = br.ReadInt32();
                    
                    for (int i = 0; i < fileCount; i++)
                    {
                        try
                        {
                            string path = br.ReadString();
                            long size = br.ReadInt64();
                            result.Files.Add(new FileEntry
                            {
                                Path = path,
                                Size = size
                            });
                        }
                        catch (Exception ex)
                        {
                            throw new Exception("Error reading file " + i + " of " + fileCount + ": " + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Load failed: " + ex.Message + " (Dirs: " + result.Directories.Count + ", Files: " + result.Files.Count + ")");
            }
            return result;
        }
    }

    // Win32 API declarations for long path support
    public static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        public static extern System.IntPtr FindFirstFile(string lpFileName, out WIN32_FIND_DATA lpFindFileData);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        public static extern bool FindNextFile(System.IntPtr hFindFile, out WIN32_FIND_DATA lpFindFileData);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool FindClose(System.IntPtr hFindFile);

        public const int MAX_PATH = 260;
        public const int MAX_ALTERNATE = 14;
        public static readonly System.IntPtr INVALID_HANDLE_VALUE = new System.IntPtr(-1);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        public struct WIN32_FIND_DATA
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
            public string cFileName;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = MAX_ALTERNATE)]
            public string cAlternateFileName;
        }

        public const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    }

    public class Scanner
    {
        private static long totalItems = 0;
        private static long scannedItems = 0;
        private static DateTime scanStartTime;

        public static ScanResult Scan(string path, Action<string, int> onProgress)
        {
            totalItems = 0;
            scannedItems = 0;
            scanStartTime = DateTime.Now;

            var result = new ScanResult
            {
                ScanDateTicks = DateTime.Now.Ticks,
                RootPath = path
            };

            // Normalize path and add \\?\ prefix for long path support
            string normalizedPath = GetLongPath(path);
            
            // First pass: count items for progress
            CountItems(normalizedPath);
            
            // Second pass: actual scan
            scannedItems = 0;
            ScanRecursive(normalizedPath, result, onProgress);
            
            CalculateDirectorySizes(result);

            return result;
        }

        private static void CountItems(string path)
        {
            string searchPath = path.TrimEnd('\\') + @"\*";
            NativeMethods.WIN32_FIND_DATA findData;
            System.IntPtr hFind = NativeMethods.FindFirstFile(searchPath, out findData);

            if (hFind == NativeMethods.INVALID_HANDLE_VALUE) return;

            try
            {
                do
                {
                    if (findData.cFileName == "." || findData.cFileName == "..") continue;
                    
                    totalItems++;
                    
                    bool isDirectory = (findData.dwFileAttributes & NativeMethods.FILE_ATTRIBUTE_DIRECTORY) != 0;
                    if (isDirectory)
                    {
                        string fullPath = path.TrimEnd('\\') + @"\" + findData.cFileName;
                        CountItems(fullPath);
                    }
                }
                while (NativeMethods.FindNextFile(hFind, out findData));
            }
            finally
            {
                NativeMethods.FindClose(hFind);
            }
        }

        private static string GetLongPath(string path)
        {
            // If already has \\?\ prefix, return as-is
            if (path.StartsWith(@"\\?\")) return path;
            
            // Get full path
            if (!Path.IsPathRooted(path))
                path = Path.GetFullPath(path);
            
            // Add \\?\ prefix for long path support
            // UNC paths need \\?\UNC\ prefix
            if (path.StartsWith(@"\\"))
                return @"\\?\UNC\" + path.Substring(2);
            
            return @"\\?\" + path;
        }

        private static void ScanRecursive(string currentPath, ScanResult result, Action<string, int> onProgress)
        {
            scannedItems++;
            
            if (onProgress != null)
            {
                // Calculate progress percentage
                int percentage = totalItems > 0 ? (int)((scannedItems * 100) / totalItems) : 0;
                
                // Remove \\?\ prefix for display and sanitize for console
                string displayPath = currentPath.Replace(@"\\?\UNC\", @"\\").Replace(@"\\?\", "");
                displayPath = SanitizeForConsole(displayPath);
                
                onProgress(displayPath, percentage);
            }

            string searchPath = currentPath.TrimEnd('\\') + @"\*";
            NativeMethods.WIN32_FIND_DATA findData;
            System.IntPtr hFind = NativeMethods.FindFirstFile(searchPath, out findData);

            if (hFind == NativeMethods.INVALID_HANDLE_VALUE)
            {
                return; // Access denied or other error
            }

            try
            {
                do
                {
                    // Skip . and ..
                    if (findData.cFileName == "." || findData.cFileName == "..") continue;

                    string fullPath = currentPath.TrimEnd('\\') + @"\" + findData.cFileName;
                    bool isDirectory = (findData.dwFileAttributes & NativeMethods.FILE_ATTRIBUTE_DIRECTORY) != 0;

                    if (isDirectory)
                    {
                        // Recurse into subdirectory
                        ScanRecursive(fullPath, result, onProgress);
                    }
                    else
                    {
                        // Add file
                        long fileSize = ((long)findData.nFileSizeHigh << 32) | findData.nFileSizeLow;
                        
                        // Remove \\?\ prefix before storing
                        string storedPath = fullPath.Replace(@"\\?\UNC\", @"\\").Replace(@"\\?\", "");
                        
                        result.Files.Add(new FileEntry
                        {
                            Path = storedPath,
                            Size = fileSize
                        });
                    }
                }
                while (NativeMethods.FindNextFile(hFind, out findData));
            }
            finally
            {
                NativeMethods.FindClose(hFind);
            }
        }

        private static string SanitizeForConsole(string path)
        {
            // Replace characters that can't be displayed in the console
            char[] chars = path.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (char.IsControl(chars[i]) || chars[i] > 127)
                {
                    chars[i] = '?';
                }
            }
            return new string(chars);
        }

        private static void CalculateDirectorySizes(ScanResult result)
        {
            // Map: Directory Path -> Total Size
            var dirSizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            // 1. Sum files in their immediate directories
            foreach (var file in result.Files)
            {
                try
                {
                    string dirPath = Path.GetDirectoryName(file.Path);
                    if (string.IsNullOrEmpty(dirPath)) continue;
                    if (!dirSizes.ContainsKey(dirPath)) dirSizes[dirPath] = 0;
                    dirSizes[dirPath] += file.Size;
                }
                catch { }
            }

            // 2. Propagate up
            var sortedDirs = dirSizes.Keys.OrderByDescending(k => k.Length).ToList();

            foreach (var dir in sortedDirs)
            {
                try
                {
                    long size = dirSizes[dir];
                    string parent = Path.GetDirectoryName(dir);
                    
                    if (string.IsNullOrEmpty(parent)) continue;
                    
                    if (!dirSizes.ContainsKey(parent)) dirSizes[parent] = 0;
                    dirSizes[parent] += size;
                }
                catch { }
            }

            // Convert to List<DirEntry>
            foreach (var kvp in dirSizes)
            {
                result.Directories.Add(new DirEntry { Path = kvp.Key, TotalSize = kvp.Value });
            }
        }
    }

    class Program
    {
        enum State { DriveSelect, Results }
        enum Tab { Files, Directories }

        static State CurrentState = State.DriveSelect;
        static Tab CurrentTab = Tab.Files;
        
        // Drive Select State
        static DriveInfo[] Drives;
        static int DriveIndex = 0;

        // Results State
        static ScanResult CurrentScan;
        static List<object> SortedItems; // FileEntry or DirEntry
        static int SelectedIndex = 0;
        static int ScrollOffset = 0;
        static string SortMode = "Size"; // Size, Name, Path
        static bool Running = true;

        static void Main(string[] args)
        {
            // Set console to UTF-8 to handle Unicode characters
            try
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
            }
            catch
            {
                // If UTF-8 fails, continue with default encoding
            }
            
            Console.WriteLine("SpaceLeft - Disk Usage Analyzer");
            Drives = DriveInfo.GetDrives().Where(d => d.IsReady).ToArray();

            while (Running)
            {
                try
                {
                    if (CurrentState == State.DriveSelect)
                    {
                        DrawDriveSelect();
                        HandleDriveSelectInput();
                    }
                    else if (CurrentState == State.Results)
                    {
                        DrawResults();
                        HandleResultsInput();
                    }
                }
                catch (Exception ex)
                {
                    Console.Clear();
                    Console.WriteLine("An error occurred: " + ex.Message);
                    Console.WriteLine("Press any key to continue...");
                    Console.ReadKey();
                }
            }
        }

        // --- Drive Selection ---

        static void DrawDriveSelect()
        {
            Console.Clear();
            Console.WriteLine("SpaceLeft - Select a Drive to Scan");
            Console.WriteLine(new string('-', Console.WindowWidth - 1));

            for (int i = 0; i < Drives.Length; i++)
            {
                var d = Drives[i];
                bool selected = i == DriveIndex;
                
                string label = d.Name;
                try { label += " [" + d.VolumeLabel + "]"; } catch { }
                
                string lastScan = "Never";
                string filename = GetScanFileName(d.Name);
                if (File.Exists(filename))
                {
                    try
                    {
                        // Peek at date without full load if possible, but for now just check file time or load header?
                        // Loading full file might be slow for just checking date. 
                        // Let's just use file modification time as proxy or load it.
                        // For correctness, let's trust file modification time for now to be fast.
                        lastScan = File.GetLastWriteTime(filename).ToString();
                    }
                    catch { }
                }

                string line = string.Format("{0,-20} | Last Scan: {1}", label, lastScan);

                if (selected)
                {
                    Console.BackgroundColor = ConsoleColor.Gray;
                    Console.ForegroundColor = ConsoleColor.Black;
                }
                Console.WriteLine(line.PadRight(Console.WindowWidth - 1));
                if (selected) Console.ResetColor();
            }
            
            Console.WriteLine("\n[Enter] Load (if available)  [Shift+Enter] Scan  [Esc] Quit");
        }

        static void HandleDriveSelectInput()
        {
            var key = Console.ReadKey(true);
            bool shift = (key.Modifiers & ConsoleModifiers.Shift) != 0;
            
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    if (DriveIndex > 0) DriveIndex--;
                    break;
                case ConsoleKey.DownArrow:
                    if (DriveIndex < Drives.Length - 1) DriveIndex++;
                    break;
                case ConsoleKey.Enter:
                    if (shift)
                        ScanDrive(Drives[DriveIndex]);
                    else
                        LoadOrScanDrive(Drives[DriveIndex]);
                    break;
                case ConsoleKey.Escape:
                case ConsoleKey.Q:
                    Running = false;
                    break;
            }
        }

        static void LoadOrScanDrive(DriveInfo drive)
        {
            string filename = GetScanFileName(drive.Name);
            
            // Load existing data if available
            if (File.Exists(filename))
            {
                try
                {
                    Console.Clear();
                    Console.WriteLine("Loading scan data for " + drive.Name + "...");
                    CurrentScan = Storage.Load(filename);
                    
                    if (CurrentScan != null && CurrentScan.Files != null && CurrentScan.Directories != null)
                    {
                        CurrentState = State.Results;
                        CurrentTab = Tab.Files;
                        SortItems();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed to load: " + ex.Message);
                    Console.WriteLine("Press any key to return...");
                    Console.ReadKey();
                    return;
                }
            }
            else
            {
                Console.Clear();
                Console.WriteLine("No previous scan found for " + drive.Name);
                Console.WriteLine("Press [Shift+Enter] to scan, or [Esc] to go back.");
                Console.ReadKey();
            }
        }

        static void ScanDrive(DriveInfo drive)
        {
            string filename = GetScanFileName(drive.Name);
            
            // Scan the drive
            Console.Clear();
            Console.WriteLine("Scanning " + drive.Name + "...");
            Console.WriteLine("This may take a while. Press Ctrl+C to cancel.");
            Console.WriteLine();
            
            long itemCount = 0;
            
            try
            {
                CurrentScan = Scanner.Scan(drive.Name, (path, percentage) => {
                    itemCount++;
                    if (itemCount % 100 == 0)
                    {
                        // Only show item count - no paths to avoid Unicode errors
                        try
                        {
                            Console.SetCursorPosition(0, 3);
                            Console.Write("Items scanned: " + itemCount.ToString().PadRight(20));
                        }
                        catch
                        {
                            // Ignore console errors
                        }
                    }
                });
                
                Console.WriteLine();
                Console.WriteLine("Scan complete. Saving...");
                Storage.Save(CurrentScan, filename);
                
                CurrentState = State.Results;
                CurrentTab = Tab.Files;
                SortItems();
            }
            catch (Exception ex)
            {
                Console.Clear();
                Console.WriteLine("Scan failed: " + ex.Message);
                Console.WriteLine("Press any key to return...");
                Console.ReadKey();
            }
        }

        static string GetScanFileName(string driveName)
        {
            // driveName is "C:\" -> "C_scan.gz"
            string clean = driveName.Replace(":\\", "").Replace(":", "");
            return clean + "_scan.gz";
        }

        // --- Results View ---

        static void DrawResults()
        {
            Console.Clear();
            string positionInfo = SortedItems.Count > 0 ? string.Format(" | {0}/{1}", SelectedIndex + 1, SortedItems.Count) : "";
            Console.WriteLine(string.Format("Results for {0} | Scanned: {1}{2}", 
                CurrentScan.RootPath, new DateTime(CurrentScan.ScanDateTicks), positionInfo));
            
            // Draw tabs with highlighting
            Console.Write("Tabs: ");
            if (CurrentTab == Tab.Files)
            {
                Console.BackgroundColor = ConsoleColor.Gray;
                Console.ForegroundColor = ConsoleColor.Black;
                Console.Write("[FILES]");
                Console.ResetColor();
            }
            else
                Console.Write(" FILES ");
            
            Console.Write(" ");
            
            if (CurrentTab == Tab.Directories)
            {
                Console.BackgroundColor = ConsoleColor.Gray;
                Console.ForegroundColor = ConsoleColor.Black;
                Console.Write("[DIRECTORIES]");
                Console.ResetColor();
            }
            else
                Console.Write(" DIRECTORIES ");
            Console.WriteLine();
            
            // Draw sort options with highlighting
            Console.Write("[Tab]Switch | ");
            
            if (SortMode == "Size")
            {
                Console.BackgroundColor = ConsoleColor.Gray;
                Console.ForegroundColor = ConsoleColor.Black;
                Console.Write("[S]ize");
                Console.ResetColor();
            }
            else
                Console.Write("[S]ize");
            
            Console.Write(" ");
            
            if (SortMode == "Name")
            {
                Console.BackgroundColor = ConsoleColor.Gray;
                Console.ForegroundColor = ConsoleColor.Black;
                Console.Write("[N]ame");
                Console.ResetColor();
            }
            else
                Console.Write("[N]ame");
            
            Console.Write(" ");
            
            if (SortMode == "Path")
            {
                Console.BackgroundColor = ConsoleColor.Gray;
                Console.ForegroundColor = ConsoleColor.Black;
                Console.Write("[P]ath");
                Console.ResetColor();
            }
            else
                Console.Write("[P]ath");
            
            Console.WriteLine(" | [Enter]Open Explorer [Shift+Enter]Open PS [Ctrl+Enter]Open CMD | [Esc]Back");
            Console.WriteLine(new string('-', Console.WindowWidth - 1));

            int height = Console.WindowHeight - 5;
            for (int i = 0; i < height; i++)
            {
                int index = i + ScrollOffset;
                if (index >= SortedItems.Count) break;

                var item = SortedItems[index];
                bool isSelected = index == SelectedIndex;
                
                if (isSelected)
                {
                    Console.BackgroundColor = ConsoleColor.Gray;
                    Console.ForegroundColor = ConsoleColor.Black;
                }

                string line = "";
                if (CurrentTab == Tab.Files)
                {
                    var f = (FileEntry)item;
                    line = string.Format("{0,-12} {1}", FormatBytes(f.Size), f.Path);
                }
                else
                {
                    var d = (DirEntry)item;
                    line = string.Format("{0,-12} {1}", FormatBytes(d.TotalSize), d.Path);
                }

                // Truncate
                if (line.Length > Console.WindowWidth - 1)
                    line = line.Substring(0, Console.WindowWidth - 1);
                
                Console.WriteLine(line.PadRight(Console.WindowWidth - 1));
                if (isSelected) Console.ResetColor();
            }
        }

        static void HandleResultsInput()
        {
            var key = Console.ReadKey(true);
            bool ctrl = (key.Modifiers & ConsoleModifiers.Control) != 0;
            bool shift = (key.Modifiers & ConsoleModifiers.Shift) != 0;
            
            int pageHeight = Console.WindowHeight - 5;

            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    if (SelectedIndex > 0) SelectedIndex--;
                    if (SelectedIndex < ScrollOffset) ScrollOffset = SelectedIndex;
                    break;
                case ConsoleKey.DownArrow:
                    if (SelectedIndex < SortedItems.Count - 1) SelectedIndex++;
                    if (SelectedIndex >= ScrollOffset + pageHeight) ScrollOffset = SelectedIndex - (pageHeight - 1);
                    break;
                case ConsoleKey.PageUp:
                    SelectedIndex = Math.Max(0, SelectedIndex - pageHeight);
                    if (SelectedIndex < ScrollOffset) ScrollOffset = SelectedIndex;
                    break;
                case ConsoleKey.PageDown:
                    SelectedIndex = Math.Min(SortedItems.Count - 1, SelectedIndex + pageHeight);
                    if (SelectedIndex >= ScrollOffset + pageHeight) ScrollOffset = SelectedIndex - (pageHeight - 1);
                    break;
                case ConsoleKey.Home:
                    SelectedIndex = 0;
                    ScrollOffset = 0;
                    break;
                case ConsoleKey.End:
                    SelectedIndex = Math.Max(0, SortedItems.Count - 1);
                    ScrollOffset = Math.Max(0, SelectedIndex - (pageHeight - 1));
                    break;
                case ConsoleKey.Tab:
                    CurrentTab = CurrentTab == Tab.Files ? Tab.Directories : Tab.Files;
                    SortItems();
                    break;
                case ConsoleKey.S:
                    SortMode = "Size";
                    SortItems();
                    break;
                case ConsoleKey.N:
                    SortMode = "Name";
                    SortItems();
                    break;
                case ConsoleKey.P:
                    SortMode = "Path";
                    SortItems();
                    break;
                case ConsoleKey.Enter:
                    string path = GetSelectedPath();
                    if (shift) OpenPowerShell(path);
                    else if (ctrl) OpenCmd(path);
                    else OpenInExplorer(path);
                    break;
                case ConsoleKey.Escape:
                    CurrentState = State.DriveSelect;
                    break;
            }
        }

        static string GetSelectedPath()
        {
            if (SortedItems == null || SortedItems.Count == 0) return "";
            if (CurrentTab == Tab.Files) return ((FileEntry)SortedItems[SelectedIndex]).Path;
            return ((DirEntry)SortedItems[SelectedIndex]).Path;
        }

        static void SortItems()
        {
            IEnumerable<object> query;
            if (CurrentTab == Tab.Files)
                query = CurrentScan.Files.Cast<object>();
            else
                query = CurrentScan.Directories.Cast<object>();

            // Sort
            // Note: For huge lists, creating a new list every time might be slow.
            // But for < 100k items it's fine.
            
            if (CurrentTab == Tab.Files)
            {
                var q = CurrentScan.Files.AsEnumerable();
                if (SortMode == "Size") q = q.OrderByDescending(x => x.Size);
                else if (SortMode == "Name") q = q.OrderBy(x => Path.GetFileName(x.Path));
                else if (SortMode == "Path") q = q.OrderBy(x => x.Path);
                SortedItems = q.Cast<object>().ToList();
            }
            else
            {
                var q = CurrentScan.Directories.AsEnumerable();
                if (SortMode == "Size") q = q.OrderByDescending(x => x.TotalSize);
                else if (SortMode == "Name") q = q.OrderBy(x => Path.GetFileName(x.Path));
                else if (SortMode == "Path") q = q.OrderBy(x => x.Path);
                SortedItems = q.Cast<object>().ToList();
            }

            SelectedIndex = 0;
            ScrollOffset = 0;
        }

        static void OpenInExplorer(string path)
        {
            if (!PathExists(path))
            {
                AskForRescan();
                return;
            }
            try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { }
        }

        static void OpenCmd(string path)
        {
            if (!PathExists(path))
            {
                AskForRescan();
                return;
            }
            try 
            {
                // If file, get dir
                if (File.Exists(path)) path = Path.GetDirectoryName(path);
                System.Diagnostics.Process.Start("cmd.exe", "/k cd /d \"" + path + "\""); 
            } 
            catch { }
        }

        static void OpenPowerShell(string path)
        {
            if (!PathExists(path))
            {
                AskForRescan();
                return;
            }
            try 
            {
                if (File.Exists(path)) path = Path.GetDirectoryName(path);
                System.Diagnostics.Process.Start("powershell.exe", "-NoExit -Command \"cd '" + path + "'\""); 
            } 
            catch { }
        }

        static bool PathExists(string path)
        {
            return File.Exists(path) || Directory.Exists(path);
        }

        static void AskForRescan()
        {
            Console.Clear();
            Console.WriteLine("This path no longer exists on the disk.");
            Console.WriteLine("The scan data may be outdated.");
            Console.WriteLine();
            Console.WriteLine("[R]escan this drive  [Esc] Cancel");
            
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.R)
            {
                // Find current drive from CurrentScan
                DriveInfo selectedDrive = null;
                foreach (var d in Drives)
                {
                    if (CurrentScan.RootPath.StartsWith(d.Name))
                    {
                        selectedDrive = d;
                        break;
                    }
                }
                
                if (selectedDrive != null)
                {
                    ScanDrive(selectedDrive);
                }
            }
        }

        static string MinimizePath(string path)
        {
            if (path.Length > 60)
                return "..." + path.Substring(path.Length - 57);
            return path.PadRight(60);
        }

        static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return String.Format("{0:0.##} {1}", len, sizes[order]);
        }
    }

}
