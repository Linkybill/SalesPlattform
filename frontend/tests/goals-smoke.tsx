import { createRoot } from 'react-dom/client'
import { TaskGoalProfilesPage } from '../src/TaskGoalProfilesPage'
import { MySalesTasksPage } from '../src/MySalesTasksPage'
import '../src/styles.css'
createRoot(document.getElementById('root')!).render(window.location.search.includes('mine') ? <MySalesTasksPage /> : <TaskGoalProfilesPage />)
