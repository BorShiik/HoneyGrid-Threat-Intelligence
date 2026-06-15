import React from 'react';

interface CountryFlagProps {
  /** ISO 3166-1 alpha-2 country code (e.g., 'US', 'PL', 'GB') */
  code?: string;
  className?: string;
}

/**
 * Renders a high-quality SVG flag from flagcdn.com instead of relying on
 * OS-level emoji fonts (since Windows does not support country flag emojis natively).
 */
export function CountryFlag({ code, className }: CountryFlagProps) {
  if (!code || code.length !== 2) return <span className={className}>🏴</span>;
  
  const cc = code.toLowerCase();
  return (
    <img
      src={`https://flagcdn.com/${cc}.svg`}
      alt={code.toUpperCase()}
      title={code.toUpperCase()}
      className={`inline-block h-[1em] w-[1.33em] object-cover align-baseline shadow-sm ${className || ''}`}
      loading="lazy"
    />
  );
}
