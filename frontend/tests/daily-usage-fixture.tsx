import { DailyUsageChart, type DailyUsageReport } from '../src/DailyUsageChart'

declare global { interface Window { dailyUsageFixture: { fail: boolean; empty: boolean; errors: number; requests: string[] } } }
window.dailyUsageFixture = { fail: false, empty: false, errors: 0, requests: [] }
async function fetchDaily(url: string): Promise<Response> {
  window.dailyUsageFixture.requests.push(url)
  if (window.dailyUsageFixture.fail) return new Response('private error not exposed', { status: 503 })
  const days = Number(new URL(url, location.origin).searchParams.get('days'))
  const last = Date.UTC(2026, 8, 19)
  const dates = Array.from({ length: days }, (_, index) => new Date(last - (days - 1 - index) * 86400000).toISOString().slice(0, 10))
  const data: DailyUsageReport = {
    fromUtc: `${dates[0]}T00:00:00Z`, toUtc: '2026-09-19T12:00:00Z', timeZone: 'UTC',
    series: window.dailyUsageFixture.empty ? [] : ['zoho', 'another'].map(providerKey => ({
      providerKey, connectionKey: 'default', usageUnit: providerKey === 'zoho' ? 'credits' : 'requests',
      days: dates.map((date, index) => ({ date, requests: index === 0 ? 3 : 0,
        successfulRequests: index === 0 ? 2 : 0, failedRequests: index === 0 ? 1 : 0,
        estimatedUnits: index === 0 ? providerKey === 'zoho' ? 9 : 88 : 0, isPartial: index === days - 1 })),
    })),
  }
  return Response.json(data)
}
const onError = () => { window.dailyUsageFixture.errors++ }
export function DailyUsageFixture() { return <DailyUsageChart authorizedFetch={fetchDaily} onError={onError} /> }
