# SalesPlattform

### Release- und Rebuild-Stand (08.09.2026)

Lokal ist HTTPS ueber die Plattform-Traefik-Kette ausgerollt; aktuelle
App-Images: `0.1.4`. `rebuild-all.ps1` liest den Default-Tag jetzt aus
dem Manifest und lehnt abweichende Tags ab. Erst nach erfolgreichen Builds
und serialisiertem K3d-Import werden laufende Instanzen ersetzt.

`.github/workflows/release.yml` ist der manuelle, geschuetzte GitHub-Actions-
Release-Weg fuer `hetzner-test`/spaeter `production`: CI, GHCR-Images mit
passendem Zielmanifest, SSH/Helm-Deployment, Controller- und HTTPS-Pruefung.
Er setzt eine bereitgestellte Identity Platform voraus und installiert nicht
selbststaendig Ubuntu/K3s/Vault. Einrichtung und offene Produktionsvoraussetzungen
stehen zentral in `IdentityPlattform/deploy/cicd/README.md`.
Abgleich 10.09.2026: Hetzner `ax42-1` / `176.9.57.203` ist eingerichtet;
K3s und Plattform wurden dort bereits betrieben. Ein erfolgreicher Gesamtlauf
dieser Release-Workflows ist nicht nachgewiesen. CI-Environments, separate
CI-SSH-Schlüssel und Paket-/Registry-Zugriff bleiben gesondert abzunehmen.
Aktueller Code-/Test-/Rolloutstand: [KI-Betriebsstand](docs/ki/09-deployment-und-betriebsstand.md).
Lokale Builds/Tests ausschließlich unter Windows PowerShell, kein WSL.

Der Release-Workflow übernimmt die erzeugte `deployment-config.js` vor dem
Image-Build nach `frontend/public/assets`. App-, Platform-API- und Tenant-Portal-
Build-URLs stammen aus `release/values.json`. Produktion übergibt zusätzlich
`vars.PUBLIC_APPLICATION_URL` an das gepinnte Plattform-Tooling. CI prüft den
Root-/Runtime-Vertrag mit der installierten React-Library.

Die fachliche Zielbeschreibung liegt als tokenfreundliches Markdown-
Pflichtenheft unter
[`docs/pflichtenheft/Vertriebstool_Spezifikation.md`](docs/pflichtenheft/Vertriebstool_Spezifikation.md).
Die daraus abgeleiteten, für KI-Agenten optimierten Infodateien liegen unter
[`docs/ki/`](docs/ki/). Der aktuelle technische Stand und die offenen
Entscheidungen sind dort getrennt vom Zielumfang dokumentiert.

Kleines Startgerüst für eine Anwendung auf der Identity Platform. Die
Anwendung besteht aktuell aus:

- einem React-Frontend,
- einem geschützten ASP.NET-Core-Endpunkt `GET /api/hello-world`,
- einer plattformgesteuerten EF-Core-Tenant-Datenbank.

Die Zoho-Anbindung ist als provider-neutraler CRM-Adapter umgesetzt; Zoho ist
der erste Provider. OAuth-Verbindungen werden pro Tenant gespeichert und der
vollständige fachliche Initialimport von Ownern, Accounts, Kontakten, Leads,
Produkten, Pipelines, Deals, Aktivitäten, Terminen, E-Mails und
Stage-Historien wird im kanonischen SalesPlattform-Modell abgelegt. Weitere
Provider wie Pipedrive können später denselben Adaptervertrag implementieren.

Vollimport und inkrementelle Synchronisation sind zentrale Anwendungsjobs der
Identity Platform. Die Plattform besitzt Zeitpläne, durable RabbitMQ-Zustellung,
Run-Historie, Live-Logs, strukturierte Laufdetails und SignalR-Live-Status. Die
gemeinsame Detailansicht erlaubt auch den echten Abbruch eines aktiven Laufs.
Die SalesPlattform registriert nur zwei
providerneutrale Implementierungen: `crm-full-import` ist je Tenant per UI
konfigurierbar (Standard täglich), `crm-incremental-crawl` läuft fest alle
15 Minuten. Die gemeinsame Route `/jobs` wird von der React-Plattformlibrary
im Header eingeblendet und ist Tenant-Admins vorbehalten.

