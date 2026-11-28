import { useEffect, useRef, useState, type FormEvent } from 'react'
import { createPortal } from 'react-dom'
import { useApplicationContext } from '@hammer2fall/identity-platform-react'
import { HttpTransportType, HubConnectionBuilder } from '@microsoft/signalr'
import { tenantApplicationPath } from './identityPlatformConfig'
import './teams.css'
import './salesChat.css'

type Topic = 'calls' | 'renewals'
type Signal = {
  id: string
  title: string
  type: string
  reason: string
  dueAt: string | null
  priority: string
  assignedToMe: boolean
  crmUrl: string | null
  lastDocumentedContact: string | null
  recurringAmount: number | null
  currency: string | null
}
type Signals = { worklistRefreshedAt: string | null; items: Signal[] }
export type ModelStatus = { enabled: boolean; available: boolean; model: string | null }
type Turn = { role: 'user' | 'assistant'; text: string }
type Change = { title: string; details: string[]; approval: string }
type ChatProgress = { kind: 'status' | 'step' | 'text'; text: string }

const date = new Intl.DateTimeFormat('de-DE', { timeZone: 'Europe/Berlin', dateStyle: 'medium' })
const timestamp = new Intl.DateTimeFormat('de-DE', { timeZone: 'Europe/Berlin', dateStyle: 'medium', timeStyle: 'short' })
const number = new Intl.NumberFormat('de-DE', { maximumFractionDigits: 2 })
const exampleQuestions = [
  'Wen soll ich als Nächstes anrufen und warum?',
  'Zeige mir die offenen Aufgaben von Team …',
  'Welche Aufgaben hat …?',
  'Weise die Aufgabe … meiner Kollegin … zu.',
  'Erstelle für heute ein Tagesprofil mit …',
]

