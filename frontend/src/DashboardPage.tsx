import { WorklistWidget } from './WorklistWidget'

export function DashboardPage() {
  return (
    <main className="sales-page">
      <section className="sales-hero">
        <div>
          <p className="sales-eyebrow">SALESPLATTFORM · ARBEITSLISTE</p>
          <h1>Arbeitsliste</h1>
          <p className="sales-lead">Die wichtigsten offenen Vorgänge werden aus den CRM-Daten priorisiert und direkt als nächste Arbeitsschritte angezeigt.</p>
        </div>
      </section>
      <WorklistWidget />
    </main>
  )
}
