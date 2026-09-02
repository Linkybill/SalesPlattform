import { useCallback, useEffect, useState } from 'react'
import {
  useApplicationContext,
} from '@hammer2fall/identity-platform-react'

type CurrentUser = {
  subject: string | null
  email: string | null
  displayName: string | null
}

type CrmOwnerOption = {
  id: string
  displayName: string
  email: string | null
  isActive: boolean
}

type OwnerMapping = {
  platformUserSubject: string | null
  platformUserEmail: string
  crmOwnerId: string
  crmOwnerName: string
  crmOwnerEmail: string | null
  updatedAt: string
  updatedBy: string | null
}

type OwnerMappingResponse = {
  currentUser: CurrentUser
  crmOwners: CrmOwnerOption[]
  mappings: OwnerMapping[]
}

type ApiError = { message?: string; detail?: string; title?: string }

export function OwnerMappingPage() {
  const {
    activeTenant,
    activeTenantId,
    authorizedFetch,
    error: platformError,
    user,
  } = useApplicationContext()
  const [response, setResponse] = useState<OwnerMappingResponse | null>(null)
  const [platformUserEmail, setPlatformUserEmail] = useState('')
  const [platformUserSubject, setPlatformUserSubject] = useState<string | null>(null)
  const [crmOwnerId, setCrmOwnerId] = useState('')
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)

  const canManage = activeTenant?.isTenantAdmin === true

  const readPayload = async <T,>(apiResponse: Response): Promise<T | null> => {
    const body = await apiResponse.text()
    if (!body.trim()) return null
    try {
      return JSON.parse(body) as T
    } catch {
      return null
    }
  }

  const getApiError = (payload: ApiError | null, fallback: string) =>
    payload?.message ?? payload?.detail ?? payload?.title ?? fallback

  const selectCurrentUser = (payload: OwnerMappingResponse) => {
    const email = payload.currentUser.email ?? ''
    const currentMapping = payload.mappings.find(mapping =>
      (payload.currentUser.subject && mapping.platformUserSubject === payload.currentUser.subject)
      || mapping.platformUserEmail.toLowerCase() === email.toLowerCase())
    setPlatformUserEmail(email)
    setPlatformUserSubject(payload.currentUser.subject)
    setCrmOwnerId(currentMapping?.crmOwnerId ?? '')
  }

  const load = useCallback(async () => {
    if (!user || !activeTenantId || !canManage) return
    setLoading(true)
    setError(null)
    try {
      const mappingResponse = await authorizedFetch('/api/owner-mappings')
      const mappingPayload = await readPayload<OwnerMappingResponse & ApiError>(mappingResponse)
      if (!mappingResponse.ok || !mappingPayload) {
        throw new Error(getApiError(mappingPayload, `Benutzerzuordnungen antworteten mit HTTP ${mappingResponse.status}.`))
      }
      setResponse(mappingPayload)
      selectCurrentUser(mappingPayload)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Benutzerzuordnungen konnten nicht geladen werden.')
    } finally {
      setLoading(false)
    }
  }, [activeTenantId, authorizedFetch, canManage, user])

  useEffect(() => {
    void load()
  }, [load])

  const save = async () => {
    setSaving(true)
    setError(null)
    setMessage(null)
    try {
      const apiResponse = await authorizedFetch('/api/owner-mappings', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify({
          platformUserEmail,
          platformUserSubject,
          crmOwnerId: crmOwnerId || null,
        }),
      })
      const payload = await readPayload<OwnerMappingResponse & ApiError>(apiResponse)
      if (!apiResponse.ok || !payload) {
        throw new Error(getApiError(payload, `Zuordnung konnte nicht gespeichert werden (HTTP ${apiResponse.status}).`))
      }
      setResponse(payload)
      setMessage(`Zuordnung für ${platformUserEmail} wurde gespeichert.`)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Die Zuordnung konnte nicht gespeichert werden.')
    } finally {
      setSaving(false)
    }
  }

  const remove = async (mapping: OwnerMapping) => {
    setError(null)
    setMessage(null)
    try {
      const apiResponse = await authorizedFetch(`/api/owner-mappings/${encodeURIComponent(mapping.platformUserEmail)}`, {
        method: 'DELETE',
        headers: { Accept: 'application/json' },
      })
      const payload = await readPayload<OwnerMappingResponse & ApiError>(apiResponse)
      if (!apiResponse.ok || !payload) {
        throw new Error(getApiError(payload, `Zuordnung konnte nicht entfernt werden (HTTP ${apiResponse.status}).`))
      }
      setResponse(payload)
      if (response && response.currentUser.email?.toLowerCase() === mapping.platformUserEmail.toLowerCase()) {
        setCrmOwnerId('')
      }
      setMessage(`Zuordnung für ${mapping.platformUserEmail} wurde entfernt.`)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Die Zuordnung konnte nicht entfernt werden.')
    }
  }

  if (!canManage) {
    return (
      <main className="sales-page">
        <section className="sales-card">
          <p className="sales-eyebrow">EINSTELLUNGEN · BENUTZERZUORDNUNG</p>
          <h1>Keine Berechtigung</h1>
          <p className="sales-card-copy">Diese Einstellung ist ausschließlich für Tenant-Administratoren verfügbar.</p>
        </section>
      </main>
    )
  }

  return (
    <main className="sales-page">
      <section className="sales-hero">
        <div>
          <p className="sales-eyebrow">EINSTELLUNGEN · CRM-ZUORDNUNG</p>
          <h1>CRM-Benutzerzuordnung</h1>
          <p className="sales-lead">
            Ordnen Sie Plattform-Benutzer ihrem CRM-Besitzer zu. Die Arbeitsliste
            verwendet diese Zuordnung tenantbezogen und sicher.
          </p>
        </div>
        <button className="secondary-button" type="button" onClick={() => void load()} disabled={loading || saving}>
          {loading ? 'Wird geladen …' : 'Zuordnungen aktualisieren'}
        </button>
      </section>

      {(error || platformError) && <div className="message error-message">{error ?? platformError}</div>}
      {message && <div className="message success-message">{message}</div>}

      <section className="sales-card owner-mapping-card">
        <div className="card-heading">
          <div>
            <p className="sales-eyebrow">NEUE ZUORDNUNG</p>
            <h2>Plattform-Benutzer mit CRM-Besitzer verbinden</h2>
          </div>
          {response?.currentUser.email && <span className="worklist-refresh">Angemeldet: {response.currentUser.email}</span>}
        </div>
        <p className="sales-card-copy">
          Für deinen eigenen Test ist die angemeldete Plattform-E-Mail bereits vorausgefüllt.
          Für andere Benutzer kann die Adresse manuell eingetragen werden.
        </p>

        <div className="owner-mapping-form">
          <label>
            Plattform-E-Mail
            <input
              type="email"
              value={platformUserEmail}
              onChange={event => {
                setPlatformUserEmail(event.target.value)
                if (response?.currentUser.email?.toLowerCase() !== event.target.value.trim().toLowerCase()) setPlatformUserSubject(null)
              }}
              placeholder="z. B. max.mustermann@firma.de"
              disabled={saving}
            />
          </label>
          <label>
            CRM-Besitzer
            <select value={crmOwnerId} onChange={event => setCrmOwnerId(event.target.value)} disabled={saving || !response}>
              <option value="">CRM-Besitzer auswählen …</option>
              {response?.crmOwners.map(owner => (
                <option key={owner.id} value={owner.id} disabled={!owner.isActive}>
                  {owner.displayName}{owner.email ? ` · ${owner.email}` : ''}{!owner.isActive ? ' · inaktiv' : ''}
                </option>
              ))}
            </select>
          </label>
        </div>
        <div className="button-row">
          <button className="primary-button" type="button" onClick={() => void save()} disabled={saving || loading || !platformUserEmail || !crmOwnerId}>
            {saving ? 'Wird gespeichert …' : 'Zuordnung speichern'}
          </button>
        </div>
      </section>

      <section className="sales-card owner-mapping-card">
        <div className="card-heading">
          <div>
            <p className="sales-eyebrow">AKTIVE ZUORDNUNGEN</p>
            <h2>{response?.mappings.length ?? 0} Zuordnungen</h2>
          </div>
        </div>
        {!response && loading && <p className="worklist-empty">Zuordnungen werden geladen …</p>}
        {response?.mappings.length === 0 && <div className="worklist-empty"><strong>Noch keine Zuordnungen</strong><span>Lege oben eine Testzuordnung an.</span></div>}
        {response && response.mappings.length > 0 && (
          <div className="owner-mapping-list">
            {response.mappings.map(mapping => (
              <article className="owner-mapping-row" key={mapping.platformUserEmail}>
                <div>
                  <strong>{mapping.platformUserEmail}</strong>
                  <small>{mapping.platformUserSubject ? 'Stabile Benutzer-ID hinterlegt' : 'Zuordnung über E-Mail-Adresse'}</small>
                </div>
                <div>
                  <strong>{mapping.crmOwnerName}</strong>
                  <small>{mapping.crmOwnerEmail ?? 'CRM-E-Mail nicht hinterlegt'}</small>
                </div>
                <button className="ghost-button" type="button" onClick={() => void remove(mapping)} disabled={saving}>
                  Entfernen
                </button>
              </article>
            ))}
          </div>
        )}
      </section>
    </main>
  )
}
