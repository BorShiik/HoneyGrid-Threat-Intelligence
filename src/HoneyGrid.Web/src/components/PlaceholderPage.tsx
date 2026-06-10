import { motion } from 'framer-motion';
import { Badge } from '@/components/ui/badge';

interface PlaceholderPageProps {
  title: string;
  description: string;
  /** Sprint number in which the feature lands, e.g. 2 → "W budowie — Tydzień 2". */
  week: number;
  children?: React.ReactNode;
}

export function PlaceholderPage({ title, description, week, children }: PlaceholderPageProps) {
  return (
    <motion.section
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.25 }}
      className="space-y-4"
    >
      <div className="flex items-center gap-3">
        <h2 className="text-2xl font-bold tracking-tight">{title}</h2>
        <Badge variant="secondary" className="font-mono">
          W budowie — Tydzień {week}
        </Badge>
      </div>
      <p className="max-w-2xl text-muted-foreground">{description}</p>
      {children}
    </motion.section>
  );
}
