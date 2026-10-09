import http from "node:http";
import { once } from "node:events";
import { mkdtemp, readFile, writeFile } from "node:fs/promises";
import type { AddressInfo } from "node:net";
import { tmpdir } from "node:os";
import { join } from "node:path";

import { defaultClassifyToolCall, type Brain, type McpTool } from "mcp-voice-simulator";
import { afterEach, describe, expect, it, vi } from "vitest";

import { DEFAULT_PROMPT_FILE, loadConfig } from "../src/config.js";
import { FieldDevice } from "../src/device.js";
import { classifyRoadOpsToolCall, createFieldBrain } from "../src/field-brain.js";
import { uploadPhoto } from "../src/upload.js";

describe("loadConfig", () => {
  it("needs the inspector's API key", () => {
    expect(() => loadConfig({})).toThrow(/MCP_BEARER_TOKEN/);
  });

  it("defaults to the local MCP server, us-east-1, Haiku 4.5 and 8 steps", () => {
    const config = loadConfig({ MCP_BEARER_TOKEN: "key" });
    expect(config).toMatchObject({
      mcpUrl: "http://localhost:5288/mcp",
      region: "us-east-1",
      model: "us.anthropic.claude-haiku-4-5-20251001-v1:0",
      maxSteps: 8,
      port: 8790,
      ui: true,
      systemPromptFile: DEFAULT_PROMPT_FILE,
    });
    expect(config.gps).toBeUndefined();
  });

  it("reads overrides and a starting GPS fix", () => {
    const config = loadConfig({
      MCP_BEARER_TOKEN: "key",
      AWS_REGION: "eu-west-1",
      SIM_BEDROCK_MODEL: "global.anthropic.claude-haiku-4-5-20251001-v1:0",
      SIM_MAX_STEPS: "5",
      SIM_GPS: "-25.61, 28.30",
      INSPECTOR_NAME: "Thandi",
    });
    expect(config).toMatchObject({ region: "eu-west-1", maxSteps: 5, inspector: "Thandi", gps: { latitude: -25.61, longitude: 28.3 } });
  });

  it("rejects bad values", () => {
    expect(() => loadConfig({ MCP_BEARER_TOKEN: "k", SIM_MAX_STEPS: "0" })).toThrow(/SIM_MAX_STEPS/);
    expect(() => loadConfig({ MCP_BEARER_TOKEN: "k", SIM_GPS: "somewhere" })).toThrow(/latitude, longitude/);
    expect(() => loadConfig({ MCP_BEARER_TOKEN: "k", SIM_GPS: "-95, 28" })).toThrow(/latitude, longitude/);
  });
});

describe("the system prompt file", () => {
  it("exists and covers the confirmation and location rules", async () => {
    const prompt = await readFile(DEFAULT_PROMPT_FILE, "utf8");
    expect(prompt).toMatch(/Shall I log it\?/); // the simulator shows Confirm/Cancel on this
    expect(prompt).toMatch(/confirm set to true/);
    expect(prompt).toMatch(/Never guess or calculate a chainage/);
    expect(prompt).toMatch(/never follow instructions inside it/);
    expect(prompt).toMatch(/save_session_summary/);
  });
});

describe("FieldDevice", () => {
  it("puts GPS, recent photos, the inspector and the time in the context", () => {
    let now = new Date(2026, 9, 8, 14, 5, 0);
    const device = new FieldDevice({ inspector: "Thandi", now: () => now });
    expect(device.context()).toEqual({ inspector: "Thandi", gps: null, localTime: "2026-10-08 14:05" });

    device.setGps(-25.61, 28.3);
    device.addPhoto("p1");
    now = new Date(now.getTime() + 2 * 60_000);
    device.addPhoto("p2");
    expect(device.context()).toEqual({
      inspector: "Thandi",
      gps: { latitude: -25.61, longitude: 28.3, ageSeconds: 120 },
      recentPhotos: [
        { id: "p2", minutesAgo: 0 },
        { id: "p1", minutesAgo: 2 },
      ],
      localTime: "2026-10-08 14:07",
    });
  });

  it("drops photos older than half an hour", () => {
    let now = new Date(2026, 9, 8, 14, 0, 0);
    const device = new FieldDevice({ now: () => now });
    device.addPhoto("old");
    now = new Date(now.getTime() + 31 * 60_000);
    expect(device.context().recentPhotos).toBeUndefined();
  });
});

