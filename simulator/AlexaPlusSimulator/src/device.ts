import type { BrainTurnContext } from "mcp-voice-simulator";

/** Photos older than this aren't "this photo" any more. */
const PHOTO_WINDOW_MS = 30 * 60_000;
const MAX_PHOTOS = 5;

export interface GpsFix {
  latitude: number;
  longitude: number;
  accuracyM?: number;
  at: Date;
}

/**
 * What the inspector's phone knows: its GPS fix and the photos just uploaded.
 * Sent to the agent with every turn as context, so "here" and "this photo"
 * resolve without the model guessing.
 */
export class FieldDevice {
  private gps?: GpsFix;
  private photos: Array<{ id: string; at: Date }> = [];

  constructor(
    private readonly options: { inspector?: string; now?: () => Date } = {},
  ) {}

  private now(): Date {
    return this.options.now?.() ?? new Date();
  }

  setGps(latitude: number, longitude: number, accuracyM?: number): void {
    this.gps = { latitude, longitude, accuracyM, at: this.now() };
  }

  clearGps(): void {
    this.gps = undefined;
  }

  addPhoto(id: string): void {
    this.photos = [{ id, at: this.now() }, ...this.photos.filter((p) => p.id !== id)].slice(0, MAX_PHOTOS);
  }

  context(): BrainTurnContext {
    const now = this.now();
    const recentPhotos = this.photos
      .filter((p) => now.getTime() - p.at.getTime() <= PHOTO_WINDOW_MS)
      .map((p) => ({ id: p.id, minutesAgo: Math.floor((now.getTime() - p.at.getTime()) / 60_000) }));
    return {
      ...(this.options.inspector ? { inspector: this.options.inspector } : {}),
      gps: this.gps
        ? {
            latitude: this.gps.latitude,
            longitude: this.gps.longitude,
            ...(this.gps.accuracyM === undefined ? {} : { accuracyM: this.gps.accuracyM }),
            ageSeconds: Math.round((now.getTime() - this.gps.at.getTime()) / 1000),
          }
        : null,
      ...(recentPhotos.length ? { recentPhotos } : {}),
      localTime: localTime(now),
    };
  }
}

/** e.g. "2026-10-08 14:05", the phone's local time. */
function localTime(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`;
}
