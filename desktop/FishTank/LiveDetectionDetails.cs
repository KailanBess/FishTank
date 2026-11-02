using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace FishTank;

public static class LiveDetectionDetails
{
    public static string Read(string xml)
    {
        if (xml == null || xml.Length > 65536) return "Event details omitted because they were absent or exceeded the size limit.";
        var allowed = new HashSet<string> { "threatname", "threatid", "severityname", "categoryname", "actionname", "errorcode", "errordescription", "detectionid", "path", "processname" };
        var result = new StringBuilder();
        try
        {
            using (var source = new StringReader(xml))
            using (var reader = XmlReader.Create(source, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65536 }))
            {
                var document = new XmlDocument { XmlResolver = null }; document.Load(reader);
                foreach (XmlNode node in document.SelectNodes("//*[local-name()='EventData']/*[local-name()='Data']"))
                {
                    string name = node.Attributes?["Name"]?.Value ?? "";
                    string key = name.Replace(" ", "").Replace("_", "").ToLowerInvariant();
                    if (!allowed.Contains(key)) continue;
                    string text = node.InnerText;
                    if (text.Length > 1024) text = text.Substring(0, 1024) + " [truncated]";
                    if (name.Length > 64) name = name.Substring(0, 64);
                    if (result.Length + name.Length + text.Length > 4096) { result.AppendLine("Remaining selected details omitted at the size limit."); break; }
                    result.AppendLine(name + ": " + text);
                }
            }
            return result.Length == 0 ? "No supported detail fields were present; review the original event in Windows Security/Event Viewer." : result.ToString();
        }
        catch (XmlException) { return "Event detail XML could not be safely parsed. The reported event classification is retained; no file contents were read."; }
    }
}