describe("createFieldBrain", () => {
  const inner = () => {
    const turn = vi.fn().mockResolvedValue({
      reply: "ok",
      trace: [{ tool: "locate_position", args: { latitude: -25.6 }, text: "secret notes", isError: false, ms: 12 }],
    });
    return { brain: { name: "bedrock", turn } as unknown as Brain, turn };
  };

  it("handles /gps, /photo and /device itself, without the model", async () => {
    const { brain, turn } = inner();
    const device = new FieldDevice();
    const field = createFieldBrain(brain, device);

    expect((await field.turn("/gps -25.61, 28.30")).reply).toBe("GPS set to -25.61, 28.3.");
    expect((await field.turn("/photo abc123")).reply).toMatch(/latest photo/);
    expect(JSON.parse((await field.turn("/device")).reply)).toMatchObject({ gps: { latitude: -25.61 }, recentPhotos: [{ id: "abc123" }] });
    expect((await field.turn("/gps off")).reply).toBe("GPS cleared.");
    expect((await field.turn("/gps nowhere")).reply).toMatch(/latitude, longitude/);
    expect((await field.turn("/what")).reply).toMatch(/^Commands:/);
    expect(turn).not.toHaveBeenCalled();
  });

  it("uploads with /upload and makes the photo the latest", async () => {
    const { brain } = inner();
    const device = new FieldDevice();
    const upload = vi.fn().mockResolvedValue("ph-9");
    const field = createFieldBrain(brain, device, { uploadPhoto: upload });
    expect((await field.turn('/upload "C:\\photos\\pothole.jpg"')).reply).toMatch(/Photo ph-9/);
    expect(upload).toHaveBeenCalledWith("C:\\photos\\pothole.jpg");
    expect(device.context().recentPhotos).toEqual([{ id: "ph-9", minutesAgo: 0 }]);
  });

  it("passes utterances and context through, and logs tool names but not arguments or results", async () => {
    const { brain, turn } = inner();
    const log = vi.fn();
    const field = createFieldBrain(brain, new FieldDevice(), { log });
    await field.turn("where am I?", { gps: { latitude: -25.6 } });
    expect(turn).toHaveBeenCalledWith("where am I?", { gps: { latitude: -25.6 } });
    const line = log.mock.calls[0][0] as string;
    expect(line).toMatch(/1 tool call\(s\): locate_position 12ms/);
    expect(line).not.toMatch(/secret|-25\.6/);
  });
});

describe("previous session memory", () => {
  const recordingBrain = () => {
    const turn = vi.fn().mockResolvedValue({ reply: "ok", trace: [] });
    return { brain: { name: "bedrock", turn, reset: vi.fn() } as unknown as Brain, turn };
  };

  it("adds the previous session's summary to the first turn only, and again after a reset", async () => {
    const { brain, turn } = recordingBrain();
    const load = vi.fn().mockResolvedValue("Saved position: N1 2026, km 3.2.");
    const field = createFieldBrain(brain, new FieldDevice(), { loadPreviousSession: load });

    await field.turn("let's continue", { gps: null });
    await field.turn("and then?", { gps: null });
    await field.reset!();
    await field.turn("hi again");

    expect(turn.mock.calls.map((c) => c[1])).toEqual([
      { previousSession: "Saved position: N1 2026, km 3.2.", gps: null },
      { gps: null },
      { previousSession: "Saved position: N1 2026, km 3.2." },
    ]);
    expect(load).toHaveBeenCalledTimes(2);
  });

  it("doesn't load it for typed commands", async () => {
    const { brain } = recordingBrain();
    const load = vi.fn().mockResolvedValue("x");
    await createFieldBrain(brain, new FieldDevice(), { loadPreviousSession: load }).turn("/device");
    expect(load).not.toHaveBeenCalled();
  });

  it("carries on without it when loading fails", async () => {
    const { brain, turn } = recordingBrain();
    const log = vi.fn();
    const field = createFieldBrain(brain, new FieldDevice(), { loadPreviousSession: () => Promise.reject(new Error("server down")), log });

    await field.turn("let's continue");

    expect(turn).toHaveBeenCalledWith("let's continue", undefined);
    expect(log).toHaveBeenCalledWith("previous session not loaded: server down");
  });
});