export function SalesChatWidget({ model, initiallyOpen = false }: { model: ModelStatus; initiallyOpen?: boolean }) {
  const { authorizedFetch, activeTenantId, getAccessToken } = useApplicationContext()
  const [question, setQuestion] = useState('')
  const [turns, setTurns] = useState<Turn[]>([])
  const [changes, setChanges] = useState<Change[]>([])
  const [changeStatus, setChangeStatus] = useState('')
  const [askError, setAskError] = useState('')
  const [busy, setBusy] = useState(false)
  const [progress, setProgress] = useState('')
  const [steps, setSteps] = useState<string[]>([])
  const [partialAnswer, setPartialAnswer] = useState('')
  const pending = useRef<AbortController | null>(null)

  useEffect(() => () => pending.current?.abort(), [])

  const [open, setOpen] = useState(initiallyOpen)
  const launcher = useRef<HTMLButtonElement>(null)
  const input = useRef<HTMLTextAreaElement>(null)
  const minimizeButton = useRef<HTMLButtonElement>(null)
  const restoreLauncherFocus = useRef(false)

  useEffect(() => {
    if (initiallyOpen) setOpen(true)
  }, [initiallyOpen])

  useEffect(() => {
    if (open) {
      (input.current ?? minimizeButton.current)?.focus({ preventScroll: true })
    } else if (restoreLauncherFocus.current) {
      restoreLauncherFocus.current = false
      launcher.current?.focus()
    }
  }, [open])

  function close() {
    restoreLauncherFocus.current = true
    setOpen(false)
  }

  async function ask(event: FormEvent) {
    event.preventDefault()
    const text = question.trim()
    if (pending.current || !text || !model.available || !activeTenantId) return
    const controller = new AbortController()
    pending.current = controller
    setAskError('')
    setProgress('Live-Verbindung wird aufgebaut …')
    setSteps([])
    setPartialAnswer('')
    setBusy(true)
    setQuestion('')
    const submitted: Turn[] = [...turns, { role: 'user', text }]
    setTurns(submitted)
    const connection = new HubConnectionBuilder()
      .withUrl(`${tenantApplicationPath.applicationBaseUrl.replace(/\/$/, '')}/api/strategy/hub`, {
        accessTokenFactory: getAccessToken,
        headers: { 'X-Tenant-Id': activeTenantId },
        transport: HttpTransportType.LongPolling,
        withCredentials: false,
      })
      .build()
    const stop = () => { void connection.stop().catch(() => undefined) }
    controller.signal.addEventListener('abort', stop, { once: true })
    connection.on('Progress', (update: ChatProgress) => {
      if (controller.signal.aborted) return
      if (update.kind === 'text') {
        setPartialAnswer(previous => previous + update.text)
      } else if (update.kind === 'step') {
        setSteps(previous => [...previous, update.text])
        setProgress(update.text)
      } else {
        setProgress(update.text)
      }
    })
    try {
      await connection.start()
      controller.signal.throwIfAborted()
      setProgress('Frage wird verarbeitet …')
      const result = await connection.invoke<{ answer: string; changes: Change[] }>('Ask', {
        question: text, history: turns.slice(-8),
      })
      if (!controller.signal.aborted) {
        setTurns([...submitted, { role: 'assistant', text: (result.answer || 'Der Assistent hat keine Antwort geliefert.').slice(0, 3000) }])
        setChanges(previous => [...previous, ...result.changes])
      }
    } catch (error) {
      const detail = error instanceof Error ? error.message.split('HubException: ')[1] : undefined
      setTurns(turns)
      setQuestion(text)
      setAskError(controller.signal.aborted ? 'Antwort abgebrochen.' : detail
        || 'Die Live-Verbindung wurde unterbrochen. Du kannst die Frage erneut senden.')
    } finally {
      controller.signal.removeEventListener('abort', stop)
      stop()
      setPartialAnswer('')
      setProgress('')
      if (pending.current === controller) {
        pending.current = null
        setBusy(false)
      }
    }
  }

  async function apply(change: Change) {
    if (pending.current) return
    const controller = new AbortController()
    pending.current = controller
    setBusy(true)
    setChangeStatus('')
    setAskError('')
    try {
      const response = await authorizedFetch('/api/strategy/changes/apply', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ approval: change.approval }), signal: controller.signal,
      })
      const result: { message?: string; detail?: string } = await response.json().catch(() => ({}))
      if (!response.ok) throw new Error(result.message || result.detail || 'Die Änderung konnte nicht gespeichert werden.')
      setChanges(previous => previous.filter(item => item.approval !== change.approval))
      setChangeStatus(result.message || 'Änderung gespeichert.')
    } catch (error) {
      if (!controller.signal.aborted) setAskError(error instanceof Error ? error.message : 'Die Änderung konnte nicht gespeichert werden.')
    } finally {
      pending.current = null
      setBusy(false)
    }
  }

  return createPortal(<div className="sales-chat-widget">
    <section id="sales-chat-window" className="sales-chat-window" hidden={!open}
      role="dialog" aria-modal="false" aria-labelledby="sales-chat-title"
      onKeyDown={event => { if (event.key === 'Escape') { event.stopPropagation(); close() } }}>
      <header className="sales-chat-header">
        <h2 id="sales-chat-title">Vertriebsassistent</h2>
        <button ref={minimizeButton} type="button" className="sales-chat-close" onClick={close} aria-label="Chat minimieren">−</button>
      </header>
      <div className="sales-chat-content">
      {model.available ? <>
        {steps.length > 0 && <section className="sales-chat-change" aria-label="Arbeitsschritte">
          <strong>Arbeitsschritte</strong>
          <ol>{steps.map((step, index) => <li key={index}>{step}</li>)}</ol>
        </section>}
        {turns.length > 0 && <div className="sales-chat-transcript" role="log" aria-label="Gespräch">
          {turns.map((turn, index) => <div key={index} className={`sales-chat-turn ${turn.role}`}>
            <strong>{turn.role === 'user' ? 'Du' : 'Vertriebsassistent'}</strong>
            <p>{turn.text}</p>
          </div>)}
          {partialAnswer && <div className="sales-chat-turn assistant">
            <strong>Vertriebsassistent</strong><p>{partialAnswer}</p>
          </div>}
        </div>}
        {changes.map(change => <section className="sales-chat-change" key={change.approval}>
          <h2>{change.title}</h2>
          <ul>{change.details.map((detail, index) => <li key={index}>{detail}</li>)}</ul>
          <div className="teams-actions">
            <button type="button" className="primary-button" disabled={busy} onClick={() => void apply(change)}>Änderung bestätigen</button>
            <button type="button" className="secondary-button" disabled={busy}
              onClick={() => setChanges(items => items.filter(item => item.approval !== change.approval))}>Verwerfen</button>
          </div>
        </section>)}
        <form onSubmit={ask} className="sales-chat-form">
          <label htmlFor="sales-strategy-question">Nachricht</label>
          <textarea ref={input} id="sales-strategy-question" value={question} onChange={event => setQuestion(event.target.value)} maxLength={1200} rows={3} />
          <button type="submit" className="primary-button" disabled={busy || !question.trim()}>Senden</button>
          {progress && <button type="button" className="secondary-button" onClick={() => pending.current?.abort()}>Antwort abbrechen</button>}
        </form>
        {turns.length === 0 && <div className="teams-actions" aria-label="Beispiele">
          {exampleQuestions.map(example => <button type="button" key={example} className="secondary-button"
            onClick={() => setQuestion(example)}>{example}</button>)}
        </div>}
        {busy && <p role="status">{progress || 'Wird verarbeitet …'}</p>}
        {changeStatus && <p role="status">{changeStatus}</p>}
        {askError && <p className="teams-error" role="alert">{askError}</p>}
      </> : <p>Bitte im Mandantenportal unter AppSettings Anbieter und Modell konfigurieren.</p>}
      </div>
    </section>
    <button ref={launcher} type="button" className="primary-button sales-chat-launcher" hidden={open}
      aria-expanded={open} aria-controls="sales-chat-window" onClick={() => setOpen(true)}>
      <svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true">
        <path d="M21 11.5a8.5 8.5 0 0 1-8.5 8.5H4l-3 2V11.5a10 10 0 0 1 20 0Z" />
        <path d="M7 9h8M7 13h5" />
      </svg>
      <span>Vertriebschat</span>
    </button>
  </div>, document.body)
}

