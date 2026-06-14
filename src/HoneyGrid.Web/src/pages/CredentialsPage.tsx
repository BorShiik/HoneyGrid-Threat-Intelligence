import { useMemo } from 'react';
import { motion } from 'framer-motion';
import { KeyRound } from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { cn } from '@/lib/utils';
import { formatInt } from '@/lib/format';
import { squarify } from '@/lib/treemap';
import { useStatsCredentials } from '@/api/queries';
import type { CredentialStat } from '@/types/api';

const TM_W = 720;
const TM_H = 300;

function RankBars({
  title,
  rows,
  max,
}: {
  title: string;
  rows: { label: string; count: number }[];
  max: number;
}) {
  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-base">{title}</CardTitle>
      </CardHeader>
      <CardContent className="space-y-2">
        {rows.map((r) => (
          <div key={r.label} className="space-y-1">
            <div className="flex items-center justify-between text-sm">
              <code className="font-mono">{r.label}</code>
              <span className="font-mono tabular-nums text-muted-foreground">{formatInt(r.count)}</span>
            </div>
            <div className="h-2 overflow-hidden rounded-full bg-muted">
              <div className="h-full rounded-full bg-primary" style={{ width: `${(r.count / max) * 100}%` }} />
            </div>
          </div>
        ))}
      </CardContent>
    </Card>
  );
}

export function CredentialsPage() {
  const { data, isPending, isError } = useStatsCredentials();

  const pairRects = useMemo(() => {
    if (!data) return [];
    return squarify(
      data.topPairs.map((p) => ({ ...p, value: p.count })),
      TM_W,
      TM_H,
    );
  }, [data]);

  const maxUser = Math.max(1, ...(data?.topUsernames ?? []).map((u) => u.count));
  const maxPass = Math.max(1, ...(data?.topPasswords ?? []).map((p) => p.count));

  return (
    <motion.section
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.25 }}
      className="space-y-4"
    >
      <div>
        <h2 className="flex items-center gap-2 text-2xl font-bold tracking-tight">
          <KeyRound className="size-6 text-primary" /> Analiza poświadczeń
        </h2>
        <p className="mt-1 max-w-2xl text-muted-foreground">
          Najczęściej próbowane loginy, hasła i pary z ataków brute-force. Mapa kafelkowa skaluje
          rozmiar pary do liczby prób.
        </p>
      </div>

      {isError && (
        <p className="rounded-lg border border-destructive/40 bg-destructive/10 p-4 text-sm text-destructive">
          Nie udało się pobrać statystyk poświadczeń.
        </p>
      )}

      {isPending ? (
        <Skeleton className="aspect-[2.4/1] w-full" />
      ) : data ? (
        <>
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            <Tile label="Próby łącznie" value={formatInt(data.totalAttempts)} />
            <Tile label="Unikalne loginy (top)" value={formatInt(data.topUsernames.length)} />
            <Tile label="Unikalne hasła (top)" value={formatInt(data.topPasswords.length)} />
            <Tile label="Najczęstsza para" value={`${data.topPairs[0]?.username}:${data.topPairs[0]?.password}`} />
          </div>

          <Card className="overflow-hidden">
            <CardHeader className="pb-2">
              <CardTitle className="text-base">Mapa kafelkowa par login/hasło</CardTitle>
            </CardHeader>
            <CardContent className="p-0">
              <svg viewBox={`0 0 ${TM_W} ${TM_H}`} className="w-full" role="img" aria-label="Mapa kafelkowa par">
                {pairRects.map((rect, i) => {
                  const pair = rect.item as CredentialStat & { value: number };
                  const intensity = 0.25 + (i === 0 ? 0.55 : Math.max(0, 0.5 - i * 0.1));
                  const showLabel = rect.width > 70 && rect.height > 34;
                  return (
                    <g key={`${pair.username}:${pair.password}`}>
                      <rect
                        x={rect.x + 1}
                        y={rect.y + 1}
                        width={Math.max(0, rect.width - 2)}
                        height={Math.max(0, rect.height - 2)}
                        rx={4}
                        className="fill-primary"
                        style={{ opacity: intensity }}
                      />
                      {showLabel && (
                        <>
                          <text x={rect.x + 10} y={rect.y + 22} className="fill-primary-foreground text-[13px] font-semibold">
                            {pair.username}:{pair.password}
                          </text>
                          <text x={rect.x + 10} y={rect.y + 40} className="fill-primary-foreground/80 text-[11px]">
                            {formatInt(pair.count)} prób
                          </text>
                        </>
                      )}
                    </g>
                  );
                })}
              </svg>
            </CardContent>
          </Card>

          <div className="grid gap-4 lg:grid-cols-2">
            <RankBars
              title="Najczęstsze loginy"
              max={maxUser}
              rows={data.topUsernames.map((u) => ({ label: u.username, count: u.count }))}
            />
            <RankBars
              title="Najczęstsze hasła"
              max={maxPass}
              rows={data.topPasswords.map((p) => ({ label: p.password, count: p.count }))}
            />
          </div>

          <Card>
            <CardHeader className="pb-2">
              <CardTitle className="text-base">Najczęstsze pary login / hasło</CardTitle>
            </CardHeader>
            <CardContent className="p-0">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Login</TableHead>
                    <TableHead>Hasło</TableHead>
                    <TableHead className="w-32 text-right">Próby</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {data.topPairs.map((p) => (
                    <TableRow key={`${p.username}:${p.password}`}>
                      <TableCell className="font-mono">{p.username}</TableCell>
                      <TableCell className="font-mono">{p.password}</TableCell>
                      <TableCell className={cn('text-right font-mono tabular-nums')}>
                        {formatInt(p.count)}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        </>
      ) : null}
    </motion.section>
  );
}

function Tile({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border bg-card px-4 py-3">
      <div className="truncate text-lg font-bold tabular-nums">{value}</div>
      <div className="text-xs text-muted-foreground">{label}</div>
    </div>
  );
}
