import { useMemo } from 'react'

export type LayoutNode = {
  id: string
  type: string
  title: string | null
  text: string | null
  reportKey: string | null
  columns: number
  visible: boolean
  allowed: boolean
  children: LayoutNode[]
}

export type ReportDefinition = {
  key: string
  title: string
  description: string
  requiredRole: string
  allowed: boolean
}

type Props = {
  nodes: LayoutNode[]
  reports: ReportDefinition[]
  onChange: (nodes: LayoutNode[]) => void
}

const containerTypes = new Set(['grid', 'accordion', 'tabs'])

export function DashboardContentEditor({ nodes, reports, onChange }: Props) {
  const usedReports = useMemo(() => {
    const keys = new Set<string>()
    const visit = (items: LayoutNode[]) => items.forEach(item => {
      if (item.type === 'report' && item.reportKey) keys.add(item.reportKey)
      visit(item.children)
    })
    visit(nodes)
    return keys
  }, [nodes])

  const updateNode = (path: number[], update: (node: LayoutNode) => LayoutNode) => {
    onChange(updateAtPath(nodes, path, update))
  }

  const removeNode = (path: number[]) => {
    onChange(removeAtPath(nodes, path))
  }

  const moveNode = (path: number[], direction: -1 | 1) => {
    onChange(moveAtPath(nodes, path, direction))
  }

  const addTo = (path: number[], type: string) => {
    const selectedReport = type.startsWith('report:')
      ? reports.find(report => report.key === type.slice('report:'.length))
      : reports.find(report => !usedReports.has(report.key))
    const node = createNode(type, selectedReport)
    if (!node) return
    onChange(path.length === 0 ? [...nodes, node] : updateAtPath(nodes, path, current => ({
      ...current,
      children: [...current.children, node],
    })))
  }

  return <section className="content-editor">
    <div className="content-editor-toolbar">
      <div><p className="sales-eyebrow">SEITENEDITOR</p><h2>Reportseite bearbeiten</h2><p className="sales-card-copy">Komponenten direkt auf der Seite anordnen. Das Modell wird intern als strukturierter Seitenbaum gespeichert.</p></div>
      <div className="content-editor-add-buttons"><strong>Komponente hinzufügen</strong><AddButtons onAdd={type => addTo([], type)} reports={reports} usedReports={usedReports} /></div>
    </div>
    <div className="content-editor-tree">
      {nodes.length === 0 && <p className="muted">Noch keine Komponenten. Füge eine Überschrift, ein Grid oder einen Report hinzu.</p>}
      {nodes.map((node, index) => <NodeEditor key={node.id} node={node} path={[index]} siblingCount={nodes.length} reports={reports} usedReports={usedReports} onUpdate={updateNode} onRemove={removeNode} onMove={moveNode} onAdd={addTo} />)}
    </div>
  </section>
}

