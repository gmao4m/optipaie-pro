using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OptiPaie.Common.Logging;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Licensing;

namespace OptiPaie.Services.Licensing
{
    /// <summary>
    /// <see cref="ILicenseBackend"/> implementation for Supabase Edge Functions. This
    /// is the ONLY class that knows the backend is Supabase; it is isolated behind the
    /// provider-agnostic interface, so a future migration replaces just this file.
    /// Sends the public anon key as the API authorisation. Throws on transport failure;
    /// returns an unsuccessful response for a non-2xx HTTP status.
    /// </summary>
    public sealed class SupabaseLicenseBackend : ILicenseBackend
    {
        private static readonly HttpClient Http = CreateClient();

        private readonly LicensingOptions _options;
        private readonly ILogger _logger;

        public SupabaseLicenseBackend(LicensingOptions options, ILogger logger)
        {
            _options = Guard.AgainstNull(options, nameof(options));
            _logger = Guard.AgainstNull(logger, nameof(logger));
        }

        public Task<BackendLicenseResponse> ActivateAsync(ActivationRequest request, CancellationToken cancellationToken)
        {
            Guard.AgainstNull(request, nameof(request));
            var body = new
            {
                productKey = request.ProductKey,
                licenseKey = request.LicenseKey,
                companyName = request.CompanyName,
                email = request.Email,
                deviceId = request.DeviceId,
                appVersion = request.AppVersion
            };
            return PostAsync(_options.ActivateUrl, body, cancellationToken);
        }

        public Task<BackendLicenseResponse> ValidateAsync(ValidationRequest request, CancellationToken cancellationToken)
        {
            Guard.AgainstNull(request, nameof(request));
            var body = new
            {
                productKey = request.ProductKey,
                licenseKey = request.LicenseKey,
                deviceId = request.DeviceId,
                appVersion = request.AppVersion
            };
            return PostAsync(_options.ValidateUrl, body, cancellationToken);
        }

        public Task<BackendLicenseResponse> ActivateModuleAsync(ModuleActivationRequest request, CancellationToken cancellationToken)
        {
            Guard.AgainstNull(request, nameof(request));
            var body = new
            {
                productKey = request.ProductKey,
                licenseKey = request.LicenseKey,
                deviceId = request.DeviceId,
                activationKey = request.ActivationKey,
                appVersion = request.AppVersion
            };
            return PostAsync(ModuleUrl(), body, cancellationToken);
        }

        /// <summary>The activate-module endpoint, derived from the activate URL.</summary>
        private string ModuleUrl()
        {
            string activate = _options.ActivateUrl ?? string.Empty;
            int slash = activate.LastIndexOf('/');
            return slash > 0 ? activate.Substring(0, slash) + "/activate-module" : activate;
        }

        private async Task<BackendLicenseResponse> PostAsync(string url, object body, CancellationToken cancellationToken)
        {
            string requestJson = JsonConvert.SerializeObject(body);

            using (var message = new HttpRequestMessage(HttpMethod.Post, url))
            {
                message.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
                message.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _options.AnonKey);
                message.Headers.TryAddWithoutValidation("apikey", _options.AnonKey);

                using (HttpResponseMessage response = await Http.SendAsync(message, cancellationToken).ConfigureAwait(false))
                {
                    string responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Dto dto = SafeParse(responseJson);

                    return new BackendLicenseResponse
                    {
                        Success = response.IsSuccessStatusCode,
                        Token = dto.Token,
                        Status = dto.Status,
                        Modules = dto.Modules,
                        ErrorCode = dto.Error,
                        ErrorMessage = dto.Message
                    };
                }
            }
        }

        private Dto SafeParse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new Dto();
            }

            try
            {
                return JsonConvert.DeserializeObject<Dto>(json) ?? new Dto();
            }
            catch (Exception ex)
            {
                _logger.Warn("Could not parse licensing response: " + ex.Message);
                return new Dto();
            }
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            return client;
        }

        /// <summary>Maps the JSON shape returned by both Edge Functions (case-insensitive).</summary>
        private sealed class Dto
        {
            public string Token { get; set; }
            public string Status { get; set; }
            public string[] Modules { get; set; }
            public string Error { get; set; }
            public string Message { get; set; }
        }
    }
}
