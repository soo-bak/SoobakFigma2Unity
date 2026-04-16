using System;
using System.Threading;
using System.Threading.Tasks;

namespace SoobakFigma2Unity.Editor.Api
{
    internal sealed class RequestQueue : IDisposable
    {
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private DateTime _lastRequestTime = DateTime.MinValue;
        private int _minIntervalMs;
        private readonly int _maxRetries;
        private int _consecutiveRateLimits;

        public RequestQueue(int minIntervalMs = 200, int maxRetries = 8)
        {
            _minIntervalMs = minIntervalMs;
            _maxRetries = maxRetries;
        }

        public async Task<T> EnqueueAsync<T>(Func<Task<T>> requestFunc, CancellationToken ct = default)
        {
            await _semaphore.WaitAsync(ct);
            try
            {
                var elapsed = (DateTime.UtcNow - _lastRequestTime).TotalMilliseconds;
                if (elapsed < _minIntervalMs)
                    await Task.Delay((int)(_minIntervalMs - elapsed), ct);

                for (int attempt = 0; attempt <= _maxRetries; attempt++)
                {
                    try
                    {
                        _lastRequestTime = DateTime.UtcNow;
                        var result = await requestFunc();
                        if (_consecutiveRateLimits > 0)
                        {
                            _consecutiveRateLimits = Math.Max(0, _consecutiveRateLimits - 1);
                            if (_consecutiveRateLimits == 0)
                                _minIntervalMs = Math.Max(200, _minIntervalMs / 2);
                        }
                        return result;
                    }
                    catch (FigmaRateLimitException ex) when (attempt < _maxRetries)
                    {
                        _consecutiveRateLimits++;
                        int delay = ex.RetryAfterMs > 0
                            ? ex.RetryAfterMs
                            : (int)(Math.Pow(2, attempt) * 1000);
                        delay = Math.Min(delay, 60000);
                        if (_consecutiveRateLimits >= 2)
                            _minIntervalMs = Math.Min(_minIntervalMs * 2, 5000);

                        UnityEngine.Debug.LogWarning(
                            $"[SoobakFigma2Unity] Rate limited. Retry {attempt + 1}/{_maxRetries} in {delay}ms...");
                        await Task.Delay(delay, ct);
                    }
                }
                throw new FigmaApiException($"Max retries ({_maxRetries}) exceeded due to rate limiting.");
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public void Dispose() => _semaphore?.Dispose();
    }
}
