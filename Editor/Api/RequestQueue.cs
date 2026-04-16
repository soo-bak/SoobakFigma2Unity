using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SoobakFigma2Unity.Editor.Api
{
    /// <summary>
    /// Rate-limit aware request queue. Ensures requests are spaced
    /// and retries on 429 with exponential backoff.
    /// </summary>
    internal sealed class RequestQueue
    {
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private DateTime _lastRequestTime = DateTime.MinValue;
        private readonly int _minIntervalMs;
        private readonly int _maxRetries;

        public RequestQueue(int minIntervalMs = 200, int maxRetries = 5)
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
                        return await requestFunc();
                    }
                    catch (FigmaRateLimitException) when (attempt < _maxRetries)
                    {
                        var delay = (int)(Math.Pow(2, attempt) * 1000);
                        await Task.Delay(delay, ct);
                    }
                }

                throw new FigmaApiException("Max retries exceeded due to rate limiting.");
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public void Dispose()
        {
            _semaphore?.Dispose();
        }
    }
}
