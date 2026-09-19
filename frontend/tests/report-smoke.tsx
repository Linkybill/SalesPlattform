import { useState } from 'react'
import { createRoot } from 'react-dom/client'
import { ReportsPage } from '../src/ReportsPage'
import { WorklistWidget } from '../src/WorklistWidget'
import '../src/styles.css'
import '../src/reportNavigation.css'

function Fixture() {
  const [work, setWork] = useState(true)
  return <><header><button onClick={() => setWork(true)}>Test Arbeit</button><button onClick={() => setWork(false)}>Test Steuerung</button></header>{work ? <main className="sales-page reports-page"><h1>Arbeit</h1><WorklistWidget /></main> : <ReportsPage />}</>
}
createRoot(document.getElementById('root')!).render(<Fixture />)
