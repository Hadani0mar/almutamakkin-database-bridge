import "jsr:@supabase/functions-js/edge-runtime.d.ts";
import { createClient } from "jsr:@supabase/supabase-js@2";

const cors = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers": "authorization, x-client-info, apikey, content-type",
};

const expectedKeyId = "amkq-2026-07-28";
// Public verification material is intentionally embedded.  It permits the
// Edge Function to authenticate a signed package without storing a publisher
// credential on a workstation; it cannot create signatures itself.
const signingPublicKeySpkiBase64 =
  "MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEAt0zqy2dv7mpvWqkpOzQmHBnz3TU0Xe464ElIvUNdWllixmhfpBg5y6AjD30llPqbCETnaBtrYXNCBb5yI7HUyKDC8u3HVxsJtucmYdX3s8wr5Igsr453cZgUBtceeIwHNH7cFok5XbzyZTjKw6dYS3pyHDMEnwniCzz2nvUjVNc5abfikOHsTw+T7jUWB+c1uBi+LsZkANHAKmN0LZu31yQsNUjo68wv93bR5hU3q3YilI9mmGr77u4Aw9BSeUGoMME08T4ICaSITVEUqpGdo3rU/BnVExkQjy9GSjkbLLLWrpcbLaGNkTzmkoHGTiGvoY07xhQzYFDBj5Ij5tjCGYfOa105ixjXrXmExgk79d70PTtUmiX3xliNcDPOqzJwktQWd3t75Gj2F6r1qOXE5orygtqeTqFnJNMXUITf0udHJddiwBqQBPZtLyPl+lzGZoFbijgqCDRWdoyI1VwVo+UTT41u8M8SlGdO0DY7WzYlVdtm9OhC8S7WLZjiL+YdAgMBAAE=";
const supportedParameterTypes = new Set([
  "string",
  "int",
  "int[]",
  "long",
  "decimal",
  "double",
  "bool",
  "datetime",
  "guid",
  "null",
]);

type ParameterDefinition = {
  name: string;
  type: string;
  required: boolean;
};

type PackageDefinition = {
  queryId: string;
  version: number;
  system: string;
  databaseProfile: string;
  sql: string;
  parameters: ParameterDefinition[];
  timeoutSeconds: number;
  maxRows: number;
};

function field(source: Record<string, unknown>, name: string): unknown {
  return source[name] ?? source[name.charAt(0).toUpperCase() + name.slice(1)];
}

function bytesToBase64(bytes: Uint8Array): string {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary);
}

function base64ToBytes(value: string): Uint8Array {
  const binary = atob(value);
  return Uint8Array.from(binary, (character) => character.charCodeAt(0));
}

function normalizeParameters(value: unknown): ParameterDefinition[] | null {
  if (!Array.isArray(value)) return null;

  const names = new Set<string>();
  const parameters: ParameterDefinition[] = [];
  for (const candidate of value) {
    if (!candidate || typeof candidate !== "object") return null;
    const item = candidate as Record<string, unknown>;
    const name = String(field(item, "name") ?? "");
    const type = String(field(item, "type") ?? "");
    const requiredValue = field(item, "required");
    const required = requiredValue === undefined ? true : requiredValue === true;
    if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(name) || !supportedParameterTypes.has(type.toLowerCase())) {
      return null;
    }

    const key = name.toLowerCase();
    if (names.has(key)) return null;
    names.add(key);
    parameters.push({ name, type, required });
  }

  return parameters;
}

function canonicalPayload(definition: PackageDefinition): Uint8Array {
  // This exact AMKQ1 format must stay identical to
  // QueryPackageSignaturePayload.Build in the .NET bridge.
  const parameterPart = [...definition.parameters]
    .sort((left, right) => left.name < right.name ? -1 : left.name > right.name ? 1 : 0)
    .map((parameter) => `${parameter.name}|${parameter.type}|${parameter.required ? "1" : "0"}`)
    .join("\n");
  const canonical = [
    "AMKQ1",
    definition.queryId,
    String(definition.version),
    definition.system,
    definition.databaseProfile,
    String(definition.timeoutSeconds),
    String(definition.maxRows),
    parameterPart,
    definition.sql,
  ].join("\n");
  return new TextEncoder().encode(canonical);
}