function NodeEditor({ node, path, siblingCount, reports, usedReports, onUpdate, onRemove, onMove, onAdd }: {
  node: LayoutNode
  path: number[]
  siblingCount: number
  reports: ReportDefinition[]
  usedReports: Set<string>
  onUpdate: (path: number[], update: (node: LayoutNode) => LayoutNode) => void
  onRemove: (path: number[]) => void
  onMove: (path: number[], direction: -1 | 1) => void
  onAdd: (path: number[], type: string) => void
}) {
  const isContainer = containerTypes.has(node.type)
  const report = reports.find(candidate => candidate.key === node.reportKey)
  const label = node.type === 'report' ? report?.title ?? node.reportKey ?? 'Report' : typeLabel(node.type)

  return <article className={`content-editor-node content-editor-node-${node.type}`}>
    <div className="content-editor-node-header">
      <span className="content-editor-node-type">{label}</span>
      <div className="content-editor-node-actions"><button className="ghost-button" type="button" onClick={() => onMove(path, -1)} disabled={path[path.length - 1] === 0} title="Nach oben">↑</button><button className="ghost-button" type="button" onClick={() => onMove(path, 1)} disabled={path[path.length - 1] === siblingCount - 1} title="Nach unten">↓</button><button className="ghost-button danger-button" type="button" onClick={() => onRemove(path)}>Entfernen</button></div>
    </div>
    {node.type === 'heading' && <label className="content-editor-field">Überschrift<input value={node.title ?? ''} onChange={event => onUpdate(path, current => ({ ...current, title: event.target.value }))} /></label>}
    {node.type === 'text' && <label className="content-editor-field">Text<textarea value={node.text ?? ''} rows={3} onChange={event => onUpdate(path, current => ({ ...current, text: event.target.value }))} /></label>}
    {node.type === 'report' && <div className="content-editor-report-meta"><strong>{report?.title ?? node.reportKey}</strong><span>{report?.description}</span><small>{report?.allowed === false ? 'Für deine Rolle nicht sichtbar' : 'Report-Komponente'}</small></div>}
    {isContainer && <>
      <label className="content-editor-field">{node.type === 'tabs' ? 'Tab-Gruppe' : node.type === 'accordion' ? 'Akkordeon' : 'Grid'}<input value={node.title ?? ''} placeholder={node.type === 'grid' ? 'Optionale Bezeichnung' : 'Bezeichnung'} onChange={event => onUpdate(path, current => ({ ...current, title: event.target.value || null }))} /></label>
      {node.type === 'grid' && <label className="content-editor-field">Spalten<select value={node.columns} onChange={event => onUpdate(path, current => ({ ...current, columns: Number(event.target.value) }))}><option value={1}>1 Spalte</option><option value={2}>2 Spalten</option><option value={3}>3 Spalten</option><option value={4}>4 Spalten</option></select></label>}
      <div className="content-editor-add-row"><span>Unterkomponente</span>{node.type === 'tabs' && <button className="ghost-button" type="button" onClick={() => onAdd(path, 'tab')}>Tab hinzufügen</button>}{node.type === 'accordion' && <button className="ghost-button" type="button" onClick={() => onAdd(path, 'section')}>Abschnitt hinzufügen</button>}<AddButtons onAdd={type => onAdd(path, type)} reports={reports} usedReports={usedReports} compact /></div>
      <div className="content-editor-children">
        {node.children.length === 0 && <p className="muted">Leer – füge {node.type === 'tabs' ? 'einen Tab' : node.type === 'accordion' ? 'einen Abschnitt' : 'eine Komponente'} hinzu.</p>}
        {node.children.map((child, index) => <NodeEditor key={child.id} node={child} path={[...path, index]} siblingCount={node.children.length} reports={reports} usedReports={usedReports} onUpdate={onUpdate} onRemove={onRemove} onMove={onMove} onAdd={onAdd} />)}
      </div>
    </>}
    {node.type === 'report' && <label className="content-editor-toggle"><input type="checkbox" checked={node.visible} onChange={event => onUpdate(path, current => ({ ...current, visible: event.target.checked }))} /> auf der Seite anzeigen</label>}
    {node.type === 'report' && <label className="content-editor-field">Breite<select value={node.columns} onChange={event => onUpdate(path, current => ({ ...current, columns: Number(event.target.value) }))}><option value={1}>1 Spalte</option><option value={2}>2 Spalten</option></select></label>}
  </article>
}

function AddButtons({ onAdd, reports, usedReports, compact = false }: { onAdd: (type: string) => void; reports: ReportDefinition[]; usedReports: Set<string>; compact?: boolean }) {
  const available = reports.filter(report => !usedReports.has(report.key))
  return <div className={`content-editor-add-options ${compact ? 'is-compact' : ''}`}>
    <button className="ghost-button" type="button" onClick={() => onAdd('grid')}>Grid</button>
    <button className="ghost-button" type="button" onClick={() => onAdd('accordion')}>Akkordeon</button>
    <button className="ghost-button" type="button" onClick={() => onAdd('tabs')}>Tabs</button>
    <button className="ghost-button" type="button" onClick={() => onAdd('heading')}>Überschrift</button>
    <button className="ghost-button" type="button" onClick={() => onAdd('text')}>Text</button>
    {available.length > 0 && <select className="content-editor-report-picker" value="" onChange={event => { if (event.target.value) onAdd(event.target.value) }}><option value="">Report …</option>{available.map(report => <option key={report.key} value={`report:${report.key}`}>{report.title}</option>)}</select>}
  </div>
}

