/**
 * Minimal squarified treemap layout (Bruls, Huizing & van Wijk).
 * Dependency-free; returns absolutely-positioned rectangles for SVG/HTML.
 */

export interface TreemapInput {
  value: number;
  [key: string]: unknown;
}

export interface TreemapRect<T> {
  item: T;
  x: number;
  y: number;
  width: number;
  height: number;
}

interface Box {
  x: number;
  y: number;
  width: number;
  height: number;
}

function worst(row: number[], length: number, total: number, area: number): number {
  if (row.length === 0 || length === 0) return Infinity;
  const scale = area / total;
  const max = Math.max(...row) * scale;
  const min = Math.min(...row) * scale;
  const s = (row.reduce((a, b) => a + b, 0) * scale) ** 2;
  return Math.max((length * length * max) / s, s / (length * length * min));
}

export function squarify<T extends TreemapInput>(
  items: T[],
  width: number,
  height: number,
): TreemapRect<T>[] {
  const positive = items.filter((i) => i.value > 0).sort((a, b) => b.value - a.value);
  const total = positive.reduce((s, i) => s + i.value, 0);
  if (total <= 0) return [];

  const rects: TreemapRect<T>[] = [];
  const area = width * height;
  // Work in value-space scaled to the available pixel area.
  const scaled = positive.map((i) => ({ item: i, value: (i.value / total) * area }));

  let box: Box = { x: 0, y: 0, width, height };
  let remaining = scaled.slice();

  function layoutRow(row: typeof scaled, b: Box, horizontal: boolean): Box {
    const rowSum = row.reduce((s, r) => s + r.value, 0);
    if (horizontal) {
      const rowHeight = rowSum / b.width;
      let x = b.x;
      for (const r of row) {
        const w = r.value / rowHeight;
        rects.push({ item: r.item, x, y: b.y, width: w, height: rowHeight });
        x += w;
      }
      return { x: b.x, y: b.y + rowHeight, width: b.width, height: b.height - rowHeight };
    }
    const rowWidth = rowSum / b.height;
    let y = b.y;
    for (const r of row) {
      const h = r.value / rowWidth;
      rects.push({ item: r.item, x: b.x, y, width: rowWidth, height: h });
      y += h;
    }
    return { x: b.x + rowWidth, y: b.y, width: b.width - rowWidth, height: b.height };
  }

  let row: typeof scaled = [];
  while (remaining.length > 0) {
    const horizontal = box.width >= box.height;
    const length = horizontal ? box.width : box.height;
    const next = remaining[0];
    const rowValues = row.map((r) => r.value);
    const withNext = [...rowValues, next.value];
    const rowArea = horizontal ? box.width * box.height : box.width * box.height;

    if (row.length === 0 || worst(withNext, length, rowArea, rowArea) <= worst(rowValues, length, rowArea, rowArea)) {
      row.push(next);
      remaining = remaining.slice(1);
    } else {
      box = layoutRow(row, box, horizontal);
      row = [];
    }
  }
  if (row.length > 0) {
    layoutRow(row, box, box.width >= box.height);
  }
  return rects;
}
