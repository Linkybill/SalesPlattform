export function IndustrySelection({ options, selected, onChange }: {
  options: string[]
  selected: string[]
  onChange: (value: string[]) => void
}) {
  const choices = [...options, ...selected.filter(value => !options.includes(value))]
  return <details className="goal-industry-selector">
    <summary>Branchen: {selected.length ? selected.join(', ') : 'Alle'}</summary>
    {choices.length === 0 ? <p>Im CRM-Spiegel sind noch keine Branchen vorhanden.</p> : <div className="team-user-options">
      {choices.map(value => <label key={value} className="team-user-option">
        <input type="checkbox" checked={selected.includes(value)} onChange={event => onChange(event.target.checked
          ? [...selected, value] : selected.filter(item => item !== value))} />
        <span>{value}</span>
      </label>)}
    </div>}
    {selected.length > 0 && <button type="button" className="secondary-button" onClick={() => onChange([])}>Alle Branchen zulassen</button>}
  </details>
}
