import { WorklistWidget } from './WorklistWidget'

export function DashboardPage() {
  return (
    <main className="sales-page reports-page">
      <section className="sales-hero">
        <div>
          <p className="sales-eyebrow">SALESPLATTFORM · ARBEITSLISTE</p>
          <h1>Arbeit</h1>
          <p className="sales-lead">Was ist als Nächstes zu tun? Vorgänge links nach Thema auswählen, Termine prüfen und direkt im CRM weiterarbeiten. Kennzahlen und Reports findest du unter Steuerung.</p>
        </div>
      </section>
      <WorklistWidget />
    </main>
  )
}
