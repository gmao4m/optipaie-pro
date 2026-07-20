// POST /activate
//   Body: { productKey, licenseKey, companyName, email, deviceId, appVersion? }
//   Binds the license to the calling device (first activation), or re-confirms
//   an existing binding for the same device. Returns a signed license token
//   scoped to the app's own product.
//
//   Responses:
//     200 { token, status:'active', modules:[...] }
//     400 invalid_request | unknown_product
//     404 invalid_key
//     403 suspended | revoked | wrong_product
//     409 device_in_use
import { serve } from "https://deno.land/std@0.224.0/http/server.ts";
import { json, preflight } from "../_shared/cors.ts";
import { serviceClient } from "../_shared/supabase.ts";
import {
  audit,
  buildSignedToken,
  getCustomerName,
  getProductByKey,
  logActivation,
  registerDevice,
  resolveModules,
  UNASSIGNED_COMPANY,
} from "../_shared/license.ts";

serve(async (req) => {
  if (req.method === "OPTIONS") return preflight();
  if (req.method !== "POST") return json({ error: "method_not_allowed" }, 405);

  let body: {
    productKey?: string;
    licenseKey?: string;
    companyName?: string;
    email?: string;
    deviceId?: string;
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
  const companyName = (body.companyName ?? "").trim();
  const email = (body.email ?? "").trim();
  const appVersion = (body.appVersion ?? "").trim();

  if (!productKey || !licenseKey || !deviceId) {
    return json(
      {
        error: "invalid_request",
        message: "productKey, licenseKey and deviceId are required.",
      },
      400,
    );
  }

  const db = serviceClient();

  const product = await getProductByKey(db, productKey);
  if (!product) {
    return json({ error: "unknown_product", message: "Produit inconnu." }, 400);
  }

  const { data: lic, error } = await db
    .from("licenses")
    .select("*")
    .eq("license_key", licenseKey)
    .maybeSingle();

  if (error) return json({ error: "server_error" }, 500);

  if (!lic) {
    await audit(db, {
      action: "activation.error",
      product_id: product.id,
      product_key: product.key,
      license_key: licenseKey,
      details: { reason: "invalid_key", deviceId, companyName },
    });
    return json({ error: "invalid_key", message: "Clé de licence invalide." }, 404);
  }

  // The key must belong to THIS product — a Payroll app can't use an Accounting key.
  if (lic.product_id !== product.id) {
    await audit(db, {
      action: "activation.error",
      product_id: product.id,
      product_key: product.key,
      license_id: lic.id,
      license_key: licenseKey,
      company_name: lic.company_name,
      details: { reason: "wrong_product" },
    });
    return json(
      { error: "wrong_product", message: "Cette clé n'appartient pas à ce logiciel." },
      403,
    );
  }

  if (lic.status === "revoked" || lic.status === "suspended") {
    await logActivation(db, { license_id: lic.id, device_id: deviceId, action: "activate", result: lic.status, app_version: appVersion });
    await audit(db, {
      action: "activation.error",
      product_id: product.id,
      product_key: product.key,
      license_id: lic.id,
      license_key: licenseKey,
      company_name: lic.company_name,
      details: { reason: lic.status, deviceId },
    });
    return json(
      { error: lic.status, message: "Licence non disponible. Contactez le fournisseur." },
      403,
    );
  }

  // Expired dated license (annual/monthly/trial) → refuse.
  if (lic.expires_at && new Date(lic.expires_at).getTime() < Date.now()) {
    await logActivation(db, { license_id: lic.id, device_id: deviceId, action: "activate", result: "expired", app_version: appVersion });
    return json({ error: "expired", message: "Licence expirée." }, 403);
  }

  // Multi-device registration + max_devices enforcement.
  const reg = await registerDevice(db, lic, deviceId, null, appVersion);
  if (!reg.ok) {
    await logActivation(db, { license_id: lic.id, device_id: deviceId, action: "activate", result: "max_devices", app_version: appVersion });
    await audit(db, {
      action: "activation.error",
      product_id: product.id,
      product_key: product.key,
      license_id: lic.id,
      license_key: licenseKey,
      company_name: lic.company_name,
      details: { reason: "max_devices", requestedDevice: deviceId, max: lic.max_devices ?? 1 },
    });
    return json(
      { error: "max_devices", message: "Nombre maximum d'appareils atteint pour cette licence." },
      403,
    );
  }

  const nowIso = new Date().toISOString();
  const patch: Record<string, unknown> = {
    status: "active",
    last_validation_at: nowIso,
  };
  if (!lic.activated_at) patch.activated_at = nowIso;
  if (!lic.email && email) patch.email = email; // fill only if empty; admin stays source of truth
  // Bulk "pool" licenses start with a placeholder company name — adopt the
  // customer's real one on first activation (admin can still override later).
  if ((!lic.company_name || lic.company_name === UNASSIGNED_COMPANY) && companyName) {
    patch.company_name = companyName;
  }
  if (appVersion) patch.app_version = appVersion;

  const { data: updated, error: upErr } = await db
    .from("licenses")
    .update(patch)
    .eq("id", lic.id)
    .select("*")
    .single();

  if (upErr || !updated) return json({ error: "server_error" }, 500);

  const customerName = await getCustomerName(db, updated.customer_id);
  const modules = await resolveModules(db, product.id, updated.id, updated.status);
  const { token } = buildSignedToken(updated, product, modules, deviceId, customerName);

  await logActivation(db, { license_id: updated.id, device_id: deviceId, action: "activate", result: "ok", app_version: appVersion });
  await audit(db, {
    action: "license.activate",
    product_id: product.id,
    product_key: product.key,
    license_id: updated.id,
    license_key: updated.license_key,
    company_name: updated.company_name,
    details: { deviceId, type: updated.type, companyName, appVersion },
  });

  return json({ token, status: "active", modules }, 200);
});
