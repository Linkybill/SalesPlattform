# Sales: Deployment- und Betriebsstand vom 10.09.2026

## Ergänzung 12.09.2026 – Remote-Integration

`IdentityPlatform.Shared` wurde nach erfolgreicher Veröffentlichung auf
`0.1.60` aktualisiert; React bleibt `0.1.50`. Der obsolete HTTP-/Container-DNS-
Default für die Platform API wurde entfernt. Der gemeinsame Deploymentplan
liefert die öffentliche HTTPS-Origin; API-Aufrufe nutzen `/api`.
`dev/main` installiert Sales weiterhin auf AX42-1 bei derselben Plattform.
WireGuard-/Remote-Bootstrap wird ausschließlich vom gemeinsamen Plattform-
Tooling geliefert, nicht als zweite Sales-Implementierung. Grenzen:
[Server-VPN](../../../../IdentityPlattform/docs/server-vpn.md).

Die ersten nativen Consumer-Builds wurden beim Registry-Restore mit HTTP 401
blockiert (kein Compilerfehlernachweis). `GITHUB_PACKAGES_TOKEN` lokal und
`PACKAGES_TOKEN` in CI benötigen gültigen Paketzugriff. Kein Live-Rollout.

Dieser Abgleich konsolidiert die Arbeit vom 08.–10.09.2026. Architektur,
gemeinsame Fehlerkorrekturen und Betriebsgrenzen stehen im
[Plattform-Gesamtstand](../../../../IdentityPlattform/docs/stand-2026-09-10.md).
Ältere Tagesnotizen sind historische Nachweise, keine noch offenen Aufträge.

## Auslieferungsstand

- Sales-Commit `691e9c2` wurde auf `main` gepusht.
- Gemeinsame Plattformkorrekturen: `bd533b7` auf `main` gepusht.
- React-Library `0.1.50` ist veröffentlicht und in Sales-Paketmanifest und
  Lockfile übernommen. Das Backend verwendet `IdentityPlatform.Shared` `0.1.59`
  mit dem einheitlichen `/api`-Vertrag; NuGet-Versionen werden unabhängig verwaltet.
- Native Windows-Prüfungen: Backend-Release-Build ohne Warnungen/Fehler,
  Frontend-Lint/-Build, drei Root-/OIDC-Vertragstests und HTTPS-Vertragstests
  bestanden. Ein eigenes Sales-.NET-Testprojekt existiert nicht.
- Das ist kein Nachweis eines erfolgreichen vollständigen Hetzner-Rollouts
  oder eines interaktiven Microsoft-Logins. Diese Abnahmen bleiben offen.

## Profile und Aufruf

Lokale Befehle ausschließlich in Windows PowerShell, kein WSL. Im Sales-Ordner:

```powershell
.\deploy-all.ps1 -Target local -PlatformTarget local -Environment dev
# Alternativer Zielrechner, kein zusätzlicher Plattform-Rollout:
.\deploy-all.ps1 -Target ax42-1 -PlatformTarget ax42-1 -Environment dev

# Separater Runtime-Cluster, zugehörige Plattform bleibt AX42-1
.\deploy-all.ps1 -Target ax42-2 -PlatformTarget ax42-1 -Environment dev
```

`appsettings.Deployment.json` ist die Quelle der Sales-URLs:

| Profil | Sales-Frontend |
| --- | --- |
| local / dev | `https://127.0.0.1:3003` |
| ax42-1 / dev | `https://176.9.57.203:3003` |

Zoho-Redirect-/Frontend-Callbacks stehen ebenfalls hier unter `BackendUrls`.
Das öffentliche `assets/deployment-config.js` liefert die aufgelösten
Runtime-URLs, niemals Secrets. Vite-Defaults sind kein Remote-Deploymentprofil.
`ClusterNamePrefix` verändert Namen, nicht Ports/URLs. Keine zweite Installation
auf demselben Target/Environment mit unverändertem Portprofil erstellen.

## Zuständigkeiten und korrigierte Fehler

Sales deployt nur Sales. Die Plattform muss vorher separat installiert und mit
dem Vertrag `registration-v1` aktualisiert sein. Das Sales-Manifest meldet seine
URLs; die Plattform richtet daraus die HTTPS-Einstiege ein und lädt bekannte
aktive Manifeste auch nach ihrem Neustart. Kein statischer Sales-Eintrag in
einem zweiten Plattform-Helm-Profil.

Der Weg ist Traefik → zusammengeführter Application Router → Sales. Der Router
wählt tenant-/app-bezogen zulässige Shared-/Dedicated-Ziele; Sales führt keinen
eigenen Cluster- oder Routerpool. Backend-Runtime-Env und zentrale Secret-
Verweise kommen aus dem Plattformgenerator. Keine App-Brokerpasswörter,
keine Backend-Credentials im Frontend, keine pauschalen Scale-0/1-Rebuilds.

Die gemeldeten Deployabbrüche betrafen unter anderem Kubernetes-Ausgabe/
Profilabfrage, den alten Registrierungsvertrag, Helm-Ownership einer vorhandenen
ConfigMap, Feldbesitz von `manifest.json` sowie gleichzeitige `value`- und
`valueFrom`-Setter. Korrekturen und Schutzgrenzen sind im Gesamtstand beschrieben.
Ein erneuter Fehler ist gezielt zu diagnostizieren; weder Cluster/PVCs löschen
noch fremde ConfigMaps ungeprüft übernehmen. Image-Import oder ein erfolgreicher
Registrierungsjob allein beweist keinen erfolgreichen Helm-Rollout.

## Anmeldung und Settings

React `0.1.49` stabilisierte Token-Erneuerung/Settings-Entwürfe; `0.1.50`
trennt App-Root, Tenant-Pfad und OIDC-Callback und verhindert identische
Redirectschleifen. Sales nutzt diese gemeinsame Implementierung, keine eigene
Renewal-/Loginlogik. TLS umfasst auch nginx/Kestrel und interne Backchannels;
Entwicklungszertifikate benötigen eine vertrauenswürdige lokale Root-CA.

Die Einladung eines bereits bekannten Entra-Benutzers ist ein anderer Fehler:
die Korrektur liegt in Platform API/Tenant Portal, nicht in Sales oder einem
weiteren Library-Update. Tenant-Mitgliedschaft entsteht erst bei authentifizierter
Annahme; E-Mail-Gleichheit allein darf keine beliebigen Konten verknüpfen.

## Nächste Abnahme

Plattform separat aktualisieren, danach Sales separat deployen und den korrekten
Kontext prüfen. Anschließend HTTPS, Microsoft-Login/Einladung, Tenant-Wechsel,
Settings-Entwürfe bei Token-Erneuerung und Zoho-Callbacks live prüfen. Keine
Produktions-, Backup-/Restore- oder unbeaufsichtigte Pipeline-Freigabe aus den
lokalen Buildresultaten ableiten.
