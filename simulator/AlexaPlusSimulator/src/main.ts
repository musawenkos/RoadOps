import { readFile } from "node:fs/promises";

import { McpSessionManager, callTool, createBedrockBrain, createServer } from "mcp-voice-simulator";

import { ConfigError, loadConfig, type AppConfig } from "./config.js";
import { FieldDevice } from "./device.js";
import { classifyRoadOpsToolCall, createFieldBrain } from "./field-brain.js";
import { uploadPhoto } from "./upload.js";

let config: AppConfig;
try {
  config = loadConfig(process.env);
} catch (err) {
  if (!(err instanceof ConfigError)) throw err;
  console.error(`roadops-simulator: ${err.message}`);
  process.exit(1);
}

const systemPrompt = await readFile(config.systemPromptFile, "utf8");

const sessionManager = new McpSessionManager({
  mcpUrl: config.mcpUrl,
  bearerToken: config.bearerToken,
  ui: config.ui,
});

const agent = createBedrockBrain({
  session: () => sessionManager.ensureSession(),
  region: config.region,
  model: config.model,
  systemPrompt,
  maxSteps: config.maxSteps,
  classifyToolCall: classifyRoadOpsToolCall,
});

const device = new FieldDevice({ inspector: config.inspector });
if (config.gps) device.setGps(config.gps.latitude, config.gps.longitude);

const brain = createFieldBrain(agent, device, {
  uploadPhoto: (filePath) => uploadPhoto(config.mcpUrl, config.bearerToken, filePath),
  async loadPreviousSession() {
    const session = await sessionManager.ensureSession();
    if (!session.tools.some((t) => t.name === "get_session_summary")) return undefined;
    const result = await callTool(session, "get_session_summary", {});
    return result.isError ? undefined : result.text;
  },
  log: (line) => console.log(`  ${line}`),
});

createServer({
  sessionManager,
  brain,
  port: config.port,
  ui: config.ui,
  turnContext: () => device.context(),
});

console.log(`RoadOps Alexa+ simulator: http://127.0.0.1:${config.port}`);
console.log(`  MCP server: ${config.mcpUrl}`);
console.log(`  Brain: bedrock, ${config.model} in ${config.region}${process.env.AWS_PROFILE ? ` (profile ${process.env.AWS_PROFILE})` : ""}`);
console.log(`  Max tool calls per turn: ${config.maxSteps}`);
console.log(`  GPS: ${config.gps ? `${config.gps.latitude}, ${config.gps.longitude}` : "none (type /gps latitude, longitude)"}`);
console.log(`  Type /help in the simulator for the GPS and photo commands.`);