Beide CRM-Jobs verwenden die mandantenbezogene Exklusivgruppe
`crm-synchronization`; Vollimport und Incremental-Crawl laufen daher nicht
parallel. Vor jedem neuen Lauf wird zusätzlich geprüft, ob ein gespeicherter
app-eigener Sync-Lauf noch einen aktiven Plattformlauf besitzt. Verwaiste
Läufe werden bereinigt. E-Mails bleiben Related-Lists desselben CRM-Laufs und
sind kein eigener Synchronisationsjob.

Der Initialimport umfasst alle für das Pflichtenheft benötigten Zoho-Daten und
läuft als idempotenter Plattformjob. Der inkrementelle Crawl liest jedes
Quellmodul ab seinem letzten erfolgreichen Cursor, mit Überlappungsfenster und
Soft-Delete-Verarbeitung. Nach einer Erweiterung der Zoho-Scopes
muss die Verbindung einmal über „Zoho verbinden“ neu autorisiert werden, damit
Benutzer, Pipelines, E-Mails und Stage-Historien gelesen werden dürfen.

Provider-Webhooks werden später ergänzend an denselben neutralen Sync-Pfad
angebunden. Sie liefern Änderungen sofort; der 15-Minuten-Crawl schließt
verpasste Events, der Vollimport dient der periodischen Reconciliation.

## Voraussetzungen

Für die lokale Entwicklung liegt das Identity-Platform-Repository neben
diesem Repository, zum Beispiel unter `C:\git`:

```text
C:\git\
├── IdentityPlattform/
└── SalesPlattform/SalesPlattform/
```

Das Backend verwendet das private NuGet-Paket `IdentityPlatform.Shared` und das Frontend
`@hammer2fall/identity-platform-react` aus diesem Nachbar-Repository.

Für den NuGet-Restore wird ein GitHub-PAT mit `read:packages` benötigt. Das
Token wird nur in der aktuellen nativen PowerShell-Sitzung gesetzt:

    $env:GITHUB_PACKAGES_TOKEN = '<PAT>'
    $env:NuGetPackageSourceCredentials_github = "Username=github;Password=$env:GITHUB_PACKAGES_TOKEN"

## Lokal starten

Zuerst die Identity Platform gemäß deren Dokumentation starten und die
Anwendung für einen Tenant aktivieren. Danach:

```powershell
.\deploy-all.ps1 -Target local -Environment dev
```

Das Skript baut ausschließlich Sales-Frontend/-Backend, niemals Plattform oder
Aufmass. Lokale `rebuild-all.ps1`-Aufrufe delegieren an das eigene deploy-all.
`-Preview` ist read-only; `-ClusterNamePrefix abc` wählt die vorhandene Zuordnung.
Es importiert die Images in den vorhandenen
K3d-Cluster und startet die Sales-Deployments. Es benötigt die vorherige Zuordnung
durch `deploy-all.ps1 -Target local -Environment dev` im Plattform-Repository.
Der Root-URL-Vertrag vom 09.09.2026 lautet:

| Target/Environment | Operator | Tenant-Portal | SalesPlattform |
|---|---|---|---|
| local/dev | `https://127.0.0.1:3000` | `https://127.0.0.1:3001` | `https://127.0.0.1:3003` |
| ax42-1/dev | `https://176.9.57.203:3000` | `https://176.9.57.203:3001` | `https://176.9.57.203:3003` |

Der Einstieg ist `/`, tenantbezogene Aufrufe verwenden `/{tenantId}/...`.
Die Shared-Library liest den Tenant aus dem Browserpfad. `appsettings.Deployment.json`
definiert `Frontend.Port=3003` ohne Pfadzusatz (Root `/`) für beide Targets; den Host liefert
das Plattform-Target. Remote/dev läuft über den eigenen Aufruf
`deploy-all.ps1 -Target ax42-1 -Environment dev` in diesem Sales-Ordner.
Die Plattform vorher separat deployen. Der App-Origin wird über das Manifest
registriert, nicht als zweite Liste im Plattformskript gepflegt. Der Aufruf
prüft die installierte Fähigkeit `ApplicationIngress__Contract=registration-v1`;
alte/fehlende Plattformen werden nicht automatisch installiert. Diese Trennung
ist offline getestet, noch nicht ausgerollt. Details:
`IdentityPlattform/docs/solution-deployments.md`.

