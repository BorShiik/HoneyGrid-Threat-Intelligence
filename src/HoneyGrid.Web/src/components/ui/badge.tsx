import * as React from 'react';
import { cva, type VariantProps } from 'class-variance-authority';
import { cn } from '@/lib/utils';

const badgeVariants = cva(
  'inline-flex items-center rounded-md border px-2.5 py-0.5 text-xs font-semibold transition-colors focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2',
  {
    variants: {
      variant: {
        default: 'border-transparent bg-primary text-primary-foreground shadow',
        secondary: 'border-transparent bg-secondary text-secondary-foreground',
        destructive: 'border-transparent bg-destructive text-destructive-foreground shadow',
        outline: 'text-foreground',
        critical:
          'border-transparent bg-severity-critical/15 text-severity-critical ring-1 ring-inset ring-severity-critical/40',
        high: 'border-transparent bg-severity-high/15 text-severity-high ring-1 ring-inset ring-severity-high/40',
        medium:
          'border-transparent bg-severity-medium/15 text-severity-medium ring-1 ring-inset ring-severity-medium/40',
        low: 'border-transparent bg-severity-low/15 text-severity-low ring-1 ring-inset ring-severity-low/40',
      },
    },
    defaultVariants: {
      variant: 'default',
    },
  },
);

export interface BadgeProps
  extends React.HTMLAttributes<HTMLDivElement>, VariantProps<typeof badgeVariants> {}

function Badge({ className, variant, ...props }: BadgeProps) {
  return <div className={cn(badgeVariants({ variant }), className)} {...props} />;
}

export { Badge, badgeVariants };
