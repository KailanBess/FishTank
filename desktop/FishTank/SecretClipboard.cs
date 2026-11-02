using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FishTank;

public sealed class SecretClipboard : IDisposable
{
	private readonly Timer timer = new Timer
	{
		Interval = 30000
	};

	private uint sequence;

	private bool owns;

	[DllImport("user32.dll")]
	private static extern uint GetClipboardSequenceNumber();

	public SecretClipboard()
	{
		Timer obj = timer;
		EventHandler value = delegate
		{
			Clear();
		};
		obj.Tick += value;
	}

	public void Copy(string password)
	{
		DataObject dataObject = new DataObject();
		dataObject.SetText(password, TextDataFormat.UnicodeText);
		dataObject.SetData("CanIncludeInClipboardHistory", autoConvert: false, new MemoryStream(BitConverter.GetBytes(0)));
		dataObject.SetData("CanUploadToCloudClipboard", autoConvert: false, new MemoryStream(BitConverter.GetBytes(0)));
		byte[] buffer = new byte[4];
		dataObject.SetData("ExcludeClipboardContentFromMonitorProcessing", autoConvert: false, new MemoryStream(buffer));
		Clipboard.SetDataObject(dataObject, copy: true);
		sequence = GetClipboardSequenceNumber();
		owns = true;
		timer.Stop();
		timer.Start();
	}

	public void Clear()
	{
		timer.Stop();
		if (owns && GetClipboardSequenceNumber() == sequence)
		{
			try
			{
				Clipboard.Clear();
			}
			catch
			{
			}
		}
		owns = false;
	}

	public void Dispose()
	{
		Clear();
		timer.Dispose();
	}
}