export function SalesStrategyPage() {
  const { authorizedFetch, activeTenantId } = useApplicationContext()
  const [topic, setTopic] = useState<Topic>('calls')
  const [signals, setSignals] = useState<Signals | null>(null)
  const [loadError, setLoadError] = useState(false)

  useEffect(() => {
    if (!activeTenantId) return
    const controller = new AbortController()
    setSignals(null)
    setLoadError(false)
    void authorizedFetch(`/api/strategy/${topic}`, { signal: controller.signal })
      .then(async response => {
        if (!response.ok) throw new Error()
        const result: Signals = await response.json()
        if (!controller.signal.aborted) setSignals(result)
      }).catch(() => { if (!controller.signal.aborted) setLoadError(true) })
    return () => controller.abort()
  }, [activeTenantId, authorizedFetch, topic])

  return <main className="sales-page teams-page">
    <section className="sales-panel">
      <h2>Vertriebsauswertung</h2>
      <div className="teams-heading">
        <button type="button" aria-pressed={topic === 'calls'} className="secondary-button" onClick={() => setTopic('calls')}>Anrufprioritäten</button>
        <button type="button" aria-pressed={topic === 'renewals'} className="secondary-button" onClick={() => setTopic('renewals')}>Vertragsverlängerungen</button>
      </div>
      {!activeTenantId ? <p>Bitte einen Mandanten auswählen.</p>
        : loadError ? <p role="alert">Die Auswertung konnte nicht geladen werden.</p>
        : !signals ? <p role="status">Auswertung wird geladen …</p>
        : <>
          <p>{signals.worklistRefreshedAt ? `Arbeitsliste ausgewertet am ${timestamp.format(new Date(signals.worklistRefreshedAt))} Uhr.` : 'Für die Arbeitsliste liegt kein Auswertungszeitpunkt vor.'}</p>
          {signals.items.length === 0 ? <p>Keine passenden offenen Vorgänge.</p>
            : <div className="teams-table-scroll"><table className="teams-table">
              <thead><tr><th>Vorgang</th><th>Begründung</th><th>Fällig</th><th>Aktion</th></tr></thead>
              <tbody>{signals.items.map(item => <tr key={item.id}>
                <th scope="row">{item.title}{item.assignedToMe && <p>Mir zugewiesen</p>}</th>
                <td>{item.reason}
                  {item.lastDocumentedContact && <p>Letzter dokumentierter Kontakt am {date.format(new Date(item.lastDocumentedContact))}</p>}
                  {item.recurringAmount !== null && <p>Wiederkehrender Betrag {number.format(item.recurringAmount)} {item.currency}</p>}
                </td>
                <td>{item.dueAt ? date.format(new Date(item.dueAt)) : '–'}</td>
                <td>{item.crmUrl ? <a href={item.crmUrl} target="_blank" rel="noreferrer">CRM öffnen</a> : '–'}</td>
              </tr>)}</tbody>
            </table></div>}
        </>}
    </section>
  </main>
}
