import { useEffect, useState } from 'react'

export type HookPostmanData = {
  url: string; module: string; channelId: string; token: string; expiresAt: string
}

const shellQuote = (value: string) => `'${value.replace(/'/g, `'"'"'`)}'`

export function ZohoHookPostman({ data, onClose }: { data: HookPostmanData; onClose: () => void }) {
  const [recordId, setRecordId] = useState('')
  const [showToken, setShowToken] = useState(false)
  const [copyStatus, setCopyStatus] = useState('')
  const [expired, setExpired] = useState(() => Date.parse(data.expiresAt) <= Date.now())
  useEffect(() => {
    const timer = window.setInterval(() => setExpired(Date.parse(data.expiresAt) <= Date.now()), 1000)
    return () => window.clearInterval(timer)
  }, [data.expiresAt])
  const validId = /^[0-9]{1,30}$/.test(recordId.trim())
  const body = validId && !expired ? JSON.stringify({ module: data.module, ids: [recordId.trim()],
    operation: 'update', channel_id: data.channelId, token: data.token }, null, 2) : ''
  const curl = body ? `curl --request POST ${shellQuote(data.url)} --header 'Content-Type: application/json' --data-raw ${shellQuote(body)}` : ''
  async function copy(value: string, label: string) {
    try {
      await navigator.clipboard.writeText(value)
      setCopyStatus(`${label} kopiert.`)
    } catch {
      setCopyStatus('Kopieren nicht möglich. Bitte den Text markieren und manuell kopieren.')
    }
  }
  return <section className="hook-postman" aria-label="Postman-Test vorbereiten">
    <div className="card-heading">
      <h3>Postman-Test für {data.module}</h3>
      <button className="secondary-button" type="button" onClick={onClose}>Testdaten schließen</button>
    </div>
    <p>POST · Authorization: No Auth · Body: raw / JSON · Content-Type: application/json</p>
    <label>Callback-URL<input aria-label="Postman Callback-URL" readOnly value={data.url} /></label>
    <div className="button-row">
      <button className="secondary-button" type="button" onClick={() => void copy(data.url, 'URL')}>URL kopieren</button>
    </div>
    {expired ? <p role="alert">Diese Registrierung ist abgelaufen. Testdaten schließen und erneut laden.</p> : <>
      <label>Channel-ID<input aria-label="Postman Channel-ID" readOnly value={data.channelId} /></label>
      <label>Webhook-Token<input aria-label="Postman Webhook-Token" type={showToken ? 'text' : 'password'}
        autoComplete="off" readOnly value={data.token} /></label>
      <div className="button-row">
        <button className="secondary-button" type="button" aria-pressed={showToken} onClick={() => setShowToken(x => !x)}>
          {showToken ? 'Token verbergen' : 'Token anzeigen'}</button>
        <button className="secondary-button" type="button" onClick={() => void copy(data.channelId, 'Channel-ID')}>Channel-ID kopieren</button>
        <button className="secondary-button" type="button" onClick={() => void copy(data.token, 'Webhook-Token')}>Token kopieren</button>
      </div>
      <p>Gültig bis {new Date(data.expiresAt).toLocaleString('de-DE')}. Nach einer Hook-Erneuerung die Testdaten neu laden.</p>
      <label>Zoho-Datensatz-ID aus {data.module}<input aria-label="Postman Record-ID" inputMode="numeric" autoComplete="off"
        placeholder="ID des Datensatzes in Zoho"
        value={recordId} onChange={event => { setRecordId(event.target.value); setCopyStatus('') }} /></label>
      <p>Die ID des konkreten Datensatzes, den du in Zoho geöffnet hast (aus dessen Datensatz-Link).
        Für Tasks ist das eine Aufgaben-ID, für Calls eine Anruf-ID. Channel und Token werden oben automatisch geladen.</p>
      <p>Der Request simuliert eine Änderung;
        bei Annahme wird das Ereignis gespeichert und der Datensatz im nächsten Hook-Job verarbeitet.</p>
      {recordId && !validId && <p role="alert">Bitte eine gültige numerische Zoho-Record-ID eingeben.</p>}
      {body && <>
        <label>JSON-Body mit Webhook-Token<textarea aria-label="Postman JSON-Body" readOnly rows={9} value={body} /></label>
        <div className="button-row">
          <button className="secondary-button" type="button" onClick={() => void copy(body, 'JSON-Body')}>JSON kopieren</button>
          <button className="secondary-button" type="button" onClick={() => void copy(curl, 'cURL-Request')}>cURL für Postman kopieren</button>
        </div>
        <details><summary>cURL-Request anzeigen</summary><textarea aria-label="Postman cURL-Request" readOnly rows={6} value={curl} /></details>
        <p>In Postman über Import den kopierten cURL-Request einfügen. Erwartete Antwort: 202 Accepted.
          Dieser manuelle Test prüft die Annahme durch Sales; er bestätigt keinen Versand aus Zohos Netzwerk.</p>
      </>}
    </>}
    {copyStatus && <p role="status">{copyStatus}</p>}
  </section>
}