function createNode(type: string, firstUnusedReport?: ReportDefinition): LayoutNode | null {
  const id = `node-${crypto.randomUUID()}`
  if (type.startsWith('report:')) {
    const reportKey = type.slice('report:'.length)
    return { id, type: 'report', title: firstUnusedReport?.key === reportKey ? firstUnusedReport.title : reportKey, text: null, reportKey, columns: 1, visible: true, allowed: true, children: [] }
  }
  if (type === 'heading') return { id, type, title: 'Neue Überschrift', text: null, reportKey: null, columns: 2, visible: true, allowed: true, children: [] }
  if (type === 'text') return { id, type, title: null, text: 'Neuer Text', reportKey: null, columns: 2, visible: true, allowed: true, children: [] }
  if (type === 'accordion') return { id, type, title: 'Neuer Abschnitt', text: null, reportKey: null, columns: 1, visible: true, allowed: true, children: [{ id: `${id}-section`, type: 'grid', title: 'Abschnitt 1', text: null, reportKey: null, columns: 2, visible: true, allowed: true, children: [] }] }
  if (type === 'tabs') return { id, type, title: 'Neue Tabs', text: null, reportKey: null, columns: 1, visible: true, allowed: true, children: [{ id: `${id}-tab`, type: 'grid', title: 'Tab 1', text: null, reportKey: null, columns: 2, visible: true, allowed: true, children: [] }] }
  if (type === 'tab') return { id, type: 'grid', title: 'Neuer Tab', text: null, reportKey: null, columns: 2, visible: true, allowed: true, children: [] }
  if (type === 'section') return { id, type: 'grid', title: 'Neuer Abschnitt', text: null, reportKey: null, columns: 2, visible: true, allowed: true, children: [] }
  if (type === 'grid') return { id, type, title: null, text: null, reportKey: null, columns: 2, visible: true, allowed: true, children: [] }
  return null
}

function typeLabel(type: string) {
  return type === 'grid' ? 'Grid' : type === 'accordion' ? 'Akkordeon' : type === 'tabs' ? 'Tabs' : type === 'heading' ? 'Überschrift' : type === 'text' ? 'Text' : type
}

function updateAtPath(nodes: LayoutNode[], path: number[], update: (node: LayoutNode) => LayoutNode): LayoutNode[] {
  if (path.length === 0) return nodes
  const [index, ...rest] = path
  return nodes.map((node, currentIndex) => currentIndex !== index ? node : rest.length === 0 ? update(node) : { ...node, children: updateAtPath(node.children, rest, update) })
}

function removeAtPath(nodes: LayoutNode[], path: number[]): LayoutNode[] {
  if (path.length === 1) return nodes.filter((_, index) => index !== path[0])
  const [index, ...rest] = path
  return nodes.map((node, currentIndex) => currentIndex === index ? { ...node, children: removeAtPath(node.children, rest) } : node)
}

function moveAtPath(nodes: LayoutNode[], path: number[], direction: -1 | 1): LayoutNode[] {
  if (path.length === 1) {
    const target = path[0] + direction
    if (target < 0 || target >= nodes.length) return nodes
    const next = [...nodes]
    ;[next[path[0]], next[target]] = [next[target], next[path[0]]]
    return next
  }
  const [index, ...rest] = path
  return nodes.map((node, currentIndex) => currentIndex === index ? { ...node, children: moveAtPath(node.children, rest, direction) } : node)
}
