// Shared license logic: product resolution, module resolution, signed-token
// payload and audit logging. All product-scoped.
import type { SupabaseClient } from "https://esm.sh/@supabase/supabase-js@2.45.4";
import { signToken, signingKey } from "./ed25519.ts";

/** Default offline grace if a product doesn't override activation_rules.graceDays. */
export const DEFAULT_GRACE_DAYS = 30;

/** Placeholder company name used for bulk "pool" licenses before a customer activates. */
export const UNASSIGNED_COMPANY = "(non attribuée)";

export interface ProductRow {
  id: string;
  key: string;
  name: string;
  current_version: string | null;
  activation_rules: Record<string, unknown> | null;
}

export interface LicenseRow {
  id: string;
  product_id: string;
  license_key: string;
  company_name: string;
  company_id: string | null;
  customer_id: string | null;
  email: string | null;
  device_id: string | null;
  status: string;
  type: string | null;
  max_devices: number | null;
  // Company scope: 1 = Mono-société, 0 = Multi-sociétés (unlimited). Missing/null = Mono.
  max_companies: number | null;
  activated_at: string | null;
  last_validation_at: string | null;
  expires_at: string | null;
}

export async function getProductByKey(
  db: SupabaseClient,
  productKey: string,
): Promise<ProductRow | null> {
  const { data } = await db
    .from("products")
    .select("id, key, name, current_version, activation_rules")
    .eq("key", productKey)
    .maybeSingle();
  return (data as ProductRow) ?? null;
}

/** Whether this product binds licenses to a single device (default: true). */
export function bindsDevice(product: ProductRow): boolean {
  const rules = product.activation_rules ?? {};
  return rules["bindDevice"] !== false;
}

function graceDays(product: ProductRow): number {
  const rules = product.activation_rules ?? {};
  const v = Number(rules["graceDays"]);
  return Number.isFinite(v) && v > 0 ? v : DEFAULT_GRACE_DAYS;
}

/**
 * Enabled module keys for a license. Every license unlocks the WHOLE product —
 * Mono-société and Multi-sociétés differ ONLY in the number of companies
 * (max_companies), never in features. So an active license grants every module of
 * the product; a non-active one grants none, so a suspension/revocation still locks
 * the app (and sticks offline via the signed token). The per-license license_modules
 * flags are retained in the schema but no longer gate features.
 */
export async function resolveModules(
  db: SupabaseClient,
  productId: string,
  _licenseId: string,
  status: string,
): Promise<string[]> {
  if (status !== "active") return [];

  const all = await db
    .from("modules")
    .select("key")
    .eq("product_id", productId);

  return (all.data ?? []).map((m) => m.key as string);
}

/**
 * Builds the signed license token the desktop caches and verifies offline.
 * The payload carries `product` so a desktop app rejects tokens for other
 * products, and `graceUntil` from the product's own activation rules.
 */
export function buildSignedToken(
  license: LicenseRow,
  product: ProductRow,
  modules: string[],
  deviceId: string,
  customerName: string | null,
): { token: string; issuedAt: string; graceUntil: string } {
  const now = new Date();
  const graceUntil = new Date(now.getTime() + graceDays(product) * 86_400_000);

  // Company scope for the token: 1 = Mono, 0 = Multi (unlimited). A missing OR null
  // value — a row predating the max_companies column (absent from select("*")) or a
  // null cell — coerces to 1 (safe/Mono), which matches how the desktop client reads
  // an absent/null token field. Multi is therefore ALWAYS an explicit 0, never null.
  const rawScope = (license as { max_companies?: number | null }).max_companies;
  const maxCompanies = (rawScope === undefined || rawScope === null) ? 1 : rawScope;

  const payload = {
    v: 1,
    product: product.key,
    productVersion: product.current_version,
    licenseKey: license.license_key,
    companyName: license.company_name,
    companyId: license.company_id,
    customerName: customerName ?? license.company_name,
    email: license.email,
    // The token is bound to the requesting device (multi-device supported).
    deviceId: deviceId,
    status: license.status,
    type: license.type ?? "lifetime",
    // Company scope: 1 = Mono-société, 0 = Multi-sociétés (unlimited). Never null.
    maxCompanies,
    modules,
    issuedAt: now.toISOString(),
    expiresAt: license.expires_at, // null = perpetual
    graceUntil: graceUntil.toISOString(),
  };

  return {
    token: signToken(payload, signingKey()),
    issuedAt: payload.issuedAt,
    graceUntil: payload.graceUntil,
  };
}

/** Looks up the customer display name for a license (falls back to null). */
export async function getCustomerName(
  db: SupabaseClient,
  customerId: string | null,
): Promise<string | null> {
  if (!customerId) return null;
  const { data } = await db.from("customers").select("name").eq("id", customerId).maybeSingle();
  return (data?.name as string) ?? null;
}

/** Appends a row to the activations event log. Never throws into the request path. */
export async function logActivation(
  db: SupabaseClient,
  entry: {
    license_id?: string | null;
    device_id?: string | null;
    action: string;
    result: string;
    app_version?: string | null;
  },
): Promise<void> {
  try {
    await db.from("activations").insert({
      license_id: entry.license_id ?? null,
      device_id: entry.device_id ?? null,
      action: entry.action,
      result: entry.result,
      app_version: entry.app_version ?? null,
    });
  } catch (_e) {
    // Logging must never break activation/validation.
  }
}

/**
 * Registers (or refreshes) a device for a license, enforcing max_devices.
 * Returns { ok:true } when the device is allowed, or { ok:false, reason:"max_devices" }.
 */
export async function registerDevice(
  db: SupabaseClient,
  license: LicenseRow,
  deviceId: string,
  deviceInfo: string | null,
  appVersion: string | null,
): Promise<{ ok: boolean; reason?: string }> {
  const nowIso = new Date().toISOString();

  const { data: existing } = await db
    .from("devices")
    .select("id")
    .eq("license_id", license.id)
    .eq("device_id", deviceId)
    .maybeSingle();

  if (existing) {
    await db.from("devices")
      .update({ last_seen_at: nowIso, is_active: true, app_version: appVersion })
      .eq("id", existing.id);
    return { ok: true };
  }

  const { count } = await db
    .from("devices")
    .select("id", { count: "exact", head: true })
    .eq("license_id", license.id)
    .eq("is_active", true);

  const max = license.max_devices ?? 1;
  if ((count ?? 0) >= max) {
    return { ok: false, reason: "max_devices" };
  }

  await db.from("devices").insert({
    license_id: license.id,
    device_id: deviceId,
    device_info: deviceInfo,
    app_version: appVersion,
    activated_at: nowIso,
    last_seen_at: nowIso,
    is_active: true,
  });
  return { ok: true };
}

/** Appends one row to the audit trail. Never throws into the request path. */
export async function audit(
  db: SupabaseClient,
  entry: {
    action: string;
    admin_email?: string | null;
    product_id?: string | null;
    product_key?: string | null;
    license_id?: string | null;
    license_key?: string | null;
    company_name?: string | null;
    details?: unknown;
  },
): Promise<void> {
  try {
    await db.from("audit_log").insert({
      action: entry.action,
      admin_email: entry.admin_email ?? null,
      product_id: entry.product_id ?? null,
      product_key: entry.product_key ?? null,
      license_id: entry.license_id ?? null,
      license_key: entry.license_key ?? null,
      company_name: entry.company_name ?? null,
      details: entry.details ?? null,
    });
  } catch (_e) {
    // Auditing must never break activation/validation.
  }
}
