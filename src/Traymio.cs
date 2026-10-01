using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[assembly: AssemblyTitle("Traymio")]
[assembly: AssemblyProduct("Traymio")]
[assembly: AssemblyDescription("A floating file shelf for Windows")]
[assembly: AssemblyVersion("1.0.0.0")]

namespace Traymio {
    static class Native {
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll",SetLastError=true)] internal static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr h,int id);
        [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
    }
    static class Theme {
        public static readonly Color Bg=Color.FromArgb(20,27,33), Card=Color.FromArgb(29,38,45), Ink=Color.FromArgb(238,242,234), Dim=Color.FromArgb(159,173,178), Accent=Color.FromArgb(187,225,198), Edge=Color.FromArgb(53,67,74);
        public static Label Label(string text,float size,bool bold){return new Label{Text=text,ForeColor=Ink,Font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular),AutoSize=false};}
        public static Button Button(string text,bool primary){
            var b=new Button{Text=text,FlatStyle=FlatStyle.Flat,Height=36,BackColor=primary?Accent:Card,ForeColor=primary?Bg:Ink,Cursor=Cursors.Hand,Font=new Font("Segoe UI",9,FontStyle.Bold),UseVisualStyleBackColor=false};
            b.FlatAppearance.BorderColor=Edge;b.FlatAppearance.BorderSize=primary?0:1;
            b.EnabledChanged+=delegate{b.BackColor=b.Enabled?(primary?Accent:Card):Color.FromArgb(51,63,67);};return b;
        }
        public static Icon CreateIcon(){
            using(var b=new Bitmap(64,64))using(var g=Graphics.FromImage(b)){
                g.Clear(Bg);g.SmoothingMode=SmoothingMode.AntiAlias;
                using(var p=new Pen(Accent,5)){g.DrawLines(p,new Point[]{new Point(13,34),new Point(13,48),new Point(51,48),new Point(51,34)});g.DrawLine(p,32,12,32,35);g.DrawLines(p,new Point[]{new Point(23,27),new Point(32,36),new Point(41,27)});}
                IntPtr handle=b.GetHicon();try{using(var icon=Icon.FromHandle(handle))return (Icon)icon.Clone();}finally{Native.DestroyIcon(handle);}
            }
        }
    }
    sealed class DropHint : Control {
        internal bool Active;
        internal DropHint(){DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw,true);}
        protected override void OnPaint(PaintEventArgs e){
            var g=e.Graphics;g.Clear(Theme.Bg);g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var fill=new SolidBrush(Active?Color.FromArgb(46,72,61):Theme.Card))g.FillRectangle(fill,0,0,Width-1,Height-1);
            using(var p=new Pen(Active?Theme.Accent:Theme.Edge,1)){p.DashStyle=DashStyle.Dash;g.DrawRectangle(p,1,1,Math.Max(1,Width-3),Math.Max(1,Height-3));}
            using(var font=new Font("Segoe UI",9))TextRenderer.DrawText(g,Active?"Release to add to your tray":"+   Drop files or folders anywhere here",font,ClientRectangle,Active?Theme.Accent:Theme.Dim,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        }
    }
    sealed class ShelfList : ListView {
        internal ShelfList(){DoubleBuffered=true;SetStyle(ControlStyles.OptimizedDoubleBuffer,true);}
    }
    sealed class MainForm : Form {
        internal readonly Shelf shelf=new Shelf();
        internal readonly ShelfList list=new ShelfList();
        readonly Label count=Theme.Label("0 ITEMS",9,true), status=Theme.Label("Collect here. Drag out. Keep your originals.",9,false), hint=Theme.Label("Ctrl+Alt+T to show or hide",8,false);
        readonly Button dragAll=Theme.Button("Drag all files out",true),copy=Theme.Button("Copy all",false),remove=Theme.Button("Remove",false),undo=Theme.Button("Undo",false),clear=Theme.Button("Clear tray",false);
        readonly DropHint drop=new DropHint();
        readonly Panel content=new Panel();readonly Label emptyTitle=Theme.Label("Give your files a landing spot.",13,true),emptyText=Theme.Label("Collect from different folders.\nDrag them out together when you're ready.",9,false);
        readonly CheckBox pin=new CheckBox{Text="Keep on top",AutoSize=true,Checked=true,ForeColor=Theme.Dim};
        readonly NotifyIcon notification;readonly Icon appIcon;readonly ImageList rowImages=new ImageList();readonly Bitmap rowImage;readonly Timer refresh=new Timer();readonly ToolTip tips=new ToolTip();
        readonly bool testMode;bool hotkey,outgoing,released;Point dragStart;bool armed;
        internal MainForm(bool testing){
            testMode=testing;SuspendLayout();AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
            Text="Traymio";ClientSize=new Size(470,615);MinimumSize=new Size(450,540);BackColor=Theme.Bg;ForeColor=Theme.Ink;Font=new Font("Segoe UI",9);StartPosition=FormStartPosition.CenterScreen;TopMost=!testMode;AllowDrop=true;
            appIcon=Theme.CreateIcon();Icon=appIcon;
            var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20,16,20,14),ColumnCount=1,RowCount=8};
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,77));root.RowStyles.Add(new RowStyle(SizeType.Absolute,43));root.RowStyles.Add(new RowStyle(SizeType.Absolute,43));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,39));root.RowStyles.Add(new RowStyle(SizeType.Absolute,44));root.RowStyles.Add(new RowStyle(SizeType.Absolute,55));root.RowStyles.Add(new RowStyle(SizeType.Absolute,25));Controls.Add(root);
            var header=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};var brand=Theme.Label("Traymio",25,true);brand.SetBounds(0,0,230,45);header.Controls.Add(brand);
            var sub=Theme.Label("A little space between folders.",9,false);sub.ForeColor=Theme.Dim;sub.SetBounds(1,49,310,24);header.Controls.Add(sub);count.TextAlign=ContentAlignment.MiddleRight;count.SetBounds(310,14,114,24);header.Controls.Add(count);header.Resize+=delegate{count.Left=header.ClientSize.Width-count.Width;};root.Controls.Add(header,0,0);
            var actions=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};actions.RowStyles.Add(new RowStyle(SizeType.Percent,100));for(int i=0;i<3;i++)actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,33.33f));
            var add=Theme.Button("+ Files",false);var folder=Theme.Button("+ Folder",false);var paste=Theme.Button("Paste files",false);
            int index=0;foreach(var b in new[]{add,folder,paste}){b.Dock=DockStyle.Top;b.Margin=new Padding(0,0,index<2?7:0,0);actions.Controls.Add(b,index++,0);}root.Controls.Add(actions,0,1);
            drop.Dock=DockStyle.Fill;drop.Margin=new Padding(0,1,0,7);root.Controls.Add(drop,0,2);
            content.Dock=DockStyle.Fill;content.BackColor=Theme.Card;content.Margin=Padding.Empty;root.Controls.Add(content,0,3);
            list.Dock=DockStyle.Fill;list.View=View.Details;list.HeaderStyle=ColumnHeaderStyle.None;list.Columns.Add("Files",400);list.BorderStyle=BorderStyle.None;list.BackColor=Theme.Card;list.ForeColor=Theme.Ink;list.FullRowSelect=true;list.MultiSelect=true;list.HideSelection=false;list.OwnerDraw=true;list.ShowItemToolTips=true;list.AllowDrop=true;
            using(var screen=Graphics.FromHwnd(IntPtr.Zero))rowImages.ImageSize=new Size(1,(int)(54*screen.DpiX/96));rowImage=new Bitmap(1,rowImages.ImageSize.Height);rowImages.Images.Add(rowImage);list.SmallImageList=rowImages;content.Controls.Add(list);
            emptyTitle.TextAlign=ContentAlignment.MiddleCenter;emptyText.TextAlign=ContentAlignment.MiddleCenter;emptyText.ForeColor=Theme.Dim;content.Controls.Add(emptyTitle);content.Controls.Add(emptyText);content.Resize+=delegate{PositionEmpty();};
            var secondary=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=new Padding(0,7,0,0)};secondary.RowStyles.Add(new RowStyle(SizeType.Percent,100));for(int i=0;i<3;i++)secondary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,33.33f));index=0;foreach(var b in new[]{remove,undo,clear}){b.Dock=DockStyle.Fill;b.Margin=new Padding(0,0,index<2?7:0,0);secondary.Controls.Add(b,index++,0);}root.Controls.Add(secondary,0,4);
            var export=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=new Padding(0,7,0,0)};export.RowStyles.Add(new RowStyle(SizeType.Percent,100));export.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,64));export.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,36));dragAll.Dock=DockStyle.Fill;copy.Dock=DockStyle.Fill;dragAll.Margin=new Padding(0,0,7,0);copy.Margin=Padding.Empty;export.Controls.Add(dragAll,0,0);export.Controls.Add(copy,1,0);root.Controls.Add(export,0,5);
            status.Dock=DockStyle.Fill;status.ForeColor=Theme.Dim;status.Padding=new Padding(0,9,0,0);status.Margin=Padding.Empty;root.Controls.Add(status,0,6);
            var bottom=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};bottom.RowStyles.Add(new RowStyle(SizeType.Percent,100));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,110));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,45));pin.Dock=DockStyle.Fill;pin.Margin=Padding.Empty;bottom.Controls.Add(pin,0,0);hint.Dock=DockStyle.Fill;hint.ForeColor=Theme.Dim;hint.TextAlign=ContentAlignment.MiddleCenter;bottom.Controls.Add(hint,1,0);
            var hide=new LinkLabel{Text="Hide",Dock=DockStyle.Fill,LinkColor=Theme.Accent,ActiveLinkColor=Theme.Ink,TextAlign=ContentAlignment.MiddleRight};bottom.Controls.Add(hide,2,0);root.Controls.Add(bottom,0,7);
            var menu=new ContextMenuStrip();menu.Items.Add("Show Traymio",null,delegate{ShowShelf();});menu.Items.Add("Hide",null,delegate{HideShelf();});menu.Items.Add(new ToolStripSeparator());menu.Items.Add("Exit",null,delegate{Close();});
            notification=new NotifyIcon{Text="Traymio - floating file shelf",Icon=appIcon,ContextMenuStrip=menu,Visible=false};if(!testMode)notification.Visible=true;notification.DoubleClick+=delegate{ShowShelf();};
            add.Click+=delegate{using(var dialog=new OpenFileDialog{Title="Add files to Traymio",Multiselect=true,CheckFileExists=true,Filter="All files (*.*)|*.*"})if(dialog.ShowDialog(this)==DialogResult.OK)AddPaths(dialog.FileNames);};
            folder.Click+=delegate{using(var dialog=new FolderBrowserDialog{Description="Choose a folder to add to the tray",ShowNewFolderButton=false})if(dialog.ShowDialog(this)==DialogResult.OK)AddPaths(new[]{dialog.SelectedPath});};
            paste.Click+=delegate{PasteFiles();};copy.Click+=delegate{CopyFiles();};remove.Click+=delegate{RemoveSelected();};clear.Click+=delegate{shelf.Clear();RefreshList();Message("Tray cleared. Files on disk are unchanged.");};undo.Click+=delegate{shelf.Undo();RefreshList();Message("Tray items restored.");};
            pin.CheckedChanged+=delegate{TopMost=pin.Checked&&!testMode;};hide.LinkClicked+=delegate{HideShelf();};
            list.DrawColumnHeader+=delegate(object s,DrawListViewColumnHeaderEventArgs e){e.DrawDefault=true;};list.DrawItem+=DrawItem;list.DrawSubItem+=delegate{};
            list.Resize+=delegate{FitListColumn();};list.SelectedIndexChanged+=delegate{UpdateActions();};list.ItemDrag+=delegate{BeginTransfer(Selected());};
            list.DoubleClick+=delegate{Reveal();};
            var itemMenu=new ContextMenuStrip();itemMenu.Items.Add("Copy selected files",null,delegate{CopyFiles();});itemMenu.Items.Add("Show in File Explorer",null,delegate{Reveal();});itemMenu.Items.Add("Remove from tray",null,delegate{RemoveSelected();});itemMenu.Items.Add("Remove unavailable items",null,delegate{shelf.Remove(shelf.Paths.Where(p=>!Shelf.Exists(p)));RefreshList();});list.ContextMenuStrip=itemMenu;
            dragAll.MouseDown+=delegate(object s,MouseEventArgs e){if(e.Button==MouseButtons.Left){armed=true;dragStart=e.Location;}};
            dragAll.MouseMove+=delegate(object s,MouseEventArgs e){if(armed&&e.Button==MouseButtons.Left&&(Math.Abs(e.X-dragStart.X)>SystemInformation.DragSize.Width/2||Math.Abs(e.Y-dragStart.Y)>SystemInformation.DragSize.Height/2)){armed=false;BeginTransfer(shelf.Paths);}};
            dragAll.MouseUp+=delegate{if(armed){armed=false;Message("Hold this button, then drag into a folder or compatible app.");}};
            foreach(Control c in new Control[]{this,drop,content,list,emptyTitle,emptyText}){c.AllowDrop=true;c.DragEnter+=DragOverTray;c.DragOver+=DragOverTray;c.DragLeave+=delegate{drop.Active=false;drop.Invalidate();};c.DragDrop+=DropFiles;}
            KeyPreview=true;KeyDown+=KeysPressed;
            tips.SetToolTip(dragAll,"Hold and drag this button to copy every tray item into File Explorer or a compatible app.");tips.SetToolTip(copy,"Copies selected files, or all files if none are selected. Paste with Ctrl+V in File Explorer.");tips.SetToolTip(clear,"Only clears references from Traymio. Never deletes files.");tips.SetToolTip(pin,"Keep Traymio above ordinary windows while you collect files.");
            refresh.Interval=4000;refresh.Tick+=delegate{if(Visible&&!outgoing){UpdateMissing();}};if(!testMode)refresh.Start();
            RefreshList();ResumeLayout(true);
        }
        void PositionEmpty(){emptyTitle.SetBounds(12,Math.Max(12,content.Height/2-36),Math.Max(1,content.Width-24),32);emptyText.SetBounds(16,content.Height/2+3,Math.Max(1,content.Width-32),52);}
        internal void AddPaths(IEnumerable<string> paths){try{AddSummary result=shelf.Add(paths);RefreshList();Message(result+". Originals stay where they are.");}catch(Exception ex){Message(ex.Message,true);}}
        internal void RefreshList(){var selected=new HashSet<string>(Selected(),StringComparer.OrdinalIgnoreCase);list.BeginUpdate();list.Items.Clear();foreach(string p in shelf.Paths){var item=new ListViewItem(Path.GetFileName(p)){Tag=p,ToolTipText=p};item.Selected=selected.Contains(p);list.Items.Add(item);}list.EndUpdate();list.Visible=shelf.Paths.Length>0;emptyTitle.Visible=emptyText.Visible=!list.Visible;PositionEmpty();UpdateActions();FitListColumn();}
        void FitListColumn(){if(!list.IsHandleCreated||list.IsDisposed)return;list.BeginInvoke((MethodInvoker)delegate{if(!list.IsDisposed&&list.Columns.Count>0)list.Columns[0].Width=Math.Max(1,list.ClientSize.Width-SystemInformation.VerticalScrollBarWidth-8);});}
        string[] Selected(){return list.SelectedItems.Cast<ListViewItem>().Select(x=>(string)x.Tag).ToArray();}
        string[] SelectionOrAll(){var selected=Selected();return selected.Length>0?selected:shelf.Paths;}
        void UpdateActions(){int n=shelf.Paths.Length;count.Text=n+" "+(n==1?"ITEM":"ITEMS");copy.Text=Selected().Length>0?"Copy selected":"Copy all";copy.Enabled=dragAll.Enabled=clear.Enabled=n>0;remove.Enabled=Selected().Length>0;undo.Enabled=shelf.CanUndo;}
        void UpdateMissing(){list.Invalidate();}
        void DrawItem(object sender,DrawListViewItemEventArgs e){
            string p=(string)e.Item.Tag;bool exists=Shelf.Exists(p),dir=Directory.Exists(p);var g=e.Graphics;bool selected=e.Item.Selected;
            using(var b=new SolidBrush(selected?Color.FromArgb(47,67,62):Theme.Card))g.FillRectangle(b,e.Bounds);
            float scale=DeviceScale();int pad=(int)(11*scale),badge=(int)(32*scale);var box=new Rectangle(e.Bounds.X+pad,e.Bounds.Y+(e.Bounds.Height-badge)/2,badge,badge);
            using(var b=new SolidBrush(dir?Color.FromArgb(78,69,45):Color.FromArgb(43,64,77)))g.FillRectangle(b,box);
            string ext=dir?"DIR":Path.GetExtension(p).TrimStart('.').ToUpperInvariant();if(ext.Length==0)ext="FILE";if(ext.Length>4)ext=ext.Substring(0,4);
            using(var f=new Font("Segoe UI",7,FontStyle.Bold))TextRenderer.DrawText(g,ext,f,box,dir?Color.FromArgb(236,210,151):Color.FromArgb(163,211,232),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            int left=box.Right+(int)(12*scale),textWidth=Math.Max(1,e.Bounds.Right-left-pad);
            string name=Path.GetFileName(p);if(string.IsNullOrEmpty(name))name=p;
            using(var f=new Font("Segoe UI",9.5f,FontStyle.Bold))TextRenderer.DrawText(g,name,f,new Rectangle(left,e.Bounds.Y+(int)(8*scale),textWidth,(int)(20*scale)),exists?Theme.Ink:Color.FromArgb(230,169,144),TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
            string parent=exists?(testMode?"Demo files":(Path.GetDirectoryName(p)??p)):"Unavailable - moved, removed, or disconnected";
            using(var f=new Font("Segoe UI",8))TextRenderer.DrawText(g,parent,f,new Rectangle(left,e.Bounds.Y+(int)(29*scale),textWidth,(int)(19*scale)),Theme.Dim,TextFormatFlags.PathEllipsis|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
            using(var pen=new Pen(Theme.Edge))g.DrawLine(pen,e.Bounds.X+pad,e.Bounds.Bottom-1,e.Bounds.Right-pad,e.Bounds.Bottom-1);
            if(e.Item.Focused&&list.Focused)e.DrawFocusRectangle();
        }
        float DeviceScale(){using(var g=CreateGraphics())return g.DpiX/96f;}
        internal void DragOverTray(object sender,DragEventArgs e){try{e.Effect=outgoing?DragDropEffects.None:Shelf.Accept(e.Data,e.AllowedEffect);}catch{e.Effect=DragDropEffects.None;}drop.Active=e.Effect!=DragDropEffects.None;drop.Invalidate();}
        internal void DropFiles(object sender,DragEventArgs e){drop.Active=false;drop.Invalidate();try{if(outgoing||Shelf.Accept(e.Data,e.AllowedEffect)==DragDropEffects.None){e.Effect=DragDropEffects.None;return;}var paths=e.Data.GetData(DataFormats.FileDrop) as string[];if(paths!=null)AddPaths(paths);e.Effect=DragDropEffects.Copy;}catch(Exception ex){e.Effect=DragDropEffects.None;Message("Could not read the dropped files. "+ex.Message,true);}}
        void BeginTransfer(string[] paths){
            if(paths.Length==0)return;try{var data=Shelf.Transfer(paths);outgoing=true;var effect=DoDragDrop(data,DragDropEffects.Copy);Message(effect==DragDropEffects.Copy?"Drop accepted. Tray items remain available.":"Drag cancelled or destination did not accept the files.");}catch(Exception ex){Message(ex.Message,true);}finally{outgoing=false;drop.Active=false;drop.Invalidate();list.Invalidate();}}
        void CopyFiles(){try{Clipboard.SetDataObject(Shelf.Transfer(SelectionOrAll()),true,3,100);Message("File references copied. Paste with Ctrl+V in File Explorer.");}catch(Exception ex){Message("Could not copy. "+ex.Message,true);}}
        void PasteFiles(){try{if(!Clipboard.ContainsFileDropList()){Message("Copy files in File Explorer first, then use Paste files.");return;}AddPaths(Clipboard.GetFileDropList().Cast<string>());}catch(Exception ex){Message("Could not read clipboard. "+ex.Message,true);}}
        void RemoveSelected(){shelf.Remove(Selected());RefreshList();Message("Removed from tray only. Originals are unchanged.");}
        void Reveal(){var paths=Selected();if(paths.Length!=1){Message("Select one item to show its location.");return;}string p=paths[0];if(!Shelf.Exists(p)){Message("That item is no longer available at its original path.",true);return;}try{Process.Start("explorer.exe","/select,\""+p+"\"");}catch(Exception ex){Message(ex.Message,true);}}
        void KeysPressed(object sender,KeyEventArgs e){
            if(e.Control&&e.KeyCode==Keys.A){foreach(ListViewItem item in list.Items)item.Selected=true;e.SuppressKeyPress=true;}
            else if(e.Control&&e.KeyCode==Keys.C){CopyFiles();e.SuppressKeyPress=true;}
            else if(e.Control&&e.KeyCode==Keys.V){PasteFiles();e.SuppressKeyPress=true;}
            else if(e.Control&&e.KeyCode==Keys.Z){shelf.Undo();RefreshList();e.SuppressKeyPress=true;}
            else if(e.KeyCode==Keys.Delete){RemoveSelected();e.SuppressKeyPress=true;}
            else if(e.KeyCode==Keys.Escape){HideShelf();e.SuppressKeyPress=true;}
        }
        void Message(string text,bool error=false){status.Text=text;status.ForeColor=error?Color.FromArgb(240,177,151):Theme.Dim;tips.SetToolTip(status,text);}
        internal void ShowShelf(){Show();if(WindowState==FormWindowState.Minimized)WindowState=FormWindowState.Normal;Activate();}
        void HideShelf(){Hide();}
        protected override void OnShown(EventArgs e){base.OnShown(e);if(!testMode){hotkey=Native.RegisterHotKey(Handle,1,0x4000|0x2|0x1,(uint)Keys.T);if(!hotkey){hint.Text="Use tray icon to reopen";Message("Ctrl+Alt+T is in use. Reopen Traymio from its notification-area icon.");}var area=Screen.FromControl(this).WorkingArea;if(Height>area.Height-20){MinimumSize=new Size(MinimumSize.Width,Math.Min(MinimumSize.Height,area.Height-20));Height=area.Height-20;Top=area.Top+10;}}}
        protected override void WndProc(ref System.Windows.Forms.Message m){if(m.Msg==0x312&&m.WParam.ToInt32()==1){if(Visible&&WindowState!=FormWindowState.Minimized)HideShelf();else ShowShelf();return;}base.WndProc(ref m);}
        protected override void Dispose(bool disposing){if(disposing&&!released){released=true;if(hotkey){Native.UnregisterHotKey(Handle,1);hotkey=false;}refresh.Dispose();notification.Visible=false;notification.ContextMenuStrip.Dispose();notification.Dispose();rowImages.Dispose();rowImage.Dispose();tips.Dispose();appIcon.Dispose();}base.Dispose(disposing);}
    }
    static class Program {
        [STAThread]static int Main(string[] args){Native.SetProcessDPIAware();if(args.Length==2&&args[0]=="--self-test"){Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);Application.ThreadException+=delegate(object sender,System.Threading.ThreadExceptionEventArgs e){Directory.CreateDirectory(args[1]);File.WriteAllText(Path.Combine(args[1],"FAILED.txt"),e.Exception.ToString());Environment.Exit(1);};}Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            try{if(args.Length==2&&args[0]=="--self-test"){Tests.Run(args[1]);return 0;}using(var form=new MainForm(false)){if(args.Length>0)form.AddPaths(args);Application.Run(form);}return 0;}
            catch(Exception ex){if(args.Length==2&&args[0]=="--self-test"){Directory.CreateDirectory(args[1]);File.WriteAllText(Path.Combine(args[1],"FAILED.txt"),ex.ToString());}else MessageBox.Show(ex.Message,"Traymio",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
        }
    }
}