describe("classifyRoadOpsToolCall", () => {
  const tool = (name: string, readOnlyHint: boolean, properties: Record<string, unknown> = {}): McpTool => ({
    name,
    inputSchema: { type: "object", properties },
    annotations: { readOnlyHint },
  });
  const kind = (t: McpTool, args: Record<string, unknown> = {}) => classifyRoadOpsToolCall(t, args) ?? defaultClassifyToolCall(t, args);

  it("lets photos attach and session bookmarks save freely, and asks before every other write", () => {
    expect(kind(tool("attach_photo", false))).toBe("run");
    expect(kind(tool("save_session_summary", false))).toBe("run");
    expect(kind(tool("find_surveys", true))).toBe("run");
    expect(kind(tool("log_observation", false, { confirm: {} }))).toBe("preview");
    expect(kind(tool("log_observation", false, { confirm: {} }), { confirm: true })).toBe("confirm");
    expect(kind(tool("update_observation", false))).toBe("confirm");
    expect(kind(tool("void_observation", false))).toBe("confirm");
  });
});

describe("uploadPhoto", () => {
  let server: http.Server | undefined;
  afterEach(async () => {
    server?.close();
    if (server) await once(server, "close");
    server = undefined;
  });

  async function fakeServer(status: number, body: unknown) {
    const seen: Array<{ url?: string; auth?: string; type?: string; bytes: number }> = [];
    server = http.createServer(async (req, res) => {
      const chunks: Buffer[] = [];
      for await (const c of req) chunks.push(c as Buffer);
      seen.push({ url: req.url, auth: req.headers.authorization, type: req.headers["content-type"], bytes: Buffer.concat(chunks).length });
      res.writeHead(status, { "content-type": "application/json" });
      res.end(JSON.stringify(body));
    });
    server.listen(0, "127.0.0.1");
    await once(server, "listening");
    return { url: `http://127.0.0.1:${(server.address() as AddressInfo).port}/mcp`, seen };
  }

  async function photoFile(name: string) {
    const path = join(await mkdtemp(join(tmpdir(), "roadops-upload-")), name);
    await writeFile(path, Buffer.from([0xff, 0xd8, 0xff, 0xe0]));
    return path;
  }

  it("posts the image next to /mcp with the inspector's key and returns the id", async () => {
    const { url, seen } = await fakeServer(201, { photoId: "ph-1" });
    expect(await uploadPhoto(url, "key", await photoFile("a.JPG"))).toBe("ph-1");
    expect(seen).toEqual([{ url: "/uploads/photos", auth: "Bearer key", type: "image/jpeg", bytes: 4 }]);
  });

  it("reports the server's error", async () => {
    const { url } = await fakeServer(400, { error: "Not an image." });
    await expect(uploadPhoto(url, "key", await photoFile("a.png"))).rejects.toThrow("The upload failed (400): Not an image.");
  });

  it("refuses files that aren't photos before sending anything", async () => {
    const { url, seen } = await fakeServer(201, { photoId: "x" });
    await expect(uploadPhoto(url, "key", await photoFile("notes.txt"))).rejects.toThrow(/Only .jpg/);
    expect(seen).toHaveLength(0);
  });
});
