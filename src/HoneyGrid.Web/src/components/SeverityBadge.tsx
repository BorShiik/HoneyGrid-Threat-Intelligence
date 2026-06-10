import { Badge } from '@/components/ui/badge';
import type { Severity } from '@/types/api';

/** Polish labels for the threat severity scale. */
export const SEVERITY_LABELS: Record<Severity, string> = {
  critical: 'Krytyczny',
  high: 'Wysoki',
  medium: 'Średni',
  low: 'Niski',
};

export function SeverityBadge({ severity }: { severity: Severity }) {
  return <Badge variant={severity}>{SEVERITY_LABELS[severity]}</Badge>;
}
