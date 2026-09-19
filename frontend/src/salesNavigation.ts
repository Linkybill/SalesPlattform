export const workThemes = [
  { key: 'renewals', title: 'Auslaufende Produkte', rules: ['R-06'] },
  { key: 'dormant', title: 'Schlummernde Leads', rules: ['R-07', 'R-13', 'R-14'] },
  { key: 'followups', title: 'Wiedervorlagen', rules: ['R-01', 'R-02', 'R-03', 'R-04', 'R-05', 'R-08', 'R-09', 'R-10', 'R-11', 'R-15', 'R-16', 'R-17', 'R-18'] },
  { key: 'meetings', title: 'Meeting Report', rules: ['R-12'] },
] as const

export const ruleTitles: Record<string, string> = {
  'R-01': 'Kontakt erneut versuchen', 'R-02': 'Zwischen-E-Mail', 'R-03': 'Weitere Kontaktversuche',
  'R-04': 'Letzter Kontakt / Abschluss prüfen', 'R-05': 'Hängende Deals nachfassen', 'R-06': 'Verlängerungen',
  'R-07': 'Inaktive Kontakte reaktivieren', 'R-08': 'Zuständigkeit klären', 'R-09': 'Neue Leads kontaktieren',
  'R-10': 'Weitere Produkte anbieten', 'R-11': 'Zielabweichung besprechen', 'R-12': 'Verschobene Termine klären',
  'R-13': 'Bestandskunden kontaktieren', 'R-14': 'Verlorene Abschlüsse reaktivieren',
  'R-15': 'Servicefälle bearbeiten', 'R-16': 'Angebote nachfassen', 'R-17': 'Lieferungen klären', 'R-18': 'Zahlungen klären',
}

export function themeForRule(rule: string | null) {
  return workThemes.find(theme => (theme.rules as readonly string[]).includes(rule ?? ''))?.key ?? 'other'
}

export const reportSections = [
  { key: 'cockpit', title: 'Cockpit', reports: ['cockpit'] },
  { key: 'month', title: 'Monatsreport', reports: ['cockpit', 'analysis'], timeframe: 'month' },
  { key: 'team', title: 'Vertriebsteam', reports: ['team'] },
  { key: 'year', title: 'Jahresreport', reports: ['cockpit', 'analysis'], timeframe: 'year' },
  { key: 'lifetime', title: 'Allgemein · Lifetime', reports: ['cockpit', 'analysis', 'team'], timeframe: 'lifetime' },
  { key: 'analysis', title: 'Analyse', reports: ['analysis'] },
  { key: 'customers', title: 'Kundenstamm & Karte', reports: ['customers'] },
  { key: 'goals', title: 'Ziele & Pace', reports: ['goals'] },
  { key: 'cleanup', title: 'Aufräumen', reports: ['cleanup'] },
  { key: 'service', title: 'Servicefälle', reports: ['service'] },
  { key: 'commercial', title: 'Angebote, Aufträge & Rechnungen', reports: ['commercial'] },
] satisfies { key: string; title: string; reports: string[]; timeframe?: string }[]

// Keep the tenant's configured containers and explanatory text around the
// selected reports. Hidden/unauthorized ancestors never expose descendants.
export function filterReportLayout(nodes: LayoutNode[], keys: string[]): LayoutNode[] {
  const matching = new Map<string, LayoutNode>()
  for (const node of nodes.filter(n => n.visible && n.allowed)) {
    if (node.type === 'report' && keys.includes(node.reportKey ?? '')) matching.set(node.id, { ...node, columns: 12 })
    else if (node.children.length) {
      const children = filterReportLayout(node.children, keys)
      if (children.length) matching.set(node.id, { ...node, columns: 12, children })
    }
  }
  if (!matching.size) return []
  return nodes.filter(n => n.visible && n.allowed).flatMap(n => matching.has(n.id)
    ? [matching.get(n.id)!] : ['heading', 'text'].includes(n.type) ? [n] : [])
}
import type { LayoutNode } from './DashboardContentEditor'
