using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
namespace FishTank;
public sealed class PasswordRevision { public string Password; public string ChangedUtc; }
public sealed class VaultAttachment
{
    public string Id=Guid.NewGuid().ToString("N"),Name,Content;
    public void Validate(){if(!Guid.TryParseExact(Id,"N",out _) || string.IsNullOrEmpty(Name) || Name!=Path.GetFileName(Name) || FileSafe.SafeName(Name)!=Name || Content==null || Content.Length>350000 || Convert.FromBase64String(Content).Length>256*1024)throw new InvalidDataException("Attachments require an ordinary filename and at most 256 KiB each.");}
}
public static class VaultTransfer
{
    public static List<PasswordEntry> Parse(string text,bool csv)
    {
        if(text==null || Encoding.UTF8.GetByteCount(text)>16*1024*1024)throw new InvalidDataException("Import exceeds 16 MiB.");
        List<PasswordEntry> entries;
        if(!csv){var payload=Core.Parse<PasswordPayload>(text); if(payload==null || payload.Entries==null || payload.Version<1 || payload.Version>3)throw new InvalidDataException("Unsupported vault import.");entries=payload.Entries;}
        else
        {
            var rows=Csv(text); if(rows.Count==0)throw new InvalidDataException("CSV is empty."); var headers=rows[0];
            int title=Array.IndexOf(headers,"Title"),user=Array.IndexOf(headers,"Username"),pass=Array.IndexOf(headers,"Password"),site=Array.IndexOf(headers,"Website"),notes=Array.IndexOf(headers,"Notes");
            if(title<0 || pass<0 || headers.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=headers.Length)throw new InvalidDataException("CSV needs unique Title and Password headers; optional Username, Website and Notes.");
            entries=new List<PasswordEntry>();foreach(var row in rows.Skip(1)){if(row.Length!=headers.Length)throw new InvalidDataException("CSV column counts differ.");entries.Add(new PasswordEntry{Title=row[title],Username=user<0?"":row[user],Password=row[pass],Website=site<0?"":row[site],Notes=notes<0?"":row[notes]});}
        }
        if(entries.Count>2000)throw new InvalidDataException("At most 2,000 imported entries are supported.");
        foreach(var e in entries){if(e==null)throw new InvalidDataException("Import contains a null entry.");e.Validate();} if(entries.Select(x=>x.Id).Distinct().Count()!=entries.Count)throw new InvalidDataException("Import contains duplicate IDs.");
        foreach(var e in entries.Where(x=>x.LinkedEmailId!=""))if(!entries.Any(x=>x.Id==e.LinkedEmailId && x.Kind=="Email"))throw new InvalidDataException("An imported email connection is missing.");
        return entries;
    }
    public static List<string[]> Csv(string text)
    {
        var rows=new List<string[]>();var row=new List<string>();var field=new StringBuilder();bool quoted=false,closed=false;
        for(int i=0;i<text.Length;i++)
        {
            char c=text[i];
            if(quoted){if(c=='"'){if(i+1<text.Length && text[i+1]=='"'){field.Append('"');i++;}else{quoted=false;closed=true;}}else field.Append(c);if(field.Length>100000)throw new InvalidDataException("CSV field exceeds its size limit.");continue;}
            if(c=='"'){if(field.Length!=0 || closed)throw new InvalidDataException("Malformed CSV quote.");quoted=true;}
            else if(c==',' || c=='\n' || c=='\r')
            {
                row.Add(field.ToString());field.Clear();closed=false;
                if(c!=','){if(c=='\r' && i+1<text.Length && text[i+1]=='\n')i++;if(row.Any(x=>x!=""))rows.Add(row.ToArray());row.Clear();}
            }
            else{if(closed)throw new InvalidDataException("Unexpected CSV text after quote.");field.Append(c);}
            if(rows.Count>2001 || field.Length>100000)throw new InvalidDataException("CSV size limit exceeded.");
        }
        if(quoted)throw new InvalidDataException("Unclosed CSV quote.");if(field.Length>0 || row.Count>0 || closed){row.Add(field.ToString());if(row.Any(x=>x!=""))rows.Add(row.ToArray());}return rows;
    }
}
