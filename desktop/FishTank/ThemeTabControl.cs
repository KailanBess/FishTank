using System.Drawing;
using System.Windows.Forms;

namespace FishTank;

// Owner-drawn tab labels alone leave the native unused strip and frame white.
internal sealed class ThemeTabControl : TabControl
{
    internal Color FrameBackground = SystemColors.Control;
    internal Color BorderColor = SystemColors.ControlDark;
    protected override void WndProc(ref Message message)
    {
        int id = message.Msg;
        base.WndProc(ref message);
        if (id != 0xF && id != 0x318 && id != 0x317) return;
        if (!IsHandleCreated || IsDisposed || ClientSize.Width < 1 || ClientSize.Height < 1) return;
        using (var graphics = id == 0xF || message.WParam == System.IntPtr.Zero ? CreateGraphics() : Graphics.FromHdc(message.WParam))
        using (var region = new Region(ClientRectangle))
        using (var fill = new SolidBrush(FrameBackground))
        using (var line = new Pen(BorderColor)) {
            region.Exclude(DisplayRectangle);
            for (int i = 0; i < TabCount; i++) region.Exclude(GetTabRect(i));
            graphics.FillRegion(fill, region);
            var frame = DisplayRectangle; frame.Inflate(1, 1); frame.Width--; frame.Height--;
            if (frame.Width > 0 && frame.Height > 0) graphics.DrawRectangle(line, frame);
        }
    }
}
