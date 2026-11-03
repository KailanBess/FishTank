using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
namespace FishTank;
internal sealed class LiveDetectionSession : IDisposable
{
    internal const string Channel = WindowsLiveEventSource.Channel;
    private readonly LiveSessionEngine engine;
    internal LiveDetectionSession(Action<LiveDetectionEvent> notify) { engine = new LiveSessionEngine(notify); }
    internal LiveDetectionSession(Action<LiveDetectionEvent> notify, ILiveEventSource source, Func<LiveProtectionReading> read, Func<DateTime> clock, ILiveCheckpointStore store) { engine = new LiveSessionEngine(notify, source, read, clock, store, false); }
    internal bool Enabled => engine.Enabled;
    internal string Status => engine.Status;
    internal void Start() => engine.Start();
    internal void Stop() => engine.Stop();
    internal Task CheckNowAsync() => engine.CycleAsync(true);
    internal List<LiveDetectionEvent> Events() => engine.Events();
    internal string Report() => engine.Report();
    public void Dispose() => engine.Dispose();
}
internal static class LiveHistoryExtensions
{
    internal static IEnumerable<T> TakeLastCompatible<T>(this IEnumerable<T> values, int count) { var array = values.ToArray(); return array.Skip(Math.Max(0, array.Length - count)); }
}
