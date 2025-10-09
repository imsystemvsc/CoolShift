using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ParkToggleWpf.Monitoring;

internal sealed class MonitoringManager : IAsyncDisposable, IDisposable
{
    private readonly HardwareMonitorService _hardware;
    private readonly MonitoringRepository _repository;
    private readonly MonitoringOptions _options;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;
    private readonly TimeSpan _purgeInterval = TimeSpan.FromMinutes(30);

    public event EventHandler<MonitoringSample>? SampleCaptured;

    public MonitoringManager(HardwareMonitorService hardware, MonitoringRepository repository, MonitoringOptions options)
    {
        _hardware = hardware;
        _repository = repository;
        _options = options;
    }

    public void Start()
    {
        if (_loopTask is not null)
        {
            return;
        }

        _loopTask = Task.Run(() => RunAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        if (_loopTask is null)
        {
            return;
        }

        _cts.Cancel();

        try
        {
            await _loopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Monitoring loop terminated unexpectedly: {ex}");
        }
        finally
        {
            _loopTask = null;
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var lastPurge = DateTimeOffset.UtcNow;

        while (!cancellationToken.IsCancellationRequested)
        {
            var timestamp = DateTimeOffset.UtcNow;
            var samples = Array.Empty<SensorSample>();

            try
            {
                var collected = _hardware.GetSamples();
                samples = collected.Count > 0 ? collected.ToArray() : Array.Empty<SensorSample>();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Failed to collect hardware samples: {ex}");
            }

            var snapshot = new MonitoringSample(timestamp, samples);

            try
            {
                await _repository.SaveSampleAsync(snapshot, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Failed to persist monitoring sample: {ex}");
            }

            RaiseSampleCaptured(snapshot);

            if (timestamp - lastPurge >= _purgeInterval)
            {
                try
                {
                    var cutoff = timestamp - _options.RetentionPeriod;
                    await _repository.PurgeOlderThanAsync(cutoff, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Failed to purge monitoring samples: {ex}");
                }

                lastPurge = timestamp;
            }

            try
            {
                await Task.Delay(_options.SampleInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void RaiseSampleCaptured(MonitoringSample sample)
    {
        if (sample.Samples.Count == 0)
        {
            return;
        }

        try
        {
            SampleCaptured?.Invoke(this, sample);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Monitoring sample handler threw: {ex}");
        }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cts.Cancel();
        _cts.Dispose();
        _hardware.Dispose();
    }
}
