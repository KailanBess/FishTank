using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
[assembly: System.Reflection.AssemblyMetadata("AquariumWorkspace", "1")]
namespace FishTank;
internal static class AquariumWorkspace
{
 internal static bool Enabled { get { return Environment.GetEnvironmentVariable("AQUARIUM_HOSTED") == "1"; } }
 [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool SetProp(IntPtr window,string name,IntPtr value);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
 [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr window);
 [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window,int show);
 internal static void Prepare(){if(Enabled)SetProcessDpiAwarenessContext(new IntPtr(-4));}
 internal static void Register(Form form,Func<bool> canShow = null)
 {
  if(!Enabled)return;
  var bridge=new VisibilityBridge(form,canShow);
  bool ready=false;
  Action mark=delegate {if(!form.IsDisposed&&form.IsHandleCreated){SetProp(form.Handle,"Aquarium.Workspace.ManagedVisibility.v1",new IntPtr(1));SetProp(form.Handle,"Aquarium.Workspace.v1",new IntPtr(1));}};
  form.HandleCreated+=delegate {bridge.Bind();if(ready)form.BeginInvoke(mark);};
  form.HandleDestroyed+=delegate {bridge.ReleaseHandle();};
  form.Shown+=delegate {form.MinimumSize=new System.Drawing.Size(640,400);form.WindowState=FormWindowState.Normal;if(GetParent(form.Handle)==IntPtr.Zero)ShowWindow(form.Handle,0);form.BeginInvoke((Action)delegate{ready=true;mark();});};
  if(form.IsHandleCreated)bridge.Bind();
 }
 sealed class VisibilityBridge:NativeWindow
 {
  readonly Form form;readonly Func<bool> canShow;
  static readonly uint message=RegisterWindowMessage("Aquarium.Workspace.Visibility.v1");
  internal VisibilityBridge(Form form,Func<bool> canShow){this.form=form;this.canShow=canShow;}
  internal void Bind(){AssignHandle(form.Handle);}
  protected override void WndProc(ref Message m)
  {
   if((uint)m.Msg==message){
    if(!form.IsDisposed&&GetParent(form.Handle)!=IntPtr.Zero){
     if(m.WParam==IntPtr.Zero)form.Hide();
     else if(canShow==null||canShow()){form.Show();form.PerformLayout();form.Invalidate(true);form.Update();}
    }
    m.Result=new IntPtr(1);return;
   }
   base.WndProc(ref m);
  }
 }
}