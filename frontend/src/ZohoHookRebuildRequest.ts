type AuthorizedFetch = (url: string, init?: RequestInit) => Promise<Response>

export async function requestZohoHookRebuild({ authorizedFetch, platformAuthorizedFetch, applicationKey, tenantId, installationId, signal, handoffOnly = false }: {
  authorizedFetch: AuthorizedFetch; platformAuthorizedFetch: AuthorizedFetch
  applicationKey: string; tenantId: string; installationId?: string; signal: AbortSignal; handoffOnly?: boolean
}): Promise<Response> {
  let runId: string | undefined
  if (handoffOnly && !installationId) throw new Error('A callback handoff requires an installation')
  if (installationId) {
    if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(installationId))
      throw new Error('Invalid installation identity')
    runId = crypto.randomUUID()
    const staged = await authorizedFetch(`/api/installation-jobs/jobs/crm-subscription-maintenance/runs/${runId}`, {
      method: 'PUT', headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify({ parameters: handoffOnly ? { operation: 'resume-installation-handoff' } : null }), signal,
    })
    if (!staged.ok) return staged
  }
  if (signal.aborted) throw new DOMException('Aborted', 'AbortError')
  const scope = installationId ? `&installationId=${encodeURIComponent(installationId)}` : ''
  return platformAuthorizedFetch(
    `/api/application-context/${encodeURIComponent(applicationKey)}/jobs/crm-subscription-maintenance/runs?tenantId=${encodeURIComponent(tenantId)}${scope}`, {
      method: 'POST', headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify(runId ? { runId } : {}), signal,
    })
}
