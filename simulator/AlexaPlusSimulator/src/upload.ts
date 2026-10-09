import { readFile } from "node:fs/promises";
import { extname } from "node:path";

const CONTENT_TYPES: Record<string, string> = {
  ".jpg": "image/jpeg",
  ".jpeg": "image/jpeg",
  ".png": "image/png",
  ".webp": "image/webp",
};

/** `POST /uploads/photos` on the RoadOps MCP server, next to `/mcp`. Returns the photo id. */
export async function uploadPhoto(mcpUrl: string, bearerToken: string, filePath: string): Promise<string> {
  const contentType = CONTENT_TYPES[extname(filePath).toLowerCase()];
  if (!contentType) throw new Error("Only .jpg, .png and .webp photos can be uploaded.");

  const res = await fetch(new URL("/uploads/photos", mcpUrl), {
    method: "POST",
    headers: { authorization: `Bearer ${bearerToken}`, "content-type": contentType },
    body: await readFile(filePath),
  });
  const body = (await res.json().catch(() => ({}))) as { photoId?: string; error?: string };
  if (!res.ok || !body.photoId) {
    throw new Error(`The upload failed (${res.status})${body.error ? `: ${body.error}` : "."}`);
  }
  return body.photoId;
}