async function verifySignature(
  definition: PackageDefinition,
  keyId: string,
  signatureBase64: string,
): Promise<boolean> {
  if (keyId !== expectedKeyId) return false;
  try {
    const publicKey = await crypto.subtle.importKey(
      "spki",
      base64ToBytes(signingPublicKeySpkiBase64),
      { name: "RSA-PSS", hash: "SHA-256" },
      false,
      ["verify"],
    );
    return await crypto.subtle.verify(
      { name: "RSA-PSS", saltLength: 32 },
      publicKey,
      base64ToBytes(signatureBase64),
      canonicalPayload(definition),
    );
  } catch (_) {
    return false;
  }
}

async function encryptSql(sql: string, keyBase64: string): Promise<{ ciphertext: string; iv: string }> {
  const key = await crypto.subtle.importKey("raw", base64ToBytes(keyBase64), { name: "AES-GCM" }, false, ["encrypt"]);
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const cipher = await crypto.subtle.encrypt({ name: "AES-GCM", iv }, key, new TextEncoder().encode(sql));
  return { ciphertext: bytesToBase64(new Uint8Array(cipher)), iv: bytesToBase64(iv) };
}

async function decryptSql(ciphertext: string, iv: string, keyBase64: string): Promise<string> {
  const key = await crypto.subtle.importKey("raw", base64ToBytes(keyBase64), { name: "AES-GCM" }, false, ["decrypt"]);
  const plaintext = await crypto.subtle.decrypt({ name: "AES-GCM", iv: base64ToBytes(iv) }, key, base64ToBytes(ciphertext));
  return new TextDecoder().decode(plaintext);
}

