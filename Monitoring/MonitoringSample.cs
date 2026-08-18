using System;
using System.Collections.Generic;

namespace CoolShift.Monitoring;

internal readonly record struct MonitoringSample(DateTimeOffset Timestamp, IReadOnlyList<SensorSample> Samples);
