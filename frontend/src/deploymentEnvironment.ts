// Public runtime configuration only. Kubernetes mounts this before main.tsx.
// Direct Vite development must supply the same public profile via VITE_*.
const runtime = (globalThis as typeof globalThis & {
  __IDENTITY_PLATFORM_CONFIG__?: Readonly<Record<string, string>>
}).__IDENTITY_PLATFORM_CONFIG__

export const deploymentEnvironment = runtime ?? import.meta.env

for (const key of ['VITE_PLATFORM_API_BASE_URL', 'VITE_TENANT_PORTAL_URL']) {
  if (!deploymentEnvironment[key]?.trim()) {
    throw new Error(`Deployment-Konfiguration fehlt: ${key}`)
  }
}
