using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace FishTank;
public sealed class AuthenticationEvent { public DateTime RecordedUtc; public bool Approved; public string Context; }
internal static class AuthenticationHistory
{
    private static readonly object Gate=new object();
    internal static List<AuthenticationEvent> Read()=>ProtectedStore.Read("authentication-history",()=>new List<AuthenticationEvent>())??throw new InvalidDataException("Authentication history unreadable.");
    internal static void Record(bool approved,string context){lock(Gate){var rows=Read();rows.Add(new AuthenticationEvent{Approved=approved,Context=context,RecordedUtc=DateTime.UtcNow});ProtectedStore.Write("authentication-history",rows.Skip(Math.Max(0,rows.Count-100)).ToList());}}
}
