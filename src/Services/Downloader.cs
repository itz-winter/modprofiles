using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ModProfileSwitcher.Services
{
    /// <summary>
    /// Downloads a file from a URL with progress reporting and temp-file safety.
    /// </summary>
    public static class Downloader
    {
        private static readonly HttpClient Http = new HttpClient();

        static Downloader()
        {
            Http.DefaultRequestHeaders.Add("User-Agent", "ModProfileSwitcher/1.0 (github.com)");
        }

        /// <summary>
        /// Downloads <paramref name="url"/> to <paramref name="destinationPath"/>.
        /// Reports 0.0 → 1.0 progress. Uses a temp file and renames on success.
        /// </summary>
        public static async Task DownloadFileAsync(
            string url,
            string destinationPath,
            IProgress<double> progress = null,
            CancellationToken ct = default)
        {
            var tempPath = destinationPath + ".tmp";
            try
            {
                using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                var contentLength = response.Content.Headers.ContentLength;
                using var input = await response.Content.ReadAsStreamAsync();
                using var output = File.Create(tempPath);

                var buffer = new byte[81920];
                long totalRead = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    await output.WriteAsync(buffer, 0, read, ct);
                    totalRead += read;
                    if (contentLength.HasValue && progress != null)
                        progress.Report((double)totalRead / contentLength.Value);
                }

                output.Close();

                // Rename temp → final
                if (File.Exists(destinationPath))
                    File.Delete(destinationPath);
                File.Move(tempPath, destinationPath);
            }
            catch
            {
                // Clean up temp on failure
                if (File.Exists(tempPath))
                    try { File.Delete(tempPath); } catch { }
                throw;
            }
        }
    }
}
