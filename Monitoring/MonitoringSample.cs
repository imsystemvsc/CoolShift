using System;
using System.Collections.Generic;

namespace ParkToggleWpf.Monitoring;

internal readonly record struct MonitoringSample(DateTimeOffset Timestamp, IReadOnlyList<SensorSample> Samples);