Rebuild, Runtime-JS, gemountetes Manifest und HTTPS-Abnahme konsumieren denselben
geprüften Plan. Die App-Origin enthält keinen abschließenden Slash. Platform-API
und Tenant-Portal kommen ausschließlich aus dem Profil; `VITE_*`-Prozesswerte
überschreiben den Rebuild-Plan nicht. Das Frontend lädt
`assets/deployment-config.js` ohne Cache vor dem App-Modul. Dieses öffentliche
Runtime-Profil hat Vorrang vor Build-Werten; fehlende Plattform-/Portalwerte
werden gemeldet. `VITE_API_BASE_URL` bleibt der Sales-Name für die App-Wurzel.

OIDC-Callbacks werden an die Root-Origin als `/auth/callback`,
`/auth/silent-callback` und `/auth/logout-callback` angehängt. Web-Origins enthalten
nur die Origin. Traefik, Zertifikatsvertrauen und interne HTTPS-Verbindungen
einschließlich Keycloak-Backchannel stellt die Identity Platform bereit.

Direktes Vite verwendet Port 3003 mit `strictPort`. `frontend/.env.example`
enthält den App-Default und die aus dem Profil zu übernehmenden Pflichtwerte.
HTTPS und Profilversorgung für direktes Vite müssen zentral eingerichtet sein;
`npm run dev` allein stellt diesen Vertrag noch nicht her. Kubernetes/K3s und
Helm bleiben der unterstützte Betriebsweg.

Offline-Prüfungen ohne Build oder Deployment:

```powershell
.\tests\Test-LocalHttps.ps1
node --test tests/RootUrls.test.mjs
node frontend/node_modules/typescript/bin/tsc -p frontend/tsconfig.app.json --noEmit --pretty false
node frontend/node_modules/typescript/bin/tsc -p frontend/tsconfig.node.json --noEmit --pretty false
```

Das Backend benötigt .NET 10. Die Registrierung bei der Platform erfolgt über
das Secret `IdentityPlatform__RegistrationSecret`; dieses Secret gehört nicht
ins Repository.

### Zoho CRM konfigurieren

Die CRM-Anbindung wird mandantenbezogen über die Anwendungseinstellungen der
Identity Platform gepflegt. Im Tenant-Portal bei der Zuordnung
`SalesPlattform` zu einem Mandanten wird unter `AppSettings` zunächst die
allgemeine `CRM-Integration` ausgewählt. Aktuell steht dort `Keine
CRM-Integration` oder `Zoho CRM` zur Verfügung. Bei Auswahl von `Zoho CRM`
werden die Zoho-Client-Einstellungen eingeblendet: `Zoho Datacenter`, `Zoho
Client-ID` und `Zoho Client-Secret`. Weitere Anbieter wie HubSpot oder
Pipedrive können später als zusätzliche Auswahl und eigener Adapter ergänzt
werden. Das Client-Secret ist ein Secret-Setting: Es wird verschlüsselt
gespeichert und nie an Frontend oder normale API-Antworten ausgegeben.

Der OAuth-Client in Zoho muss als Server-based Application registriert sein.
Die Redirect-URL wird aus der App-Origin des Profils abgeleitet:

- local/dev: `https://127.0.0.1:3003/api/integrations/zoho/oauth/callback`
- ax42-1/dev: `https://176.9.57.203:3003/api/integrations/zoho/oauth/callback`

Die externe Zoho-Client-Konfiguration muss die jeweilige Adresse erlauben.
Der Rebuild setzt `Zoho__RedirectUri` und `Zoho__FrontendCallbackUrl` aus dem
gleichen Plan; die Frontend-Rückkehr liegt unter `/import`. Er ändert keine
Zoho-App-Registrierung. OAuth-Secrets und Refresh-Tokens bleiben erhalten.

