/**
 * One real Bedrock round trip without the RoadOps server: a fake MCP session
 * with one read tool and one write tool. Checks that Converse accepts the
 * brain's tool specs, toolResult messages and the step-limit note, and that a
 * write is held for confirmation. A few cheap calls; run it by hand only:
 *
 *   AWS_PROFILE=roadops-bedrock npx tsx scripts/bedrock-smoke.ts [model-id]
 *
 * Without a model id it tests the app's default (SIM_BEDROCK_MODEL, else DEFAULT_MODEL), so a pass means the
 * model `npm start` will use works.
 */
import { createBedrockBrain, type McpSession } from "mcp-voice-simulator";

import { DEFAULT_MODEL } from "../src/config.js";

const model = process.argv[2] ?? process.env.SIM_BEDROCK_MODEL ?? DEFAULT_MODEL;
console.log(`Model: ${model}`);
const calls: string[] = [];
const session = {
  ui: false,
  tools: [
    {
      name: "find_surveys",
      description: "Finds condition surveys by corridor.",
      inputSchema: { type: "object", properties: { corridor: { type: "string" } }, required: ["corridor"] },
      annotations: { readOnlyHint: true },
    },
    {
      name: "void_observation",
      description: "Voids an observation logged by mistake.",
      inputSchema: { type: "object", properties: { observationId: { type: "string" } }, required: ["observationId"] },
      annotations: { readOnlyHint: false, destructiveHint: true },
    },
  ],
  client: {
    async callTool({ name, arguments: args }: { name: string; arguments: Record<string, unknown> }) {
      calls.push(name);
      const text = name === "find_surveys" ? `Surveys for ${String(args.corridor)}: 2024 (412 observations), 2026 (538 observations).` : "Voided.";
      return { content: [{ type: "text", text }] };
    },
  },
} as unknown as McpSession;

const brain = createBedrockBrain({ session: async () => session, region: process.env.AWS_REGION ?? "us-east-1", model, maxSteps: 3 });

const first = await brain.turn("Which surveys are there for the N1?");
console.log("1:", first.reply, "| calls:", calls.join(", "));

const second = await brain.turn("Void observation obs-123.");
console.log("2:", second.reply, "| awaitingConfirmation:", second.awaitingConfirmation, "| calls:", calls.join(", "));
if (calls.includes("void_observation")) throw new Error("The write ran without a confirmation turn.");
