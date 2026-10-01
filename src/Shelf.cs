using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Traymio {
    public sealed class AddSummary {
        public int Added, Duplicate, Unavailable, OverLimit;
        public override string ToString() {
            var parts=new List<string>();
            parts.Add(Added+" added");
            if(Duplicate>0)parts.Add(Duplicate+" already in tray");
            if(Unavailable>0)parts.Add(Unavailable+" unavailable");
            if(OverLimit>0)parts.Add("tray limit reached (300)");
            return string.Join(" / ",parts.ToArray());
        }
    }
    public sealed class Shelf {
        public const int Limit=300;
        readonly List<string> paths=new List<string>();
        List<string> undo;
        public string[] Paths { get { return paths.ToArray(); } }
        public bool CanUndo { get { return undo!=null; } }
        public static bool Exists(string path) { return File.Exists(path)||Directory.Exists(path); }
        public AddSummary Add(IEnumerable<string> candidates) {
            var result=new AddSummary();var known=new HashSet<string>(paths,StringComparer.OrdinalIgnoreCase);
            foreach(string candidate in candidates) {
                string full;
                try {
                    if(string.IsNullOrWhiteSpace(candidate)||!Path.IsPathRooted(candidate)) {result.Unavailable++;continue;}
                    full=Path.GetFullPath(candidate);
                    if(full.Length>Path.GetPathRoot(full).Length)full=full.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
                    if(!Exists(full)){result.Unavailable++;continue;}
                }catch(Exception ex){if(ex is ArgumentException||ex is NotSupportedException||ex is PathTooLongException||ex is System.Security.SecurityException){result.Unavailable++;continue;}throw;}
                if(known.Contains(full)){result.Duplicate++;continue;}
                if(paths.Count>=Limit){result.OverLimit++;continue;}
                paths.Add(full);known.Add(full);result.Added++;
            }
            return result;
        }
        public void Remove(IEnumerable<string> selected) {
            var set=new HashSet<string>(selected,StringComparer.OrdinalIgnoreCase);
            if(!paths.Any(p=>set.Contains(p)))return;
            undo=new List<string>(paths);paths.RemoveAll(p=>set.Contains(p));
        }
        public void Clear() {if(paths.Count==0)return;undo=new List<string>(paths);paths.Clear();}
        public void Undo() {
            if(undo==null)return;
            var current=paths.ToArray();paths.Clear();paths.AddRange(undo);
            var known=new HashSet<string>(paths,StringComparer.OrdinalIgnoreCase);
            foreach(string p in current)if(paths.Count<Limit&&known.Add(p))paths.Add(p);
            undo=null;
        }
        public static DataObject Transfer(IEnumerable<string> selected) {
            string[] values=selected.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if(values.Length==0)throw new InvalidOperationException("Add or select files first.");
            if(values.Any(p=>!Exists(p)))throw new InvalidOperationException("Some selected items are missing or unavailable. Remove them from the tray, then try again.");
            var data=new DataObject();var collection=new StringCollection();collection.AddRange(values);data.SetFileDropList(collection);
            // Tell Explorer that clipboard paste should copy, never cut.
            data.SetData("Preferred DropEffect",new MemoryStream(BitConverter.GetBytes((int)DragDropEffects.Copy)));
            return data;
        }
        public static DragDropEffects Accept(IDataObject data,DragDropEffects allowed) {
            if(data!=null&&data.GetDataPresent(DataFormats.FileDrop)&&(allowed&DragDropEffects.Copy)!=0)return DragDropEffects.Copy;
            return DragDropEffects.None;
        }
    }
}
