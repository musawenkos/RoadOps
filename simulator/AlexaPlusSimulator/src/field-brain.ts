import type { Brain, BrainTurnContext, BrainTurnResult, ClassifyToolCall } from "mcp-voice-simulator";

import { ConfigError, parseGps } from "./config.js";
import type { FieldDevice } from "./device.js";

/** Writes that run without a confirmation turn: attaching a photo the
 *  inspector just took and uploaded is already their deliberate act, and saving
 *  their own session bookmark changes no survey data. */
const NO_CONFIRMATION = new Set(["attach_photo", "save_session_summary"]);

/**
 * RoadOps' write tools: log_observation previews without `confirm` and the
 * brain's default handles that; update_observation and void_observation need
 * a yes on the next turn.
 */
export const classifyRoadOpsToolCall: ClassifyToolCall = (tool) => (tool && NO_CONFIRMATION.has(tool.name) ? "run" : undefined);

export interface FieldBrainOptions {
  /** Uploads a local photo for `/upload`, returning its id. */
  uploadPhoto?: (filePath: string) => Promise<string>;
  /** Where the inspector left off last time, as the server remembers it. Loaded
   *  once per session (at the first turn, and again after a reset) and passed
   *  to the agent as `previousSession`. */
  loadPreviousSession?: () => Promise<string | undefined>;
  /** One line per turn: tool names, timings and errors only, never arguments
   *  or results (they can hold names, notes and locations). */
  log?: (line: string) => void;
}

const HELP =
  "Commands: /gps latitude, longitude (set the phone's position), /gps off, /photo <id> (a photo already uploaded), " +
  "/upload <file> (upload a .jpg, .png or .webp), /device (show what the phone knows). Anything else goes to the assistant.";

/**
 * Wraps the agent with the phone's side of the conversation for typed
 * testing: slash commands stand in for GPS and the camera until the page
 * captures them, and are answered here without calling the model.
 */
export function createFieldBrain(inner: Brain, device: FieldDevice, options: FieldBrainOptions = {}): Brain {
  async function command(text: string): Promise<string> {
    const [, name, rest = ""] = text.match(/^\/(\w+)\s*(.*)$/s) ?? [];
    const arg = rest.trim();
    switch (name?.toLowerCase()) {
      case "gps": {
        if (/^(off|clear|none)$/i.test(arg)) {
          device.clearGps();
          return "GPS cleared.";
        }
        try {
          const { latitude, longitude } = parseGps(arg);
          device.setGps(latitude, longitude);
          return `GPS set to ${latitude}, ${longitude}.`;
        } catch (err) {
          if (err instanceof ConfigError) return err.message;
          throw err;
        }
      }
      case "photo":
        if (!/^[\w-]{1,100}$/.test(arg)) return "Give the photo id, e.g. /photo 3f2a...";
        device.addPhoto(arg);
        return `Photo ${arg} is now the latest photo.`;
      case "upload": {
        if (!options.uploadPhoto) return "Uploading isn't set up.";
        if (!arg) return "Give the file to upload, e.g. /upload C:\\photos\\pothole.jpg";
        try {
          const id = await options.uploadPhoto(arg.replace(/^"(.*)"$/, "$1"));
          device.addPhoto(id);
          return `Uploaded. Photo ${id} is now the latest photo. Say "attach the photo" to add it to your observation.`;
        } catch (err) {
          return err instanceof Error ? err.message : String(err);
        }
      }
      case "device":
        return JSON.stringify(device.context());
      default:
        return HELP;
    }
  }

  let sessionStarted = false;

  /** The previous session's summary on a session's first turn; nothing after. */
  async function previousSession(): Promise<BrainTurnContext> {
    if (sessionStarted || !options.loadPreviousSession) return {};
    sessionStarted = true;
    try {
      const summary = await options.loadPreviousSession();
      return summary ? { previousSession: summary } : {};
    } catch (err) {
      // A missing summary shouldn't stop the inspector working.
      options.log?.(`previous session not loaded: ${err instanceof Error ? err.message : String(err)}`);
      return {};
    }
  }

  return {
    name: inner.name,
    reset: () => {
      sessionStarted = false;
      return inner.reset?.();
    },
    async turn(utterance: string, context?: BrainTurnContext): Promise<BrainTurnResult> {
      if (utterance.startsWith("/")) return { reply: await command(utterance), trace: [] };

      const started = Date.now();
      const withMemory = { ...(await previousSession()), ...context };
      const result = await inner.turn(utterance, Object.keys(withMemory).length ? withMemory : undefined);
      const calls = result.trace.map((t) => `${t.tool}${t.isError ? " (error)" : ""} ${t.ms}ms`).join(", ");
      options.log?.(
        `turn: ${Date.now() - started}ms, ${result.trace.length} tool call(s)${calls ? `: ${calls}` : ""}` +
          (result.awaitingConfirmation ? ", waiting for confirmation" : ""),
      );
      return result;
    },
  };
}
