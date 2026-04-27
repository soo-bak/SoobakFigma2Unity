using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SoobakFigma2Unity.Editor.Api;
using SoobakFigma2Unity.Editor.Util;
using UnityEditor;

namespace SoobakFigma2Unity.Editor.Assets
{
    internal sealed class ImageDownloader
    {
        private readonly FigmaApiClient _api;
        private readonly ImportLogger _logger;
        private const int MaxConcurrentDownloads = 6;

        public ImageDownloader(FigmaApiClient api, ImportLogger logger)
        {
            _api = api;
            _logger = logger;
        }

        public async Task<Dictionary<string, string>> DownloadNodeImagesAsync(
            string fileKey, IReadOnlyList<string> nodeIds, string outputDir,
            float scale = 2f, bool useAbsoluteBounds = true, CancellationToken ct = default)
        {
            var result = new ConcurrentDictionary<string, string>();
            if (nodeIds.Count == 0) return new Dictionary<string, string>(result);

            Directory.CreateDirectory(outputDir);

            _logger.Info($"Requesting image URLs for {nodeIds.Count} nodes (use_absolute_bounds={useAbsoluteBounds})...");
            var imageUrls = await _api.GetImageUrlsAsync(fileKey, nodeIds, scale, "png", useAbsoluteBounds, ct);

            var validUrls = imageUrls.Where(kv => !string.IsNullOrEmpty(kv.Value)).ToList();
            int total = validUrls.Count;
            if (total == 0) { _logger.Warn("No valid image URLs returned."); return new Dictionary<string, string>(result); }

            _logger.Info($"Downloading {total} images ({MaxConcurrentDownloads} parallel)...");
            int completed = 0;

            var semaphore = new SemaphoreSlim(MaxConcurrentDownloads);
            var tasks = validUrls.Select(async kv =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var safeName = SanitizeFileName(kv.Key);
                    var filePath = Path.Combine(outputDir, $"{safeName}.png");
                    try
                    {
                        var bytes = await _api.DownloadImageAsync(kv.Value, ct);
                        File.WriteAllBytes(filePath, bytes);
                        result[kv.Key] = filePath;
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        _logger.Error($"Failed to download image for {kv.Key}: {e.Message}");
                    }
                    var count = Interlocked.Increment(ref completed);
                    EditorUtility.DisplayProgressBar("SoobakFigma2Unity Import",
                        $"Downloading image {count}/{total}...", (float)count / total);
                }
                finally { semaphore.Release(); }
            });

            await Task.WhenAll(tasks);
            semaphore.Dispose();

            _logger.Success($"Downloaded {result.Count}/{total} images.");
            return new Dictionary<string, string>(result);
        }

        public async Task<Dictionary<string, string>> DownloadImageFillsAsync(
            string fileKey, IEnumerable<string> imageRefs, string outputDir,
            CancellationToken ct = default)
        {
            var result = new ConcurrentDictionary<string, string>();
            Directory.CreateDirectory(outputDir);

            var fillUrls = await _api.GetImageFillsAsync(fileKey, ct);
            var refList = imageRefs.ToList();

            var semaphore = new SemaphoreSlim(MaxConcurrentDownloads);
            var tasks = refList.Select(async imageRef =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    if (!fillUrls.TryGetValue(imageRef, out var url) || string.IsNullOrEmpty(url)) return;
                    var safeName = SanitizeFileName(imageRef);
                    var filePath = Path.Combine(outputDir, $"fill_{safeName}.png");
                    try
                    {
                        var bytes = await _api.DownloadImageAsync(url, ct);
                        File.WriteAllBytes(filePath, bytes);
                        result[imageRef] = filePath;
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        _logger.Error($"Failed to download fill image {imageRef}: {e.Message}");
                    }
                }
                finally { semaphore.Release(); }
            });

            await Task.WhenAll(tasks);
            semaphore.Dispose();
            return new Dictionary<string, string>(result);
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Replace(":", "_");
        }
    }
}
