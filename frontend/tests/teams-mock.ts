const users = [{ id: 'one', email: 'anna@example.test' }, { id: 'two', email: 'ben@example.test' }]
const teams: { id: string; name: string; revision: string; members: typeof users }[] = []
const context = {
  activeTenantId: 'synthetic', user: { email: 'admin@example.test' },
  activeTenant: { isTenantAdmin: !window.location.search.includes('readonly') },
  authorizedFetch: async (url: string, init?: RequestInit) => {
    if (url === '/api/teams/users') return Response.json(users)
    if (init?.method === 'POST' || init?.method === 'PUT') {
      const body = JSON.parse(init.body as string)
      const id = init.method === 'PUT' ? url.split('/').pop()! : crypto.randomUUID()
      const team = { id, name: body.name, revision: crypto.randomUUID(), members: users.filter(u => body.memberIds.includes(u.id)) }
      const index = teams.findIndex(t => t.id === id)
      if (index >= 0) teams.splice(index, 1, team); else teams.push(team)
      return Response.json(team)
    }
    return Response.json(teams)
  },
}
const log = () => {}
export const useApplicationContext = () => context
export const usePlatformLog = () => log
