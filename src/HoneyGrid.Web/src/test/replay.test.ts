import { describe, expect, it } from 'vitest';
import {
  formatClock,
  framesDuration,
  framesUpTo,
  scaledDelay,
  scheduleFrames,
} from '@/lib/replay';
import type { SessionReplayFrame } from '@/types/api';

const FRAMES: SessionReplayFrame[] = [
  { offsetMs: 0, type: 'o', data: 'banner' },
  { offsetMs: 1000, type: 'i', data: 'ls\r' },
  { offsetMs: 2000, type: 'o', data: 'file.txt' },
  { offsetMs: 4000, type: 'i', data: 'exit\r' },
];

describe('scaledDelay', () => {
  it('scales the gap by the playback speed', () => {
    expect(scaledDelay(2000, 0, 1)).toBe(2000);
    expect(scaledDelay(2000, 0, 2)).toBe(1000);
    expect(scaledDelay(2000, 0, 4)).toBe(500);
  });

  it('clamps frames at or before the resume position to 0', () => {
    expect(scaledDelay(1000, 1000, 1)).toBe(0);
    expect(scaledDelay(500, 1000, 2)).toBe(0);
  });

  it('measures the delay relative to the resume position', () => {
    // 4000ms frame, resumed at 2000ms, at 2x → (4000-2000)/2 = 1000
    expect(scaledDelay(4000, 2000, 2)).toBe(1000);
  });
});

describe('scheduleFrames', () => {
  it('schedules only frames strictly after the resume position with scaled delays', () => {
    const schedule = scheduleFrames(FRAMES, 0, 1);
    // offset 0 is not > 0, so excluded
    expect(schedule.map((s) => s.frame.offsetMs)).toEqual([1000, 2000, 4000]);
    expect(schedule.map((s) => s.delayMs)).toEqual([1000, 2000, 4000]);
  });

  it('applies speed and resume offset together', () => {
    const schedule = scheduleFrames(FRAMES, 1000, 2);
    expect(schedule.map((s) => s.frame.offsetMs)).toEqual([2000, 4000]);
    // (2000-1000)/2 = 500 ; (4000-1000)/2 = 1500
    expect(schedule.map((s) => s.delayMs)).toEqual([500, 1500]);
  });

  it('preserves the original frame index', () => {
    const schedule = scheduleFrames(FRAMES, 1500, 1);
    expect(schedule[0].index).toBe(2);
  });
});

describe('framesUpTo', () => {
  it('returns frames at or before the offset (for seeking)', () => {
    expect(framesUpTo(FRAMES, 2000).map((f) => f.offsetMs)).toEqual([0, 1000, 2000]);
    expect(framesUpTo(FRAMES, 0).map((f) => f.offsetMs)).toEqual([0]);
  });
});

describe('framesDuration', () => {
  it('returns the max of fallback and the largest frame offset', () => {
    expect(framesDuration(FRAMES, 0)).toBe(4000);
    expect(framesDuration(FRAMES, 9000)).toBe(9000);
    expect(framesDuration([], 1234)).toBe(1234);
  });
});

describe('formatClock', () => {
  it('formats ms as m:ss', () => {
    expect(formatClock(0)).toBe('0:00');
    expect(formatClock(5000)).toBe('0:05');
    expect(formatClock(65_000)).toBe('1:05');
    expect(formatClock(-100)).toBe('0:00');
  });
});
