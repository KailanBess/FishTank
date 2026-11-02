using System;
using System.Collections.Generic;

namespace FishTank;

public delegate BreachResponse BreachTransport(Uri uri, Dictionary<string, string> headers);
