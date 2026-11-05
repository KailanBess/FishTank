using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FishTank;
[assembly: System.Reflection.AssemblyMetadata("AquariumWorkspace", "1")]
class Entry {
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetProp(IntPtr window,string name);
 [DllImport("user32.dll")]static extern IntPtr GetParent(IntPtr window);
 [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr window,int show);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern uint RegisterWindowMessage(string name);
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
 [STAThread] static int Main(string[] args) {
  string temp=Path.Combine(Path.GetTempPath(),"FT-layout-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);Core.Data=temp;Core.Init();
  try { AquariumWorkspace.Prepare(); if(args.Length==1 && args[0].StartsWith("--workspace-fixture=")) return RunWorkspace(args[0].Substring(20)); Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);new SetupProgress{Completed=true}.Save();
   string previous=Environment.GetEnvironmentVariable("AQUARIUM_HOSTED");Environment.SetEnvironmentVariable("AQUARIUM_HOSTED","1");
   using(var fixture=new Form()) { AquariumWorkspace.Register(fixture);var handle=fixture.Handle;if(GetProp(handle,"Aquarium.Workspace.v1")!=IntPtr.Zero)throw new Exception("Workspace marked before initial Show");fixture.Show();Application.DoEvents();if(GetProp(fixture.Handle,"Aquarium.Workspace.v1")!=new IntPtr(1))throw new Exception("Workspace not marked after Shown/queued initialization");fixture.Close(); }
   using(var parent=new Form())using(var gated=new Form()) {
    parent.StartPosition=FormStartPosition.Manual;parent.Location=new Point(-20000,-20000);parent.Show();bool allowed=false;AquariumWorkspace.Register(gated,()=>allowed);gated.TopLevel=false;parent.Controls.Add(gated);gated.Show();Application.DoEvents();
    ShowWindow(gated.Handle,0);
    uint visibility=RegisterWindowMessage("Aquarium.Workspace.Visibility.v1");
    SendMessage(gated.Handle,visibility,new IntPtr(1),IntPtr.Zero);Application.DoEvents();
    if(gated.Visible)throw new Exception("Host visibility command bypassed app visibility guard");
    allowed=true;SendMessage(gated.Handle,visibility,new IntPtr(1),IntPtr.Zero);Application.DoEvents();
    if(!gated.Visible)throw new Exception("Allowed cooperative show failed");
    SendMessage(gated.Handle,visibility,IntPtr.Zero,IntPtr.Zero);Application.DoEvents();
    if(gated.Visible)throw new Exception("Cooperative hide failed");gated.Close();
   }
   Console.WriteLine("PASS: cooperative visibility respects app show guard and restores managed state");
   Environment.SetEnvironmentVariable("AQUARIUM_HOSTED",previous);Console.WriteLine("PASS: workspace readiness waits until WinForms initial Show completes");
   using(var window=new Desktop()) {window.Size=new Size(1200,800);window.RenderPreview(args[0],"Dashboard",new FishTank.Appearance {FontFamily="Segoe UI"}.WithReferenceStyle());}Console.WriteLine("PASS: isolated dashboard rendered without starting a scan");return 0;
  } finally { Directory.Delete(temp,true); }
 }
 static int RunWorkspace(string report) {
  AquariumWorkspace.Prepare(); Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  new SetupProgress{Completed=true}.Save();AppearanceStore.Save(new FishTank.Appearance().WithReferenceStyle());
  WindowFrame.SetAppearance(AppearanceStore.Load());
  using(var window=new Desktop())using(var timer=new Timer{Interval=200}) {
   var deadline=DateTime.UtcNow.AddSeconds(20);int frames=0;
   timer.Tick+=delegate {if(GetParent(window.Handle)!=IntPtr.Zero && ++frames>=1) {
    string details="Visible="+window.Visible+";Client="+window.ClientSize+";Children="+window.Controls.Count;
    foreach(Control control in window.Controls)details+="\n"+control.GetType().Name+" Visible="+control.Visible+" Bounds="+control.Bounds;
    File.WriteAllText(report+".tmp",details);if(File.Exists(report))File.Replace(report+".tmp",report,null);else File.Move(report+".tmp",report);using(var bitmap=new Bitmap(window.Width,window.Height)){window.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(report+".png");}
   }else if(DateTime.UtcNow>deadline){File.WriteAllText(report,"Timed out awaiting native parent");timer.Stop();window.Close();}};
   timer.Start();Application.Run(window);return 0;
  }
 }}


