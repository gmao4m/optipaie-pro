using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OptiPaie.Common.Logging;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Updates;

namespace OptiPaie.Services.Updates
{
    /// <summary>
    /// Reads the per-version update metadata (mandatory flag + release notes) from the
    /// Supabase `updates` table via its public REST endpoint (RLS allows anon reads of
    /// update metadata). Returns defaults on any error so an update is never wrongly
    /// treated as mandatory.
    /// </summary>
    public sealed class SupabaseUpdateMetadataSource : IUpdateMetadataSource
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        private readonly UpdateOptions _options;
        private readonly ILogger _logger;

        public SupabaseUpdateMetadataSource(UpdateOptions options, ILogger logger)
        {
            _options = Guard.AgainstNull(options, nameof(options));
            _logger = Guard.AgainstNull(logger, nameof(logger));
        }

        public async Task<UpdateMeta> GetForVersionAsync(string version, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_options.MetadataUrl) ||
                string.IsNullOrWhiteSpace(_options.AnonKey) ||
                string.IsNullOrWhiteSpace(version))
            {
                return UpdateMeta.Default;
            }

            string url = _options.MetadataUrl.TrimEnd('/') +
                "?version=eq." + Uri.EscapeDataString(version) +
                "&select=mandatory,release_notes&limit=1";

            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.TryAddWithoutValidation("apikey", _options.AnonKey);
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _options.AnonKey);

                using (HttpResponseMessage response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.Warn("Update metadata HTTP " + (int)response.StatusCode);
                        return UpdateMeta.Default;
                    }

                    Row[] rows = JsonConvert.DeserializeObject<Row[]>(json);
                    if (rows == null || rows.Length == 0)
                    {
                        return UpdateMeta.Default;
                    }

                    return new UpdateMeta
                    {
                        Mandatory = rows[0].mandatory,
                        ReleaseNotes = rows[0].release_notes ?? string.Empty
                    };
                }
            }
        }

        private sealed class Row
        {
            public bool mandatory { get; set; }
            public string release_notes { get; set; }
        }
    }
}