Der Zoho-Refresh-Token wird nicht in der SalesPlattform und nicht in ihrer
Tenant-Datenbank gespeichert. Die SalesPlattform führt den Zoho-OAuth-
Codeaustausch und die Erneuerung des Zoho-Access-Tokens selbst durch. Den
Refresh-Token legt sie über die allgemeine, provider-neutrale Credential-API
der Identity Platform verschlüsselt und tenantbezogen ab bzw. liest ihn für
die Erneuerung transient aus. Die Identity Platform kennt dabei weder Zoho-
Endpunkte noch Zoho-spezifische Einstellungen.

Beim lokalen Docker-Desktop/K3d-Start benötigt das Sales-Backend deshalb kein
Zoho-spezifisches Secret. Es verwendet nur das vorhandene
`ServiceAuthentication` für die interne Kommunikation mit der Plattform; das
Client-Secret wird aus `<PlatformName>-secrets`, Schlüssel
`PORTALAPP_CLIENT_SECRET`, als `ServiceAuthentication__ClientSecret` injiziert. Das
`IdentityPlatform:RegistrationSecret` bleibt auf Manifestregistrierung und
Datenbank-Binding beschränkt. Zusätzlich müssen Zoho-Redirect-URL und
`FrontendCallbackUrl` auf die echte öffentliche HTTPS-Adresse der Installation
gesetzt werden; die 127.0.0.1-Werte im Bootstrap sind nur für den lokalen
Docker-Desktop/Kubernetes-Betrieb. Client-ID und Client-Secret bleiben dabei
mandantenbezogene Application Settings.

### CRM-Benutzerzuordnung

Die Arbeitsliste kann einen Plattform-Benutzer explizit einem synchronisierten
CRM-Besitzer zuordnen. Tenant-Administratoren öffnen dafür in der Sales-App den
Menüpunkt `Einstellungen`. Die Zuordnung wird tenantbezogen in der Einstellung
`crm.ownerMappings` gespeichert; die Oberfläche verwendet die stabile
Plattform-Subject-ID und validiert den ausgewählten aktiven CRM-Besitzer.

Die App-Rolle `sales-manager` / „Vertriebsleitung“ erhält eine tenantweite
Arbeitslistenansicht und benötigt dafür kein persönliches CRM-Owner-Mapping.

## Datenbank

Die Datenbank wird nicht als eigene Infrastruktur in der Sales-Plattform
konfiguriert. Wie bei der HelloWorld-Referenz fordert das Manifest eine
plattformgesteuerte Datenbank an. Die Identity Platform provisioniert das
Binding pro Tenant; `SalesPlattformDbContext` öffnet dieses Binding und liest
die Tabelle `hello_world_records` tenant-isoliert.

## Container-Builds

Das Rebuild-Skript setzt die separat installierte Plattform voraus, baut beide Images und
importiert sie in den lokalen K3d-Cluster:

```powershell
.\rebuild-all.ps1
```

Optional können Image-Tag, Kubernetes-Kontext und Cache gesteuert werden:

```powershell
.\rebuild-all.ps1 -KubeContext k3d-identity-platform -NoCache
```

Ein explizites `-Tag` muss zu allen `imageTag`-Werten im Manifest passen.
Beide Dockerfiles verwenden das Sales-Repository als Build-Kontext. Shared-.NET
und Shared-React werden als NuGet-/npm-Pakete ueber GitHub Packages bezogen.
Direkte Builds (Tag aus dem Manifest, Import/Deploy danach weiterhin ueber das Skript):

```powershell
Set-Location C:\git\SalesPlattform\SalesPlattform
$appImageTag = (Get-Content backend/manifest.json -Raw | ConvertFrom-Json).imageTag
docker build --secret id=github_packages_token,env=GITHUB_PACKAGES_TOKEN -f backend/Dockerfile -t "identity-platform/sales-plattform-backend:$appImageTag" .
docker build --secret id=github_packages_token,env=GITHUB_PACKAGES_TOKEN -f frontend/Dockerfile -t "identity-platform/sales-plattform-frontend:$appImageTag" .
```

Das `backend/manifest.json` ist der technische Vertrag für die Registrierung,
die Client-URLs und die beiden App-Komponenten.
