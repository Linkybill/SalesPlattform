# SalesPlattform

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
Set-Location .\frontend
npm install
npm run dev
```

Das Frontend ist anschließend unter `http://localhost:3100` erreichbar. Für
den Zugriff über den Identity-Platform-Router kann die API-Basis über
`VITE_API_BASE_URL` gesetzt werden.

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
Als Redirect-URL wird exakt diese URL benötigt:

    http://localhost:3101/apps/sales-plattform/api/integrations/zoho/oauth/callback

Der Zoho-Refresh-Token wird nicht in der SalesPlattform und nicht in ihrer
Tenant-Datenbank gespeichert. Die SalesPlattform führt den Zoho-OAuth-
Codeaustausch und die Erneuerung des Zoho-Access-Tokens selbst durch. Den
Refresh-Token legt sie über die allgemeine, provider-neutrale Credential-API
der Identity Platform verschlüsselt und tenantbezogen ab bzw. liest ihn für
die Erneuerung transient aus. Die Identity Platform kennt dabei weder Zoho-
Endpunkte noch Zoho-spezifische Einstellungen.

Beim lokalen Docker-Desktop/K3d-Start benötigt das Sales-Backend deshalb kein
Zoho-spezifisches Secret. Es verwendet nur das vorhandene
`IdentityPlatform:RegistrationSecret` für die interne Kommunikation mit der
Plattform. Zusätzlich müssen Zoho-Redirect-URL und
`FrontendCallbackUrl` auf die echte öffentliche HTTPS-Adresse der Installation
gesetzt werden; die localhost-Werte im Bootstrap sind nur für den lokalen
Docker-Desktop/Kubernetes-Betrieb. Client-ID und Client-Secret bleiben dabei
mandantenbezogene Application Settings.

## Datenbank

Die Datenbank wird nicht als eigene Infrastruktur in der Sales-Plattform
konfiguriert. Wie bei der HelloWorld-Referenz fordert das Manifest eine
plattformgesteuerte Datenbank an. Die Identity Platform provisioniert das
Binding pro Tenant; `SalesPlattformDbContext` öffnet dieses Binding und liest
die Tabelle `hello_world_records` tenant-isoliert.

## Container-Builds

Das Rebuild-Skript übernimmt den Kubernetes-Bootstrap, baut beide Images und
importiert sie in den lokalen K3d-Cluster:

```powershell
.\rebuild-all.ps1
```

Optional können Image-Tag, Kubernetes-Kontext und Cache gesteuert werden:

```powershell
.\rebuild-all.ps1 -Tag 0.1.1 -KubeContext k3d-identity-platform -NoCache
```

Die Dockerfiles verwenden das Sales-Repository als Build-Kontext und binden das
Identity-Platform-Repository als separaten Build-Kontext nur für das Frontend ein.
Das Backend stellt `IdentityPlatform.Shared` ausschließlich als NuGet-Paket über
GitHub Packages bereit. Der direkte Build
entspricht dem Ablauf im Skript:

```powershell
Set-Location C:\git\SalesPlattform\SalesPlattform
docker build --secret id=github_packages_token,env=GITHUB_PACKAGES_TOKEN -f backend/Dockerfile -t identity-platform/sales-plattform-backend:0.1.0 .
docker build --build-context identity=..\..\IdentityPlattform -f frontend/Dockerfile -t identity-platform/sales-plattform-frontend:0.1.0 .
```

Das `backend/manifest.json` ist der technische Vertrag für die Registrierung,
die Client-URLs und die beiden App-Komponenten.
