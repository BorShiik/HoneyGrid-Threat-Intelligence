import { PlaceholderPage } from '@/components/PlaceholderPage';

export function IocPage() {
  return (
    <PlaceholderPage
      title="Wskaźniki IoC (STIX)"
      description="Eksport wskaźników kompromitacji w formacie STIX 2.1: złośliwe adresy IP, skróty plików (hash) i wzorce ataków gotowe do importu w systemach SIEM."
      week={6}
    />
  );
}
