using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SoobakFigma2Unity.Editor.Api;
using SoobakFigma2Unity.Editor.Util;

namespace SoobakFigma2Unity.Editor.Assets
{
    /// <summary>
    /// Downloads rendered node images from Figma and saves them to disk.
    /// </summary>
    internal sealed class ImageDownloader
    {
        private readonly FigmaApiClient _api;
        private readonly ImportLogger _logger;

        public ImageDownloader(FigmaApiClient api, ImportLogger logger)
        {
            _api = api;
            _logger = logger;
        }

        /// <summary>
        /// Download images for the given node IDs and save to the output directory.
        /// Returns a mapping of nodeId -> local file path.
        /// </summary>
        public async Task<Dictionary<string, string>> DownloadNodeImagesAsync(
            string fileKey,
            IReadOnlyList<string> nodeIds,
            string outputDir,
            float scale = 2f,
            CancellationToken ct = default)
        {
            var result = new Dictionary<string, string>();

            if (nodeIds.Count == 0)
                return result;

            Directory.CreateDirectory(outputDir);

            _logger.Info($"Requesting image URLs for {nodeIds.Count} nodes...");
            var imageUrls = await _api.GetImageUrlsAsync(fileKey, nodeIds, scale, "png", ct);

            int downloaded = 0;
            foreach (var kv in imageUrls)
            {
                ct.ThrowIfCancellationRequested();

                var nodeId = kv.Key;
                var url = kv.Value;

                if (string.IsNullOrEmpty(url))
                {
                    _logger.Warn($"No image URL for node {nodeId}");
                    continue;
                }

                var safeName = SanitizeFileName(nodeId);
                var filePath = Path.Combine(outputDir, $"{safeName}.png");

                try
                {
                    var bytes = await _api.DownloadImageAsync(url, ct);
                    File.WriteAllBytes(filePath, bytes);
                    result[nodeId] = filePath;
                    downloaded++;
                }
                catch (System.Exception e)
                {
                    _logger.Error($"Failed to download image for {nodeId}: {e.Message}");
                }
            }

            _logger.Success($"Downloaded {downloaded}/{nodeIds.Count} images.");
            return result;
        }

        /// <summary>
        /// Download image fill references and save to disk.
        /// Returns imageRef -> local file path.
        /// </summary>
        public async Task<Dictionary<string, string>> DownloadImageFillsAsync(
            string fileKey,
            IEnumerable<string> imageRefs,
            string outputDir,
            CancellationToken ct = default)
        {
            var result = new Dictionary<string, string>();
            Directory.CreateDirectory(outputDir);

            var fillUrls = await _api.GetImageFillsAsync(fileKey, ct);

            foreach (var imageRef in imageRefs)
            {
                ct.ThrowIfCancellationRequested();

                if (!fillUrls.TryGetValue(imageRef, out var url) || string.IsNullOrEmpty(url))
                {
                    _logger.Warn($"No URL for image fill ref {imageRef}");
                    continue;
                }

                var safeName = SanitizeFileName(imageRef);
                var filePath = Path.Combine(outputDir, $"fill_{safeName}.png");

                try
                {
                    var bytes = await _api.DownloadImageAsync(url, ct);
                    File.WriteAllBytes(filePath, bytes);
                    result[imageRef] = filePath;
                }
                catch (System.Exception e)
                {
                    _logger.Error($"Failed to download fill image {imageRef}: {e.Message}");
                }
            }

            return result;
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Replace(":", "_");
        }
    }
}
