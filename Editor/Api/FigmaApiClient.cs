using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SoobakFigma2Unity.Editor.Models;

namespace SoobakFigma2Unity.Editor.Api
{
    public class FigmaApiException : Exception
    {
        public FigmaApiException(string message) : base(message) { }
        public FigmaApiException(string message, Exception inner) : base(message, inner) { }
    }

    public class FigmaRateLimitException : FigmaApiException
    {
        public int RetryAfterMs { get; }

        public FigmaRateLimitException(int retryAfterMs = 0)
            : base("Figma API rate limit exceeded (429).")
        {
            RetryAfterMs = retryAfterMs;
        }
    }

    internal sealed class FigmaApiClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly RequestQueue _queue;
        private const int ImageBatchSize = 80;

        public FigmaApiClient(string personalAccessToken)
        {
            _http = new HttpClient();
            _http.DefaultRequestHeaders.Add("X-FIGMA-TOKEN", personalAccessToken);
            _http.Timeout = TimeSpan.FromSeconds(120);
            _queue = new RequestQueue();
            _queue.OnRetry += (attempt, max, delayMs) =>
            {
                UnityEngine.Debug.LogWarning(
                    $"[SoobakFigma2Unity] Rate limited. Retry {attempt}/{max} in {delayMs}ms...");
            };
        }

        /// <summary>
        /// Fetch file structure. Use depth=2 for page/frame listing.
        /// </summary>
        public async Task<FigmaFileResponse> GetFileAsync(string fileKey, int? depth = null, CancellationToken ct = default)
        {
            var url = FigmaEndpoints.GetFile(fileKey, depth);
            var json = await GetJsonAsync(url, ct);
            return JsonConvert.DeserializeObject<FigmaFileResponse>(json);
        }

        /// <summary>
        /// Fetch full subtrees for specific nodes.
        /// </summary>
        public async Task<FigmaNodesResponse> GetFileNodesAsync(string fileKey, IReadOnlyList<string> nodeIds, CancellationToken ct = default)
        {
            var url = FigmaEndpoints.GetFileNodes(fileKey, nodeIds);
            var json = await GetJsonAsync(url, ct);
            return JsonConvert.DeserializeObject<FigmaNodesResponse>(json);
        }

        /// <summary>
        /// Get rendered image URLs for nodes. Returns nodeId -> imageUrl.
        /// Automatically batches large requests.
        /// </summary>
        public async Task<Dictionary<string, string>> GetImageUrlsAsync(
            string fileKey,
            IReadOnlyList<string> nodeIds,
            float scale = 2f,
            string format = "png",
            CancellationToken ct = default)
        {
            var result = new Dictionary<string, string>();

            for (int i = 0; i < nodeIds.Count; i += ImageBatchSize)
            {
                var batch = nodeIds.Skip(i).Take(ImageBatchSize).ToList();
                var url = FigmaEndpoints.GetImages(fileKey, batch, scale, format);
                var json = await GetJsonAsync(url, ct);
                var response = JsonConvert.DeserializeObject<FigmaImageResponse>(json);

                if (!string.IsNullOrEmpty(response.Error))
                    throw new FigmaApiException($"Figma image API error: {response.Error}");

                if (response.Images != null)
                {
                    foreach (var kv in response.Images)
                    {
                        if (!string.IsNullOrEmpty(kv.Value))
                            result[kv.Key] = kv.Value;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Get image fill URLs (for IMAGE type paints). Returns imageRef -> URL.
        /// </summary>
        public async Task<Dictionary<string, string>> GetImageFillsAsync(string fileKey, CancellationToken ct = default)
        {
            var url = FigmaEndpoints.GetImageFills(fileKey);
            var json = await GetJsonAsync(url, ct);
            var response = JsonConvert.DeserializeObject<FigmaImageFillsResponse>(json);
            return response.Meta?.Images ?? new Dictionary<string, string>();
        }

        /// <summary>
        /// Download raw image bytes from a URL.
        /// </summary>
        public async Task<byte[]> DownloadImageAsync(string imageUrl, CancellationToken ct = default)
        {
            return await _queue.EnqueueAsync(async () =>
            {
                var response = await _http.GetAsync(imageUrl, ct);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync();
            }, ct);
        }

        private async Task<string> GetJsonAsync(string url, CancellationToken ct)
        {
            return await _queue.EnqueueAsync(async () =>
            {
                var response = await _http.GetAsync(url, ct);

                if ((int)response.StatusCode == 429)
                {
                    int retryAfterMs = 0;
                    if (response.Headers.RetryAfter?.Delta != null)
                        retryAfterMs = (int)response.Headers.RetryAfter.Delta.Value.TotalMilliseconds;
                    else if (response.Headers.RetryAfter?.Date != null)
                        retryAfterMs = (int)(response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow).TotalMilliseconds;
                    throw new FigmaRateLimitException(Math.Max(retryAfterMs, 0));
                }

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    throw new FigmaApiException($"Figma API error {response.StatusCode}: {body}");
                }

                return await response.Content.ReadAsStringAsync();
            }, ct);
        }

        public void Dispose()
        {
            _http?.Dispose();
            _queue?.Dispose();
        }
    }
}
