const types = [
  { key: 'cold-call', name: 'Kaltakquise-Anruf' },
  { key: 'lead-reactivation-call', name: 'Lead-Reaktivierungsanruf' },
  { key: 'renewal-conversation', name: 'Verlängerungsgespräch' },
  { key: 'follow-up', name: 'Wiedervorlage bearbeiten' },
]
const profiles: Record<string, unknown>[] = []
const plans: Record<string, unknown>[] = []
const context = {
  activeTenantId: 'synthetic', user: { email: 'admin@example.test' },
  activeTenant: { isTenantAdmin: !window.location.search.includes('readonly') },
  authorizedFetch: async (url: string, init?: RequestInit) => {
    if (url.endsWith('/types')) return Response.json(types)
    if (window.location.search.includes('load-error')) return new Response('', { status: 503 })
    if (url.endsWith('/industries')) return Response.json(['Baugewerbe', 'Handel'])
    if (url.endsWith('/plans')) return Response.json({ employees: [{ id: 'employee', email: 'employee@example.test' }], plans })
    if (url.endsWith('/mine')) return Response.json([{ id: 'assignment', taskType: 'cold-call', title: 'Testlead',
      industry: 'Baugewerbe', assignedOn: '2026-11-26', crmUrl: 'https://crm.example.test/lead/1',
      crmTaskUrl: 'https://crm.example.test/tasks/2', crmStatus: 'Completed', crmCompletedAt: '2026-11-26T12:00:00Z' }])
    if (init?.method === 'POST' || init?.method === 'PUT') {
      if (window.location.search.includes('conflict')) return Response.json({ message: 'Das Profil wurde inzwischen geändert.' }, { status: 409 })
      if (url.endsWith('/deactivate')) {
        const plan = plans.find(item => item.id === url.split('/').at(-2))
        if (!plan) return new Response('', { status: 404 })
        plan.deactivatedAt = new Date().toISOString()
        return Response.json(plan)
      }
      const body = JSON.parse(init.body as string)
      if (url.startsWith('/api/sales-tasks/')) {
        const membershipId = url.includes('/employees/') ? url.split('/').pop() : null
        const existingId = init.method === 'PUT' && membershipId === null ? url.split('/').pop() : undefined
        const plan = { id: existingId ?? crypto.randomUUID(), revision: crypto.randomUUID(), deactivatedAt: null,
          name: body.name, membershipId, startsOn: body.startsOn, endsOn: body.endsOn,
          configuration: { goals: body.goals, industries: body.industries } }
        const index = plans.findIndex(item => item.id === plan.id)
        if (index >= 0) plans.splice(index, 1, plan); else plans.push(plan)
        return Response.json(plan)
      }
      const id = init.method === 'PUT' ? url.split('/').pop()! : crypto.randomUUID()
      const profile = { ...body, id, revision: crypto.randomUUID() }
      const index = profiles.findIndex(p => p.id === id)
      if (index >= 0) profiles.splice(index, 1, profile); else profiles.push(profile)
      return Response.json(profile)
    }
    return Response.json(profiles)
  },
}
export const useApplicationContext = () => context
