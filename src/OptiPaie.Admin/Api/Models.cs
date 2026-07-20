using Newtonsoft.Json;

namespace OptiPaie.Admin.Api
{
    // POCOs mapping the Supabase REST rows (snake_case). Only license metadata —
    // never any payroll/employee/company business data.

    public sealed class License
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("product_id")] public string ProductId { get; set; }
        [JsonProperty("license_key")] public string LicenseKey { get; set; }
        [JsonProperty("company_name")] public string CompanyName { get; set; }
        [JsonProperty("email")] public string Email { get; set; }
        [JsonProperty("phone")] public string Phone { get; set; }
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("status")] public string Status { get; set; }
        [JsonProperty("max_devices")] public int MaxDevices { get; set; }
        [JsonProperty("expires_at")] public string ExpiresAt { get; set; }
        [JsonProperty("created_at")] public string CreatedAt { get; set; }
        [JsonProperty("notes")] public string Notes { get; set; }

        [JsonIgnore] public string ExpiresDisplay => string.IsNullOrEmpty(ExpiresAt) ? "Permanente" : Dates.Short(ExpiresAt);
        [JsonIgnore] public string CreatedDisplay => Dates.Short(CreatedAt);
    }

    public sealed class ModulePermission
    {
        [JsonProperty("module_key")] public string ModuleKey { get; set; }
        [JsonProperty("enabled")] public bool Enabled { get; set; }
        [JsonProperty("activated_at")] public string ActivatedAt { get; set; }
        [JsonProperty("expires_at")] public string ExpiresAt { get; set; }
    }

    public sealed class ActivationKey
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("module_key")] public string ModuleKey { get; set; }
        [JsonProperty("key_code")] public string KeyCode { get; set; }
        [JsonProperty("status")] public string Status { get; set; }
        [JsonProperty("created_at")] public string CreatedAt { get; set; }
        [JsonProperty("expires_at")] public string ExpiresAt { get; set; }

        [JsonIgnore] public string CreatedDisplay => Dates.Short(CreatedAt);
        [JsonIgnore] public string ExpiresDisplay => string.IsNullOrEmpty(ExpiresAt) ? "—" : Dates.Short(ExpiresAt);
    }

    public sealed class Device
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("device_id")] public string DeviceId { get; set; }
        [JsonProperty("app_version")] public string AppVersion { get; set; }
        [JsonProperty("activated_at")] public string ActivatedAt { get; set; }
        [JsonProperty("last_seen_at")] public string LastSeenAt { get; set; }
        [JsonProperty("is_active")] public bool IsActive { get; set; }
        [JsonProperty("licenses")] public License License { get; set; }

        [JsonIgnore] public string LicenseKey => License != null ? License.LicenseKey : "—";
        [JsonIgnore] public string ActivatedDisplay => Dates.Short(ActivatedAt);
        [JsonIgnore] public string LastSeenDisplay => Dates.DateTime(LastSeenAt);
        [JsonIgnore] public string StatusDisplay => IsActive ? "actif" : "inactif";
    }

    public sealed class UpdateRow
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("version")] public string Version { get; set; }
        [JsonProperty("channel")] public string Channel { get; set; }
        [JsonProperty("mandatory")] public bool Mandatory { get; set; }
        [JsonProperty("release_notes")] public string ReleaseNotes { get; set; }
        [JsonProperty("package_url")] public string PackageUrl { get; set; }
        [JsonProperty("is_latest")] public bool IsLatest { get; set; }
        [JsonProperty("published_at")] public string PublishedAt { get; set; }

        [JsonIgnore] public string MandatoryDisplay => Mandatory ? "obligatoire" : "optionnelle";
        [JsonIgnore] public string LatestDisplay => IsLatest ? "★ latest" : "";
        [JsonIgnore] public string PublishedDisplay => Dates.Short(PublishedAt);
    }

    public sealed class AuditRow
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("admin_email")] public string AdminEmail { get; set; }
        [JsonProperty("action")] public string Action { get; set; }
        [JsonProperty("company_name")] public string CompanyName { get; set; }
        [JsonProperty("license_key")] public string LicenseKey { get; set; }
        [JsonProperty("created_at")] public string CreatedAt { get; set; }

        [JsonIgnore] public string Actor => string.IsNullOrEmpty(AdminEmail) ? "système" : AdminEmail;
        [JsonIgnore] public string CreatedDisplay => Dates.DateTime(CreatedAt);
    }

    public sealed class Overview
    {
        [JsonProperty("total_licenses")] public int TotalLicenses { get; set; }
        [JsonProperty("active_licenses")] public int ActiveLicenses { get; set; }
        [JsonProperty("disabled_licenses")] public int DisabledLicenses { get; set; }
        [JsonProperty("expired_licenses")] public int ExpiredLicenses { get; set; }
        [JsonProperty("active_devices")] public int ActiveDevices { get; set; }
        [JsonProperty("new_this_month")] public int NewThisMonth { get; set; }
    }

    public sealed class ModuleStat
    {
        [JsonProperty("module_key")] public string ModuleKey { get; set; }
        [JsonProperty("name_fr")] public string NameFr { get; set; }
        [JsonProperty("enabled_count")] public int EnabledCount { get; set; }
        [JsonProperty("sort_order")] public int SortOrder { get; set; }
    }
}
