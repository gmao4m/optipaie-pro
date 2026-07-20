// POST /validate
//   Body: { productKey, licenseKey, deviceId, appVersion? }
//   Re-issues a fresh signed token reflecting the CURRENT server state (new
//   module purchases, suspension, revocation). Called on every app start when
//   online, and every 24h in the background.
//
//   Responses:
//     200 { token, status, modules:[...] }   — status may be active|suspended|revoked
//     400 invalid_request | unknown_product
//     404 invalid_key
//     403 wrong_product
//     409 device_mismatch
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

  let body: { productKey?: string; licenseKey?: string; deviceId?: string; appVersion?: string };
  try {
    body = await req.json();
  } catch {
    return json({ error: "invalid_request", message: "Body must be JSON." }, 400);
  }

  const productKey = (body.productKey ?? "").trim();
  const licenseKey = (body.licenseKey ?? "").trim().toUpperCase();
  const deviceId = (body.deviceId ?? "").trim();
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
      action: "validation.error",
      product_id: product.id,
      product_key: product.key,
      license_key: licenseKey,
      details: { reason: "invalid_key", deviceId },
    });
    return json({ error: "invalid_key", message: "Clé de licence invalide." }, 404);
  }

  if (lic.product_id !== product.id) {
    await audit(db, {
      action: "validation.error",
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

  // The device must be one this license was activated on (multi-device supported).
  const { data: device } = await db
    .from("devices")
    .select("id")
    .eq("license_id", lic.id)
    .eq("device_id", deviceId)
    .eq("is_active", true)
    .maybeSingle();

  if (!device) {
    await logActivation(db, { license_id: lic.id, device_id: deviceId, action: "validate", result: "device_mismatch", app_version: appVersion });
    await audit(db, {
      action: "validation.error",
      product_id: product.id,
      product_key: product.key,
      license_id: lic.id,
      license_key: licenseKey,
      company_name: lic.company_name,
      details: { reason: "device_mismatch", requestedDevice: deviceId },
    });
    return json(
      { error: "device_mismatch", message: "Licence non activée sur cet appareil." },
      409,
    );
  }

  // Refresh heartbeats (device + license). Status is NOT changed here — that is an
  // admin action; validate only reflects it.
  const nowIso = new Date().toISOString();
  await db.from("devices")
    .update({ last_seen_at: nowIso, app_version: appVersion || null })
    .eq("id", device.id);

  const patch: Record<string, unknown> = { last_validation_at: nowIso };
  if (appVersion) patch.app_version = appVersion;

  const { data: updated } = await db
    .from("licenses")
    .update(patch)
    .eq("id", lic.id)
    .select("*")
    .single();

  const row = updated ?? lic;
  const customerName = await getCustomerName(db, row.customer_id);
  const modules = await resolveModules(db, product.id, row.id, row.status);
  const { token } = buildSignedToken(row, product, modules, deviceId, customerName);

  await logActivation(db, { license_id: row.id, device_id: deviceId, action: "validate", result: "ok", app_version: appVersion });
  await audit(db, {
    action: "license.validate",
    product_id: product.id,
    product_key: product.key,
    license_id: row.id,
    license_key: row.license_key,
    company_name: row.company_name,
    details: { deviceId, status: row.status, type: row.type, moduleCount: modules.length },
  });

  return json({ token, status: row.status, modules }, 200);
});
