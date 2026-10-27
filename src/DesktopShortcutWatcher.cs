using System;
using System.Collections.Generic;
using System.IO;

namespace Polka {
    // Deliberately read-only. The caller owns importing, saving, and deciding
    // when a ready item was handled. Existing desktop files form the baseline.
    internal sealed class DesktopShortcutWatcher : IDisposable {
        sealed class Observation {
            internal long Length, Modified;
            internal int StableSamples;
            internal DateTime FirstSeen;
            internal bool Consumed;
            internal bool ManagedTransfer;
        }
        sealed class Desktop {
            internal string Path;
            internal bool NeedsBaseline;
        }
        readonly List<Desktop> desktops=new List<Desktop>();
        readonly Dictionary<string,Observation> known=new Dictionary<string,Observation>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> reported=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly Action<Exception> onError;
        readonly Func<DateTime> utcNow;
        static readonly TimeSpan RetryPeriod=TimeSpan.FromSeconds(30);
        bool disposed;

        internal DesktopShortcutWatcher(IEnumerable<string> desktopDirectories,Action<Exception> onError=null)
            : this(desktopDirectories,onError,delegate { return DateTime.UtcNow; }) { }

        // Clock injection keeps retries deterministic in isolated file tests.
        internal DesktopShortcutWatcher(IEnumerable<string> desktopDirectories,Action<Exception> onError,Func<DateTime> utcNow) {
            if(desktopDirectories==null) throw new ArgumentNullException("desktopDirectories");
            if(utcNow==null) throw new ArgumentNullException("utcNow");
            this.onError=onError; this.utcNow=utcNow;
            var unique=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(string value in desktopDirectories) {
                if(String.IsNullOrWhiteSpace(value)) continue;
                if(!System.IO.Path.IsPathRooted(value)) throw new ArgumentException("Desktop paths must be absolute.","desktopDirectories");
                string path=System.IO.Path.GetFullPath(value);
                string root=System.IO.Path.GetPathRoot(path);
                if(path.Length>root.Length) path=path.TrimEnd(System.IO.Path.DirectorySeparatorChar,System.IO.Path.AltDirectorySeparatorChar);
                if(unique.Add(path)) desktops.Add(new Desktop { Path=path,NeedsBaseline=true });
            }
            // The first successful scan of each folder is always a baseline,
            // even if an inaccessible desktop recovers only after enabling.
            Scan();
        }

        internal IList<string> Scan() {
            var ready=new List<string>();
            if(disposed) return ready;
            DateTime now=utcNow();
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(Desktop desktop in desktops) {
                try {
                    RejectReparseParents(desktop.Path);
                    string[] files;
                    try { files=Directory.GetFiles(desktop.Path,"*",SearchOption.TopDirectoryOnly); }
                    catch(DirectoryNotFoundException) { files=new string[0]; }
                    catch(FileNotFoundException) { files=new string[0]; }
                    Array.Sort(files,StringComparer.OrdinalIgnoreCase);
                    foreach(string path in files) {
                        if(!Supported(path)) continue;
                        seen.Add(path);
                        Observation observation;
                        bool exists=known.TryGetValue(path,out observation);
                        if(desktop.NeedsBaseline) {
                            if(!exists) known[path]=new Observation { Consumed=true,FirstSeen=now };
                            continue;
                        }
                        if(exists && observation.Consumed) continue;
                        if(!exists) {
                            observation=new Observation { FirstSeen=now };
                            known.Add(path,observation);
                        }
                        if(!observation.ManagedTransfer && now-observation.FirstSeen>=RetryPeriod) { observation.Consumed=true; continue; }
                        try {
                            FileAttributes attributes=File.GetAttributes(path);
                            if((attributes&(FileAttributes.Directory|FileAttributes.ReparsePoint))!=0) {
                                observation.Consumed=true;
                                continue;
                            }
                            var info=new FileInfo(path);
                            long length=info.Length,modified=info.LastWriteTimeUtc.Ticks;
                            if(observation.StableSamples==0 || observation.Length!=length || observation.Modified!=modified) {
                                observation.Length=length; observation.Modified=modified; observation.StableSamples=1;
                            } else if(observation.StableSamples<2) observation.StableSamples++;
                            if(length>0 && observation.StableSamples>=2 && ReadableUnchanged(path,observation)) ready.Add(path);
                        } catch(IOException) { /* Another process is still writing, renaming or deleting it. */ }
                        catch(UnauthorizedAccessException) { /* Retry a temporarily unavailable shortcut. */ }
                    }
                    desktop.NeedsBaseline=false;
                    reported.Remove(desktop.Path);
                } catch(IOException error) { PreserveDesktop(desktop.Path,seen); Report(desktop.Path,error); }
                catch(UnauthorizedAccessException error) { PreserveDesktop(desktop.Path,seen); Report(desktop.Path,error); }
            }
            var removed=new List<string>();
            foreach(string path in known.Keys) if(!seen.Contains(path)) removed.Add(path);
            foreach(string path in removed) known.Remove(path);
            return ready;
        }

