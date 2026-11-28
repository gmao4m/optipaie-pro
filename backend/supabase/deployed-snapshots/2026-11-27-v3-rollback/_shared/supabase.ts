// Service-role Supabase client used inside the Edge Functions. The service role
// bypasses RLS — this key is injected by the platform and stays server-side.
import { createClient } from "https://esm.sh/@supabase/supabase-js@2.45.4";

export function serviceClient() {
  const url = Deno.env.get("SUPABASE_URL");
  const key = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY");
  if (!url || !key) {
    throw new Error("Missing SUPABASE_URL / SUPABASE_SERVICE_ROLE_KEY env.");
  }
  return createClient(url, key, { auth: { persistSession: false } });
}
