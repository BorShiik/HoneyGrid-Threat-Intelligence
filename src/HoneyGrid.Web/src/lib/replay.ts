import type { SessionReplayFrame } from '@/types/api';

/**
 * Pure timing helpers for the Session Replay player.
 *
 * These functions are deliberately free of any xterm / DOM dependency so the
 * playback logic can be unit-tested in isolation (the heavy terminal is mocked
 * in tests). The player component schedules `setTimeout` writes based on the
 * delays computed here.
 */

export type PlaybackSpeed = 1 | 2 | 4;

export interface ScheduledFrame {
  /** Index of the frame in the original `frames` array. */
  index: number;
  /** Raw frame payload. */
  frame: SessionReplayFrame;
  /**
   * Wall-clock delay (ms) from "now" until this frame should be written,
   * already adjusted for playback speed and the resume position.
   */
  delayMs: number;
}

/**
 * Compute the scaled wall-clock delay for a single offset.
 * Frames at or before `fromOffsetMs` are clamped to 0 so a resume/seek does
 * not schedule them in the past.
 */
export function scaledDelay(offsetMs: number, fromOffsetMs: number, speed: PlaybackSpeed): number {
  const delta = offsetMs - fromOffsetMs;
  if (delta <= 0) return 0;
  return delta / speed;
}

/**
 * Build the playback schedule for all frames whose offset is strictly greater
 * than `fromOffsetMs` (i.e. the frames still to be played from the current
 * position). Frames are returned in their original order with speed-adjusted
 * delays relative to the resume point.
 */
export function scheduleFrames(
  frames: readonly SessionReplayFrame[],
  fromOffsetMs: number,
  speed: PlaybackSpeed,
): ScheduledFrame[] {
  const result: ScheduledFrame[] = [];
  for (let index = 0; index < frames.length; index += 1) {
    const frame = frames[index];
    if (frame.offsetMs <= fromOffsetMs) continue;
    result.push({
      index,
      frame,
      delayMs: scaledDelay(frame.offsetMs, fromOffsetMs, speed),
    });
  }
  return result;
}

/**
 * Frames that should already be on screen at a given offset (offset <= position).
 * Used to "fast-forward" the terminal contents when seeking via the scrubber.
 */
export function framesUpTo(
  frames: readonly SessionReplayFrame[],
  offsetMs: number,
): SessionReplayFrame[] {
  return frames.filter((f) => f.offsetMs <= offsetMs);
}

/** Total duration implied by the frames (max offset), falling back to `fallbackMs`. */
export function framesDuration(
  frames: readonly SessionReplayFrame[],
  fallbackMs: number,
): number {
  if (frames.length === 0) return fallbackMs;
  return Math.max(fallbackMs, ...frames.map((f) => f.offsetMs));
}

/** Format a millisecond duration as `m:ss`. */
export function formatClock(ms: number): string {
  const totalSeconds = Math.max(0, Math.floor(ms / 1000));
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${minutes}:${seconds.toString().padStart(2, '0')}`;
}
