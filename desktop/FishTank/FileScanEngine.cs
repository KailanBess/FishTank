using System.Threading;

namespace FishTank;

public delegate CommandResult FileScanEngine(string path, CancellationToken cancellation);
