using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FishTank;
class Entry {
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetProp(IntPtr window,string name);
 [STAThread] static int Main(string[] args) {
  string temp=Path.Combine(Path.GetTempPath(),"FT-layout-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);Core.Data=temp;Core.Init();
  try { Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);new SetupProgress{Completed=true}.Save();
   string previous=Environment.GetEnvironmentVariable("AQUARIUM_HOSTED");Environment.SetEnvironmentVariable("AQUARIUM_HOSTED","1");
   using(var fixture=new Form()) { AquariumWorkspace.Register(fixture);var handle=fixture.Handle;if(GetProp(handle,"Aquarium.Workspace.v1")!=IntPtr.Zero)throw new Exception("Workspace marked before initial Show");fixture.Show();Application.DoEvents();if(GetProp(fixture.Handle,"Aquarium.Workspace.v1")!=new IntPtr(1))throw new Exception("Workspace not marked after Shown/queued initialization");fixture.Close(); }
   Environment.SetEnvironmentVariable("AQUARIUM_HOSTED",previous);Console.WriteLine("PASS: workspace readiness waits until WinForms initial Show completes");
   using(var window=new Desktop()) {window.Size=new Size(1200,800);window.RenderPreview(args[0],"Dashboard",new FishTank.Appearance {FontFamily="Segoe UI"}.WithReferenceStyle());}Console.WriteLine("PASS: isolated dashboard rendered without starting a scan");return 0;
  } finally { Directory.Delete(temp,true); }
 }
}


