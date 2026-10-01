using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace Traymio {
    static class Tests {
        static readonly List<string> checks=new List<string>();
        static string log;
        static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks.Add("PASS  "+message);File.AppendAllText(log,"PASS  "+message+Environment.NewLine);}
        static string Hash(string path){using(var sha=SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));}
        static void Shot(Form form,string path){Application.DoEvents();form.PerformLayout();using(var b=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(b,new Rectangle(Point.Empty,b.Size));b.Save(path,ImageFormat.Png);}}
        internal static void Run(string folder){
            Directory.CreateDirectory(folder);log=Path.Combine(folder,"progress.txt");string one=Path.Combine(folder,"Project brief.txt"),two=Path.Combine(folder,"Hero artwork.svg"),three=Path.Combine(folder,"Exports"),unicode=Path.Combine(folder,"नोट्स - notes.txt");
            Directory.CreateDirectory(three);File.WriteAllText(one,"A synthetic test file. No personal content.");File.WriteAllText(two,"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><rect width=\"64\" height=\"64\" fill=\"#bbe1c6\"/></svg>");File.WriteAllText(unicode,"Unicode filename test");
            string before=Hash(one);var shelf=new Shelf();var added=shelf.Add(new[]{one,two,three});Check(added.Added==3&&shelf.Paths.Length==3,"Collect files and folders from paths");
            Check(shelf.Add(new[]{one.ToUpperInvariant(),three+Path.DirectorySeparatorChar}).Duplicate==2,"Deduplicate case and trailing directory separators");
            Check(shelf.Add(new[]{"relative.txt",Path.Combine(folder,"missing.txt"),null}).Unavailable==3,"Reject missing, null, and relative paths");
            Check(shelf.Add(new[]{unicode}).Added==1,"Unicode and space-containing filenames supported");
            string[] defensive=shelf.Paths;defensive[0]="changed";Check(shelf.Paths[0]==one,"Returned path arrays cannot mutate the shelf");
            shelf.Remove(new[]{one});Check(!shelf.Paths.Contains(one)&&File.Exists(one)&&Hash(one)==before,"Remove only removes tray references");
            shelf.Undo();Check(shelf.Paths.Contains(one)&&!shelf.CanUndo,"Undo restores removed entries");
            shelf.Clear();Check(shelf.Paths.Length==0&&File.Exists(one)&&Directory.Exists(three),"Clear leaves source files and folders untouched");
            shelf.Undo();Check(shelf.Paths.Length==4,"Undo restores a cleared tray");
            string newer=Path.Combine(folder,"New file.txt");File.WriteAllText(newer,"new");shelf.Remove(new[]{one});shelf.Add(new[]{newer});shelf.Undo();Check(shelf.Paths.Contains(one)&&shelf.Paths.Contains(newer),"Undo keeps newly added files");
            var data=Shelf.Transfer(new[]{one,two,unicode,three});Check(data.GetDataPresent(DataFormats.FileDrop),"Transfer advertises the Windows file-drop format");
            string[] transferred=(string[])data.GetData(DataFormats.FileDrop);Check(transferred.SequenceEqual(new[]{one,two,unicode,three}),"Drag payload contains exact selected paths");
            var effect=data.GetData("Preferred DropEffect") as MemoryStream;Check(effect!=null&&BitConverter.ToInt32(effect.ToArray(),0)==(int)DragDropEffects.Copy,"Clipboard preferred effect is copy, not move");
            Check(Shelf.Accept(data,DragDropEffects.All)==DragDropEffects.Copy,"Incoming file drops negotiate copy only");
            Check(Shelf.Accept(data,DragDropEffects.Move)==DragDropEffects.None,"Move-only sources are not accepted");
            var text=new DataObject(DataFormats.Text,"not a file");Check(Shelf.Accept(text,DragDropEffects.All)==DragDropEffects.None,"Plain text drops are rejected");
            bool failed=false;try{Shelf.Transfer(new string[0]);}catch(InvalidOperationException){failed=true;}Check(failed,"Empty transfers are rejected");
            string deleted=Path.Combine(folder,"Gone.txt");File.WriteAllText(deleted,"temp");var stale=new Shelf();stale.Add(new[]{deleted});File.Delete(deleted);failed=false;try{Shelf.Transfer(stale.Paths);}catch(InvalidOperationException){failed=true;}Check(failed,"Unavailable sources block transfer instead of silently omitting items");
            string destination=Path.Combine(folder,"Copy destination");Directory.CreateDirectory(destination);foreach(string path in transferred.Where(File.Exists))File.Copy(path,Path.Combine(destination,Path.GetFileName(path)),true);
            Check(Hash(Path.Combine(destination,Path.GetFileName(one)))==before&&Hash(one)==before,"A receiver can copy the payload without changing the source");
            var capped=new Shelf();var many=new List<string>();for(int n=0;n<302;n++){string path=Path.Combine(folder,"limit-"+n+".txt");File.WriteAllText(path,"test");many.Add(path);}var summary=capped.Add(many);Check(capped.Paths.Length==300&&summary.OverLimit==2,"Tray enforces its 300-item limit");
            using(var form=new MainForm(true)){
                form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.ShowInTaskbar=false;form.Show();Shot(form,Path.Combine(folder,"empty.png"));
                var enter=new DragEventArgs(data,0,0,0,DragDropEffects.All,DragDropEffects.None);form.DragOverTray(form,enter);Check(enter.Effect==DragDropEffects.Copy,"Window drag-enter handler advertises copy");
                var drop=new DragEventArgs(data,0,0,0,DragDropEffects.All,DragDropEffects.None);form.DropFiles(form,drop);Check(drop.Effect==DragDropEffects.Copy&&form.shelf.Paths.Length==4,"Window drop handler collects all file references");
                Check(form.list.Items.Count==4,"List renders the collected items");form.list.Items[0].Selected=true;form.list.Items[1].Selected=true;Application.DoEvents();Shot(form,Path.Combine(folder,"collected.png"));
                form.Size=form.MinimumSize;Shot(form,Path.Combine(folder,"compact.png"));Check(form.Controls[0].Width==form.ClientSize.Width,"Layout follows minimum-size window resizing");
                form.Hide();form.ShowShelf();Check(form.Visible,"Hidden tray can be shown again");form.Close();
            }
            using(var icon=Theme.CreateIcon())using(var file=File.Create(Path.Combine(folder,"Traymio.ico")))icon.Save(file);
            checks.Add("Tests use synthetic files and a local payload receiver. They do not alter the real clipboard, register a global hotkey, or perform a live drag into Explorer. Manual checks remain: external drag/drop compatibility, clipboard integration, notification icon, hotkey conflicts, network paths, and mixed-DPI displays.");
            File.WriteAllLines(Path.Combine(folder,"PASSED.txt"),checks.ToArray());
        }
    }
}