function isReadOnly(sql: string): boolean {
  const normalized = sql.replace(/\/\*[\s\S]*?\*\//g, " ").replace(/--.*$/gm, " ").trim().toUpperCase();
  if (!(normalized.startsWith("SELECT") || normalized.startsWith("WITH"))) return false;
  return !/\b(INSERT|UPDATE|DELETE|MERGE|ALTER|DROP|CREATE|EXEC(?:UTE)?|TRUNCATE|GRANT|REVOKE|SELECT\s+[^;]+\s+INTO)\b/.test(normalized);
}

function parseDefinition(value: unknown): PackageDefinition | null {
  if (!value || typeof value !== "object") return null;
  const source = value as Record<string, unknown>;
  const queryId = String(field(source, "queryId") ?? "");
  const system = String(field(source, "system") ?? "");
  const databaseProfile = String(field(source, "databaseProfile") ?? "");
  const sql = String(field(source, "sql") ?? "");
  const version = Number(field(source, "version") ?? 0);
  const timeoutSeconds = Number(field(source, "timeoutSeconds") ?? 30);
  const maxRows = Number(field(source, "maxRows") ?? 1000);
  const parameters = normalizeParameters(field(source, "parameters") ?? []);
  const systemMatches = (system === "marketing" && databaseProfile === "Marketing") ||
    (system === "infinity" && databaseProfile === "InfinityRetailDB");

  if (!/^[a-z][a-z0-9_.-]{2,119}$/.test(queryId) || !systemMatches || !isReadOnly(sql) ||
    !Number.isInteger(version) || version < 1 || !Number.isInteger(timeoutSeconds) || timeoutSeconds < 1 || timeoutSeconds > 600 ||
    !Number.isInteger(maxRows) || maxRows < 1 || maxRows > 30000 || !parameters) {
    return null;
  }

  return { queryId, version, system, databaseProfile, sql, parameters, timeoutSeconds, maxRows };
}

function sameDefinition(
  current: Record<string, unknown>,
  definition: PackageDefinition,
  currentSql: string,
): boolean {
  const currentParameters = normalizeParameters(current.parameter_schema);
  if (!currentParameters) return false;
  const currentCanonical = canonicalPayload({
    queryId: String(current.query_id ?? ""),
    version: Number(current.version ?? 0),
    system: String(current.system ?? ""),
    databaseProfile: String(current.database_profile ?? ""),
    sql: currentSql,
    parameters: currentParameters,
    timeoutSeconds: Number(current.timeout_seconds ?? 0),
    maxRows: Number(current.max_rows ?? 0),
  });
  const submittedCanonical = canonicalPayload(definition);
  return currentCanonical.length === submittedCanonical.length &&
    currentCanonical.every((byte, index) => byte === submittedCanonical[index]);
}

Deno.serve(async (req) => {
  if (req.method === "OPTIONS") return new Response("ok", { headers: cors });
  if (req.method !== "POST") {
    return new Response(JSON.stringify({ error: "method_not_allowed" }), { status: 405, headers: { ...cors, "Content-Type": "application/json" } });
  }

  try {
    const body = await req.json();
    const definition = parseDefinition(body?.definition);
    const keyId = String(body?.keyId ?? "");
    const signatureBase64 = String(body?.signatureBase64 ?? "");
    if (!definition || !signatureBase64 || !(await verifySignature(definition, keyId, signatureBase64))) {
      return new Response(JSON.stringify({ error: "invalid_signed_package" }), { status: 400, headers: { ...cors, "Content-Type": "application/json" } });
    }

    const admin = createClient(Deno.env.get("SUPABASE_URL")!, Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!);
    const { data: config, error: configError } = await admin.rpc("get_bridge_query_catalog_crypto_config");
    if (configError || !config?.bridge_query_catalog_aes_key) throw new Error("query_catalog_crypto_unavailable");

    const { data: existing, error: existingError } = await admin
      .from("bridge_query_packages")
      .select("query_id, version, system, database_profile, encrypted_sql, encryption_iv, parameter_schema, timeout_seconds, max_rows")
      .eq("query_id", definition.queryId)
      .maybeSingle();
    if (existingError) throw existingError;

    if (existing && Number(existing.version) > definition.version) {
      return new Response(JSON.stringify({ error: "query_package_version_downgrade" }), { status: 409, headers: { ...cors, "Content-Type": "application/json" } });
    }

    if (existing && Number(existing.version) === definition.version) {
      const existingSql = await decryptSql(
        String(existing.encrypted_sql ?? ""),
        String(existing.encryption_iv ?? ""),
        config.bridge_query_catalog_aes_key,
      );
      if (sameDefinition(existing, definition, existingSql)) {
        return new Response(JSON.stringify({ success: true, unchanged: true, queryId: definition.queryId, version: definition.version }), { headers: { ...cors, "Content-Type": "application/json" } });
      }
      return new Response(JSON.stringify({ error: "query_package_version_conflict" }), { status: 409, headers: { ...cors, "Content-Type": "application/json" } });
    }

    const encrypted = await encryptSql(definition.sql, config.bridge_query_catalog_aes_key);
    const { error } = await admin.from("bridge_query_packages").upsert({
      query_id: definition.queryId,
      version: definition.version,
      system: definition.system,
      database_profile: definition.databaseProfile,
      encrypted_sql: encrypted.ciphertext,
      encryption_iv: encrypted.iv,
      parameter_schema: definition.parameters,
      timeout_seconds: definition.timeoutSeconds,
      max_rows: definition.maxRows,
      key_id: keyId,
      signature_base64: signatureBase64,
      is_enabled: true,
      updated_at: new Date().toISOString(),
    }, { onConflict: "query_id" });
    if (error) throw error;
    return new Response(JSON.stringify({ success: true, queryId: definition.queryId, version: definition.version }), { headers: { ...cors, "Content-Type": "application/json" } });
  } catch (_) {
    return new Response(JSON.stringify({ error: "query_package_publish_failed" }), { status: 500, headers: { ...cors, "Content-Type": "application/json" } });
  }
});