        internal void Acknowledge(string path) {
            if(disposed || String.IsNullOrEmpty(path)) return;
            Observation observation;
            if(known.TryGetValue(path,out observation)) { observation.Consumed=true; return; }
            // A shortcut restored by the dock must not be immediately imported
            // again. Suppress only that exact existing path, never rebaseline
            // other pending additions on either desktop.
            try {
                if(!System.IO.Path.IsPathRooted(path) || !Supported(path)) return;
                string full=System.IO.Path.GetFullPath(path),directory=System.IO.Path.GetDirectoryName(full);
                bool watched=false;
                foreach(Desktop desktop in desktops) if(String.Equals(desktop.Path,directory,StringComparison.OrdinalIgnoreCase)) { watched=true; break; }
                if(!watched) return;
                RejectReparseParents(directory);
                if((File.GetAttributes(full)&(FileAttributes.Directory|FileAttributes.ReparsePoint))!=0) return;
                known[full]=new Observation { Consumed=true,FirstSeen=utcNow() };
            } catch(IOException) { }
            catch(UnauthorizedAccessException) { }
            catch(ArgumentException) { }
            catch(NotSupportedException) { }
        }

        // A verified transfer removed this exact source. Forget immediately,
        // even if an installer has recreated it before the next directory scan.
        // Deliberate desktop restores must use Acknowledge instead.
        internal void ForgetTransferred(string path) {
            if(disposed || String.IsNullOrWhiteSpace(path)) return;
            try { string full; if(TryWatchedShortcut(path,out full)) known.Remove(full); }
            catch(ArgumentException) { }
            catch(NotSupportedException) { }
        }

        // Only a caller with a durable managed copy may extend this retry.
        // Ordinary invalid/unhandled new files still expire after RetryPeriod.
        internal void DeferManagedTransfer(string path) {
            if(disposed || String.IsNullOrWhiteSpace(path)) return;
            try {
                string full; if(!TryWatchedShortcut(path,out full)) return;
                Observation observation;
                if(!known.TryGetValue(full,out observation)) {
                    known[full]=new Observation {FirstSeen=utcNow(),ManagedTransfer=true};
                } else {
                    if(observation.Consumed) observation.StableSamples=0;
                    observation.Consumed=false; observation.FirstSeen=utcNow(); observation.ManagedTransfer=true;
                }
            } catch(ArgumentException) { }
            catch(NotSupportedException) { }
        }

        bool TryWatchedShortcut(string path,out string full) {
            full=null;
            if(!System.IO.Path.IsPathRooted(path) || !Supported(path)) return false;
            full=System.IO.Path.GetFullPath(path);
            string parent=System.IO.Path.GetDirectoryName(full);
            foreach(Desktop desktop in desktops) if(String.Equals(parent,desktop.Path,StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static bool Supported(string path) {
            string extension=System.IO.Path.GetExtension(path);
            return String.Equals(extension,".lnk",StringComparison.OrdinalIgnoreCase) || String.Equals(extension,".url",StringComparison.OrdinalIgnoreCase);
        }

        static bool ReadableUnchanged(string path,Observation observation) {
            // FileShare.Read refuses an active writer, including one allowing
            // readers, rather than handing a half-written shortcut to Store.
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                if(stream.Length!=observation.Length) return false;
                FileAttributes attributes=File.GetAttributes(path);
                if((attributes&(FileAttributes.Directory|FileAttributes.ReparsePoint))!=0) return false;
                return File.GetLastWriteTimeUtc(path).Ticks==observation.Modified;
            }
        }

        static void RejectReparseParents(string path) {
            string current=path;
            while(!String.IsNullOrEmpty(current)) {
                try {
                    if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
                        throw new IOException("Desktop shortcut watching does not follow redirected folders: "+current);
                } catch(FileNotFoundException) { }
                catch(DirectoryNotFoundException) { }
                current=System.IO.Path.GetDirectoryName(current);
            }
        }

        void PreserveDesktop(string directory,HashSet<string> seen) {
            string prefix=directory.TrimEnd(System.IO.Path.DirectorySeparatorChar,System.IO.Path.AltDirectorySeparatorChar)+System.IO.Path.DirectorySeparatorChar;
            foreach(string path in known.Keys) if(path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)) seen.Add(path);
        }
        void Report(string directory,Exception error) {
            if(onError!=null && reported.Add(directory)) {
                try { onError(error); } catch { /* A reporting callback cannot break observation. */ }
            }
        }
        public void Dispose() { disposed=true; known.Clear(); desktops.Clear(); reported.Clear(); }
    }
}
