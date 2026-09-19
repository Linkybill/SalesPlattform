import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { UserAuthorisationContext, initializePlatformTheme } from '@hammer2fall/identity-platform-react'
import '@hammer2fall/identity-platform-react/styles.css'
import { ReportEvidenceProvider, MetricTile, EvidenceChart, type ReportEvidence } from '../src/ReportEvidence'
import '../src/styles.css'
import '../src/reportNavigation.css'
import { HookFixture } from './hook-fixture'
import { DailyUsageFixture } from './daily-usage-fixture'

initializePlatformTheme()
const evidence: ReportEvidence = {
  metrics: { won: { key: 'won', label: 'Gewonnener Umsatz', value: 1200, unit: 'money', currency: 'EUR', period: 'Testmonat', source: 'Synthetische Daten', calculation: 'Summe der Testabschlüsse', unavailableReason: null, recordKeys: ['one'] } },
  records: { one: { key: 'one', kind: 'deal', name: 'Testabschluss', customer: 'Testkunde', owner: 'Test Vertrieb', status: 'Gewonnen', date: '2026-09-19', amount: 1200, currency: 'EUR', detail: 'Keine produktiven Daten', externalUrl: null } },
}
createRoot(document.getElementById('root')!).render(<StrictMode>
  <UserAuthorisationContext user={{ displayName: 'Theme-Test' }} syncTenantToUrl={false}
    header={{ applicationName: 'SalesPlattform', applicationSubtitle: 'Gemeinsames Theme', routes: [{ id: 'work', route: '/tests/theme-smoke.html', title: 'Arbeit' }, { id: 'reports', route: '/reports', title: 'Steuerung' }] }}>
    <main className="sales-page reports-page">
      <section className="sales-hero"><div><p className="sales-eyebrow">STEUERUNG</p><h1>Reports</h1><p className="sales-lead">Zahlen verstehen und gemeinsam steuern.</p></div></section>
      <div className="sales-section-browser"><nav className="sales-section-nav" aria-label="Reports"><button className="is-active">Cockpit</button><button>Monatsreport</button><button>Vertriebsteam</button></nav>
        <section className="sales-card"><ReportEvidenceProvider evidence={evidence} generatedAt="2026-09-19T12:00:00Z">
          <MetricTile metricKey="won" /><EvidenceChart prefix="won" />
        </ReportEvidenceProvider><p className="muted">Quelle und Zeitraum sind transparent.</p>
          <p className="message success-message">Synchronisation erfolgreich</p><p className="message error-message">Beispiel einer Fehlermeldung</p>
          <button className="primary-button">Aktualisieren</button>
        </section></div>
      <HookFixture />
      <DailyUsageFixture />
    </main>
  </UserAuthorisationContext>
</StrictMode>)
