import { useCallback, useEffect, useState } from 'react'
import { useApplicationContext } from '@hammer2fall/identity-platform-react'

type HelloWorldResponse = {
  tenantId: string
  message: string
  database: {
    connected: boolean
    storedRecords: number
    strategy: string
  }
}

export function DashboardPage() {
  const { activeTenantId, authorizedFetch, error: platformError, user } = useApplicationContext()
  const [response, setResponse] = useState<HelloWorldResponse | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const loadHelloWorld = useCallback(async () => {
    if (!user || !activeTenantId) return

    setLoading(true)
    setError(null)
    try {
      const apiResponse = await authorizedFetch('/api/hello-world')
      if (!apiResponse.ok) {
        throw new Error(`HelloWorld-Endpunkt antwortete mit HTTP ${apiResponse.status}.`)
      }

      setResponse(await apiResponse.json() as HelloWorldResponse)
    } catch (reason) {
      setResponse(null)
      setError(reason instanceof Error ? reason.message : 'Der HelloWorld-Endpunkt ist nicht erreichbar.')
    } finally {
      setLoading(false)
    }
  }, [activeTenantId, authorizedFetch, user])

  useEffect(() => {
    void loadHelloWorld()
  }, [loadHelloWorld])

  return (
    <main className="sales-page">
      <section className="sales-hero">
        <div>
          <p className="sales-eyebrow">SALESPLATTFORM · STARTGERÜST</p>
          <h1>Willkommen in der SalesPlattform</h1>
          <p className="sales-lead">
            Das Grundgerüst steht: React im Frontend, ein geschützter Backend-Endpunkt
            und eine tenant-isolierte Datenbank.
          </p>
        </div>
        <span className="status-badge">Bereit für den nächsten Schritt</span>
      </section>

      <section className="sales-card">
        <div className="card-heading">
          <div>
            <p className="sales-eyebrow">BACKEND-CHECK</p>
            <h2>HelloWorld-Endpunkt</h2>
          </div>
          <code>GET /api/hello-world</code>
        </div>

        <p>
          Der Aufruf läuft über den zentralen Identity-Platform-Kontext und verwendet
          den aktuell ausgewählten Tenant.
        </p>

        <button className="primary-button" type="button" onClick={() => void loadHelloWorld()} disabled={loading}>
          {loading ? 'Wird geladen …' : 'Endpunkt aufrufen'}
        </button>

        {(error || platformError) && (
          <div className="message error-message">{error ?? platformError}</div>
        )}

        {response && (
          <div className="message success-message">
            <strong>{response.message}</strong>
            <span>Tenant: <code>{response.tenantId}</code></span>
            <span>
              Datenbank: <code>{response.database.strategy}</code> ·
              {' '}{response.database.storedRecords} gespeicherte Datensätze
            </span>
          </div>
        )}
      </section>
    </main>
  )
}
