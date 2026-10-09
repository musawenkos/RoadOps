import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

/** Claude Haiku 4.5: the model the manual tests run on, and the one our account can still call (friction log 6).
 *  Any Converse model with tool use works through SIM_BEDROCK_MODEL; check it with scripts/bedrock-smoke.ts first. */
export const DEFAULT_MODEL = "us.anthropic.claude-haiku-4-5-20251001-v1:0";

export const DEFAULT_PROMPT_FILE = join(dirname(fileURLToPath(import.meta.url)), "..", "prompts", "roadops-system.md");

export interface AppConfig {
  mcpUrl: string;
  /** The inspector's RoadOps MCP API key. The server knows who is acting from it. */
  bearerToken: string;
  region: string;
  model: string;
  maxSteps: number;
  port: number;
  ui: boolean;
  systemPromptFile: string;
  /** Only for the assistant to address the inspector by name. */
  inspector?: string;
  /** A starting GPS fix for typed testing, until the phone sends real ones. */
  gps?: { latitude: number; longitude: number };
}

export class ConfigError extends Error {}

export function parseGps(value: string): { latitude: number; longitude: number } {
  const parts = value.split(",").map((p) => Number(p.trim()));
  const [latitude, longitude] = parts;
  if (
    parts.length !== 2 ||
    !Number.isFinite(latitude) ||
    !Number.isFinite(longitude) ||
    Math.abs(latitude) > 90 ||
    Math.abs(longitude) > 180
  ) {
    throw new ConfigError(`A GPS position is "latitude, longitude" in decimal degrees, e.g. "-25.6123, 28.3012" (got "${value}").`);
  }
  return { latitude, longitude };
}

/** Reads the app's settings from environment variables. AWS credentials are
 *  never read here: the AWS SDK finds them itself (AWS_PROFILE, SSO...). */
export function loadConfig(env: NodeJS.ProcessEnv): AppConfig {
  const get = (name: string) => env[name]?.trim() || undefined;

  const bearerToken = get("MCP_BEARER_TOKEN");
  if (!bearerToken) {
    throw new ConfigError("MCP_BEARER_TOKEN is required: the inspector's RoadOps MCP API key.");
  }

  const maxSteps = Number(get("SIM_MAX_STEPS") ?? 8);
  if (!Number.isInteger(maxSteps) || maxSteps < 1 || maxSteps > 20) {
    throw new ConfigError(`SIM_MAX_STEPS must be a whole number from 1 to 20 (got "${get("SIM_MAX_STEPS")}").`);
  }
  const port = Number(get("SIM_PORT") ?? 8790);
  if (!Number.isInteger(port) || port < 0 || port > 65535) {
    throw new ConfigError(`SIM_PORT must be a port number (got "${get("SIM_PORT")}").`);
  }
  const ui = (get("SIM_UI") ?? "on").toLowerCase();
  if (ui !== "on" && ui !== "off") throw new ConfigError(`SIM_UI must be "on" or "off" (got "${ui}").`);

  const gps = get("SIM_GPS");
  return {
    mcpUrl: get("MCP_URL") ?? "http://localhost:5288/mcp",
    bearerToken,
    region: get("AWS_REGION") ?? get("AWS_DEFAULT_REGION") ?? "us-east-1",
    model: get("SIM_BEDROCK_MODEL") ?? DEFAULT_MODEL,
    maxSteps,
    port,
    ui: ui === "on",
    systemPromptFile: get("SIM_SYSTEM_PROMPT_FILE") ?? DEFAULT_PROMPT_FILE,
    inspector: get("INSPECTOR_NAME"),
    gps: gps ? parseGps(gps) : undefined,
  };
}
