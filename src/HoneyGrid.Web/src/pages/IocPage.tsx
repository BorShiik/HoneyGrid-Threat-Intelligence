import { useMemo, useState } from 'react';
import { motion } from 'framer-motion';
import { Check, Copy, Download, Search } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
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
import { useIocsStix } from '@/api/queries';
import type { StixBundle, StixObject } from '@/types/api';

/** Polish labels for the STIX object types we surface. */
const TYPE_LABELS: Record<string, string> = {
  indicator: 'Wskaźnik',
  'attack-pattern': 'Wzorzec ataku',
  'threat-actor': 'Aktor zagrożeń',
  relationship: 'Relacja',
  identity: 'Tożsamość',
};

const TYPE_FILTERS = ['all', 'indicator', 'attack-pattern', 'relationship', 'identity'] as const;
type TypeFilter = (typeof TYPE_FILTERS)[number];

function typeBadgeVariant(type: string): 'critical' | 'high' | 'medium' | 'secondary' | 'outline' {
  switch (type) {
    case 'indicator':
      return 'critical';
    case 'attack-pattern':
      return 'high';
    case 'threat-actor':
      return 'medium';
    case 'relationship':
      return 'outline';
    default:
      return 'secondary';
  }
}

function downloadBundle(bundle: StixBundle) {
  const blob = new Blob([JSON.stringify(bundle, null, 2)], { type: 'application/json' });
  const url = URL.createObjectURL(blob);
  const date = new Date().toISOString().slice(0, 10);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = `honeygrid-stix-${date}.json`;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  URL.revokeObjectURL(url);
}

function CopyButton({ value }: { value: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <Button
      size="icon"
      variant="ghost"
      className="size-7"
      aria-label="Kopiuj"
      title="Kopiuj"
      onClick={async () => {
        try {
          await navigator.clipboard?.writeText(value);
          setCopied(true);
          window.setTimeout(() => setCopied(false), 1200);
        } catch {
          /* clipboard unavailable — no-op */
        }
      }}
    >
      {copied ? <Check className="text-severity-low" /> : <Copy />}
    </Button>
  );
}

function SummaryStat({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded-lg border bg-card px-4 py-3">
      <div className="text-2xl font-bold tabular-nums">{value}</div>
      <div className="text-xs text-muted-foreground">{label}</div>
    </div>
  );
}

export function IocPage() {
  const { data, isPending, isError } = useIocsStix();
  const [typeFilter, setTypeFilter] = useState<TypeFilter>('all');
  const [search, setSearch] = useState('');

  const objects = useMemo<StixObject[]>(() => data?.objects ?? [], [data]);

  const counts = useMemo(() => {
    const byType = (t: string) => objects.filter((o) => o.type === t).length;
    return {
      indicators: byType('indicator'),
      patterns: byType('attack-pattern'),
      actors: byType('threat-actor'),
      total: objects.length,
    };
  }, [objects]);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return objects.filter((o) => {
      if (typeFilter !== 'all' && o.type !== typeFilter) return false;
      if (q) {
        const haystack = `${o.pattern ?? ''} ${String(o.name ?? '')} ${(o.labels ?? []).join(' ')}`;
        if (!haystack.toLowerCase().includes(q)) return false;
      }
      return true;
    });
  }, [objects, typeFilter, search]);

  return (
    <motion.section
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.25 }}
      className="space-y-4"
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-2xl font-bold tracking-tight">Wskaźniki IoC (STIX)</h2>
          <p className="mt-1 max-w-2xl text-muted-foreground">
            Wskaźniki kompromitacji w formacie STIX 2.1: złośliwe adresy IP, skróty plików (hash),
            wzorce ataków i relacje — gotowe do importu w systemach SIEM.
          </p>
        </div>
        <Button
          onClick={() => data && downloadBundle(data)}
          disabled={!data}
          data-testid="export-stix"
        >
          <Download /> Eksportuj bundle STIX
        </Button>
      </div>

      {isPending && (
        <div className="space-y-3">
          <Skeleton className="h-20 w-full" />
          <Skeleton className="h-64 w-full" />
        </div>
      )}

      {isError && (
        <p className="rounded-lg border border-destructive/40 bg-destructive/10 p-4 text-sm text-destructive">
          Nie udało się pobrać kanału STIX. Spróbuj ponownie później.
        </p>
      )}

      {data && (
        <>
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            <SummaryStat label="Wskaźniki" value={counts.indicators} />
            <SummaryStat label="Wzorce ataków" value={counts.patterns} />
            <SummaryStat label="Aktorzy zagrożeń" value={counts.actors} />
            <SummaryStat label="Obiekty łącznie" value={counts.total} />
          </div>

          <Card>
            <CardHeader className="gap-3 pb-3">
              <CardTitle className="text-base">Obiekty STIX</CardTitle>
              <div className="flex flex-wrap items-center gap-2">
                <div className="flex flex-wrap gap-1">
                  {TYPE_FILTERS.map((t) => (
                    <Button
                      key={t}
                      size="sm"
                      variant={typeFilter === t ? 'default' : 'outline'}
                      onClick={() => setTypeFilter(t)}
                      aria-pressed={typeFilter === t}
                    >
                      {t === 'all' ? 'Wszystkie' : TYPE_LABELS[t]}
                    </Button>
                  ))}
                </div>
                <div className="relative ml-auto min-w-[200px] flex-1 sm:max-w-xs">
                  <Search className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
                  <input
                    type="search"
                    value={search}
                    onChange={(e) => setSearch(e.target.value)}
                    placeholder="Szukaj we wzorcu / nazwie…"
                    aria-label="Szukaj wzorca"
                    className="h-9 w-full rounded-md border border-input bg-transparent pl-8 pr-3 text-sm shadow-sm focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
                  />
                </div>
              </div>
            </CardHeader>
            <CardContent className="p-0">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead className="w-32">Typ</TableHead>
                    <TableHead>Wzorzec / nazwa</TableHead>
                    <TableHead className="w-48">Etykiety</TableHead>
                    <TableHead className="w-32">Utworzono</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {filtered.length === 0 && (
                    <TableRow>
                      <TableCell colSpan={4} className="py-8 text-center text-muted-foreground">
                        Brak obiektów pasujących do filtrów.
                      </TableCell>
                    </TableRow>
                  )}
                  {filtered.map((o) => (
                    <TableRow key={o.id}>
                      <TableCell>
                        <Badge variant={typeBadgeVariant(o.type)}>
                          {TYPE_LABELS[o.type] ?? o.type}
                        </Badge>
                      </TableCell>
                      <TableCell>
                        {o.pattern ? (
                          <div className="flex items-center gap-1.5">
                            <code
                              className={cn(
                                'rounded bg-muted px-1.5 py-0.5 font-mono text-xs',
                                'whitespace-pre-wrap break-all',
                              )}
                            >
                              {o.pattern}
                            </code>
                            <CopyButton value={o.pattern} />
                          </div>
                        ) : (
                          <span className="text-sm">{String(o.name ?? '—')}</span>
                        )}
                      </TableCell>
                      <TableCell>
                        <div className="flex flex-wrap gap-1">
                          {(o.labels ?? []).map((l) => (
                            <Badge key={l} variant="outline" className="font-mono text-[11px]">
                              {l}
                            </Badge>
                          ))}
                        </div>
                      </TableCell>
                      <TableCell className="font-mono text-xs text-muted-foreground">
                        {new Date(o.created).toLocaleDateString('pl-PL')}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        </>
      )}
    </motion.section>
  );
}
