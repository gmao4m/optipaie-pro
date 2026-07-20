// POST /activate-module
//   Body: { productKey, licenseKey, deviceId, activationKey, appVersion? }
//   Redeems a SINGLE-USE module activation key: validates it, enables the module
//   on the license, marks the key used (atomically — replay/reuse safe), and
//   returns a fresh Ed25519-signed token that now includes the module.
//
//   Responses:
//     200 { token, status:'active', modules:[...] }
//     400 invalid_request | unknown_product
//     403 wrong_product | key_wrong_license | key_revoked | key_expired
//         | module_already_active | device_mismatch
//     404 key_invalid
//     409 key_used
import { serve } from "https://deno.land/std@0.224.0/http/server.ts";
import { json, preflight } from "../_shared/cors.ts";
import { serviceClient } from "../_shared/supabase.ts";
import {
  audit,
  buildSignedToken,
  getCustomerName,
  getProductByKey,
  logActivation,
  resolveModules,
} from "../_shared/license.ts";

serve(async (req) => {
  if (req.method === "OPTIONS") return preflight();
  if (req.method !== "POST") return json({ error: "method_not_allowed" }, 405);

  let body: {
    productKey?: string;
    licenseKey?: string;
    deviceId?: string;
    activationKey?: string;
    appVersion?: string;
  };
  try {
    body = await req.json();
  } catch {
    return json({ error: "invalid_request", message: "Body must be JSON." }, 400);
  }

  const productKey = (body.productKey ?? "").trim();
  const licenseKey = (body.licenseKey ?? "").trim().toUpperCase();
  const deviceId = (body.deviceId ?? "").trim();
  const activationKey = (body.activationKey ?? "").trim().toUpperCase();
  const appVersion = (body.appVersion ?? "").trim();

  if (!productKey || !licenseKey || !deviceId || !activationKey) {
    return json(
      { error: "invalid_request", message: "productKey, licenseKey, deviceId and activationKey are required." },
      400,
    );
  }

  const db = serviceClient();
  const nowIso = new Date().toISOString();

  const product = await getProductByKey(db, productKey);
  if (!product) {
    return json({ error: "unknown_product", message: "Produit inconnu." }, 400);
  }

  const fail = async (status: number, code: string, message: string, licenseId?: string | null) => {
    await logActivation(db, { license_id: licenseId ?? null, device_id: deviceId, action: "module_activate", result: code, app_version: appVersion });
    await audit(db, {
      action: "module.validation_failed",
      product_id: product.id,
      product_key: product.key,
      license_id: licenseId ?? null,
      details: { reason: code, activationKey, deviceId },
    });
    return json({ error: code, message }, status);
  };

  // 1. The key must exist.
  const { data: key } = await db
    .from("activation_keys")
    .select("*")
    .eq("key_code", activationKey)
    .maybeSingle();
  if (!key) return await fail(404, "key_invalid", "Clé d'activation invalide.");

  // 2. The key's license must exist, match the caller's license and product.
  const { data: lic } = await db.from("licenses").select("*").eq("id", key.license_id).maybeSingle();
  if (!lic) return await fail(404, "key_invalid", "Licence introuvable.", key.license_id);
  if (lic.product_id !== product.id) {
    return await fail(403, "wrong_product", "Cette clé n'appartient pas à ce logiciel.", lic.id);
  }
  if (lic.license_key.toUpperCase() !== licenseKey) {
    return await fail(403, "key_wrong_license", "Cette clé n'appartient pas à votre licence.", lic.id);
  }

  // 3. The device must be one this license was activated on.
  const { data: device } = await db
    .from("devices").select("id").eq("license_id", lic.id).eq("device_id", deviceId).eq("is_active", true).maybeSingle();
  if (!device) return await fail(409, "device_mismatch", "Licence non activée sur cet appareil.", lic.id);

  // 4. Key status checks (precise errors before the atomic claim).
  if (key.status === "revoked") return await fail(403, "key_revoked", "Clé d'activation révoquée.", lic.id);
  if (key.status === "used") return await fail(409, "key_used", "Clé d'activation déjà utilisée.", lic.id);
  if (key.expires_at && new Date(key.expires_at).getTime() < Date.now()) {
    await db.from("activation_keys").update({ status: "expired" }).eq("id", key.id).eq("status", "unused");
    return await fail(403, "key_expired", "Clé d'activation expirée.", lic.id);
  }

  // 5. Already active? Do NOT consume the key.
  const { data: existing } = await db
    .from("license_modules").select("enabled").eq("license_id", lic.id).eq("module_key", key.module_key).maybeSingle();
  if (existing && existing.enabled) {
    return await fail(403, "module_already_active", "Ce module est déjà activé.", lic.id);
  }

  // 6. ATOMIC single-use claim — only succeeds if still 'unused' (blocks replay/race).
  const { data: claimed } = await db
    .from("activation_keys")
    .update({ status: "used", used_at: nowIso, used_device: deviceId })
    .eq("id", key.id)
    .eq("status", "unused")
    .select("id")
    .maybeSingle();
  if (!claimed) return await fail(409, "key_used", "Clé d'activation déjà utilisée.", lic.id);

  // 7. Enable the module (carry the key's optional expiry).
  await db.from("license_modules").upsert(
    {
      license_id: lic.id,
      product_id: product.id,
      module_key: key.module_key,
      enabled: true,
      expires_at: key.expires_at,
      activated_at: nowIso,
    },
    { onConflict: "license_id,module_key" },
  );

  // 8. Fresh signed token with the new module.
  const customerName = await getCustomerName(db, lic.customer_id);
  const modules = await resolveModules(db, product.id, lic.id, lic.status);
  const { token } = buildSignedToken(lic, product, modules, deviceId, customerName);

  await logActivation(db, { license_id: lic.id, device_id: deviceId, action: "module_activate", result: "ok", app_version: appVersion });
  await audit(db, {
    action: "module.activate",
    product_id: product.id,
    product_key: product.key,
    license_id: lic.id,
    license_key: lic.license_key,
    company_name: lic.company_name,
    details: { module: key.module_key, key_code: activationKey, deviceId },
  });

  return json({ token, status: lic.status, modules }, 200);
});
