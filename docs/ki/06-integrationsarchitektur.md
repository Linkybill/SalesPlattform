# Integrationsarchitektur

## Architekturentscheidung

Das fachliche Domainmodell der SalesPlattform ist unabhängig vom jeweiligen
CRM-Anbieter. Alle persistenten Vertriebsdaten werden in der eigenen
tenant-isolierten Datenbank als kanonisches Modell gespeichert. Ein CRM-Adapter
liest die Daten aus dem jeweiligen Quellsystem, normalisiert sie und befüllt
das Domainmodell.

Zoho CRM ist der erste Adapter. Weitere Anbieter wie Pipedrive werden später
über eigene Adapter angeschlossen, ohne das Domainmodell oder die Regelengine
auf einen Anbieter zuzuschneiden.

```text
Zoho CRM ───────┐
Pipedrive ──────┼─> Anbieter-Adapter ─> Normalisierung/Mapping
weiteres CRM ───┘                              │
                                              v
                                  kanonisches Domainmodell
                                              │
                              Regeln, KPIs, Snapshots, Ansichten
```

Die Reports werden im Frontend als unabhängige Komponenten in einem direkt
bearbeitbaren Seitenbaum komponiert. Das Dashboard lädt eine tenantisolierte
Report-Projektion und rendert den gespeicherten Baum mit Grid, Tabs, Akkordeon,
Überschriften, Texten und Reports. Der Baum wird intern als JSON über die
Sales-API gespeichert, aber nicht als rohe Mandantenportal-Einstellung
angeboten. Er ist keine CRM-Integration und wird nicht in
`IdentityPlatform.Shared` als CRM-Fachlichkeit modelliert. `Shared` liefert
dafür nur die generische Tenant-, Settings- und Autorisierungsinfrastruktur.

## Schichten

```text
Web/API
  -> Application: Sync-Aufträge, Mapping, Upsert, Regeln
  -> Domain: Kunde, Lead, Deal, Pipeline, Aktivität, Termin, Ziel
  -> Ports: CRM lesen, Metadaten lesen, optional CRM schreiben
  -> Adapter: Zoho, später Pipedrive, ...
  -> Infrastruktur: PostgreSQL, Rohdaten, Sync-Status
```

### Domain

Das Domainmodell verwendet keine Zoho- oder Pipedrive-spezifischen Klassen und
Feldnamen. Fachliche Objekte sind beispielsweise:

- `Customer` / Kunde bzw. Organisation,
- `Contact` / Ansprechpartner,
- `Lead`,
- `Deal` / Verkaufschance,
- `Product` / Produktkategorie,
- `Pipeline` und `PipelineStage`,
- `Activity` / Anruf, Mail oder Aufgabe,
- `Appointment` / Termin,
- `Owner` / Mitarbeiter,
- `DealStageHistory`, Ziele, Snapshots und Wiedervorlagen.

Die Regelengine und KPI-Berechnung arbeiten ausschließlich auf diesen
kanonischen Objekten. Sie dürfen keine Zoho-JSON-Strukturen kennen.

### Adapter

Jeder Anbieter erhält eine eigene Implementierung, zum Beispiel:

```text
Integrations/
├── Abstractions/
│   ├── ICrmAdapter.cs
│   ├── ICrmMetadataProvider.cs
│   ├── ICrmReader.cs
│   └── ICrmWriter.cs
├── Zoho/
│   ├── ZohoCrmAdapter.cs
│   ├── ZohoTokenService.cs
│   ├── ZohoMetadataProvider.cs
│   └── ZohoRecordReader.cs
└── Pipedrive/             # später
    └── PipedriveAdapter.cs
```

Lesen und Schreiben werden getrennt modelliert. Die CRM-Stammdaten bleiben
read-only führend in Zoho; der Zoho-Adapter bietet zusätzlich ausschließlich
die explizite Anlage der spiegelnden CRM-Tasks für lokale Arbeitsvorgänge an.
Weitere Schreibfähigkeiten dürfen nur als eigene, freigegebene Capability
hinzukommen.

### Normalisierung und Mapping

Der Adapter übersetzt Anbieter-Datensätze in interne DTOs. Eine separate
Mapping-Schicht wandelt diese DTOs in Domainobjekte um. Dadurch bleiben
Anbieterbesonderheiten außerhalb des Domainmodells.

Beispiel:

```text
Zoho Deals.Stage          -> Deal.StageId
Zoho Deals.Amount         -> Deal.Amount
Zoho Deals.Account_Name   -> Deal.CustomerId
Pipedrive deals.status    -> Deal.StageId
Pipedrive org_id          -> Deal.CustomerId
```

Die tatsächlichen Feldnamen werden je CRM-Organisation aus Metadaten und
Konfiguration ermittelt; sie werden nicht blind aus Bezeichnungen wie
`produkt` oder `vertragsende` abgeleitet.

## Persistenz

Die Datenbank wird in drei Bereiche gegliedert:

### Kanonische Fachdaten

```text
customers
contacts
leads
deals
products
pipelines
pipeline_stages
activities
appointments
owners
deal_stage_history
```

Diese Tabellen sind die einzige Datenquelle für Regelengine, KPIs und
Frontend. Jede Entität bleibt tenant-isoliert.

### Integrationsdaten

```text
integration_connections
integration_entity_links
integration_sync_runs
integration_sync_cursors
integration_raw_records
integration_errors
```

- `integration_connections`: Anbieter und Mandantenverbindung, niemals rohe
  Secrets im Klartext.
- `integration_entity_links`: Zuordnung `(Provider, Connection, EntityType,
  ExternalId)` zu einer kanonischen Entität; dadurch können Zoho- und
  Pipedrive-IDs koexistieren. `SourceDeletedAt` hält ein Quell-Delete
  historisch fest. Eine optionale `WorkItemId` verknüpft eine von der
  SalesPlattform erzeugte CRM-Task mit genau einer lokalen
  Arbeitsvorgangsinstanz.
- `integration_sync_cursors`: letzter erfolgreicher Änderungsstand je Anbieter,
  Mandant und Entitätstyp.
- `integration_raw_records`: optionales `jsonb`-Original für Debugging,
  Reprocessing und Nachvollziehbarkeit.
- `integration_sync_runs` und `integration_errors`: Status, Zähler, Laufzeit
  und Fehler eines Imports.

### Fachliche Ableitungen

```text
rule_items / wiedervorlagen
daily_snapshots
targets
configuration
duplicate_candidates
merge_decisions
audit_log
```

Diese Tabellen werden aus dem kanonischen Modell berechnet und nicht direkt
aus Zoho- oder Pipedrive-JSON befüllt.

## Synchronisationsablauf

```text
1. Connection und Provider auswählen
2. Adapter authentifiziert sich
3. Metadaten und Mapping laden
4. Datensätze paginiert lesen
5. Rohdatensatz optional speichern
6. Anbieter-Datensatz normalisieren
7. External-Link auflösen oder anlegen
8. kanonische Entität idempotent upserten
9. Stage-Historie und Änderungen speichern
10. Sync-Cursor und Ergebnis protokollieren
11. Regeln/KPIs für betroffene Daten neu berechnen
12. aktive lokale Arbeitsvorgänge als CRM-Tasks spiegeln und ihre Remote-IDs
    verknüpfen
```

### Remote-IDs und Löschabgleich

Der Remote-Key bleibt über den gesamten Lebenszyklus erhalten. Normale
Upserts suchen zuerst den bestehenden Integrationslink und aktualisieren dann
die vorhandene kanonische Entität; ein Full-Crawl ist kein physischer
Neuaufbau. Zoho liefert inkrementelle Löschungen über den jeweiligen
`/<module>/deleted`-Endpunkt. Zusätzlich prüft ein erfolgreicher Full-Crawl
fehlende aktive IDs, damit auch bereits aus dem Delete-Fenster entfernte
Datensätze erkannt werden.

Lokale CRM-Stammdaten werden bei einem Quell-Delete historisch behalten, aber
inaktiv markiert. Offene Arbeitsvorgänge, die auf einen gelöschten Lead,
Kunden, Kontakt, Deal oder Termin zeigen, werden mit dem Grund
`target-deleted-in-crm` geschlossen und nicht automatisch ohne Ziel ersetzt.
Eine gelöschte CRM-Task ist ein anderer Fall: Der verknüpfte lokale
Arbeitsvorgang wird geschlossen und als neue Instanz derselben
`WorkItemChainId` angelegt. Die neue Instanz wird über den Zoho-Adapter wieder
als CRM-Task angelegt und erhält deren neue Remote-ID. Alte Links bleiben mit
`SourceDeletedAt` für die Historie erhalten.

### Hintergrundjobs und Fortschritt

Ein Sync läuft nie innerhalb einer lang laufenden Browseranfrage. Die
SalesPlattform registriert `CrmFullImportJob` und `CrmIncrementalCrawlJob` über
das Shared-NuGet. Die Identity Platform verwaltet Definitionen, tenantbezogene
Cron-Konfiguration, dauerhafte RabbitMQ-Zustellung, jeden Run und seine Events.
Der Shared-Worker setzt beim Verarbeiten den Tenant-Kontext und ruft die
registrierte Implementierung aus dem DI-Scope auf.
Der Worker meldet während der Ausführung regelmäßig einen Heartbeat. Die
Platform bereinigt `queued`- oder `running`-Läufe ohne Heartbeat-Lease nach
90 Sekunden beim Auflisten, beim Start eines neuen Laufs und im Scheduler.
Damit kann ein abgestürzter Worker keinen neuen CRM-Sync dauerhaft blockieren;
fachliche Sync-Historie und Datensatzfehler bleiben weiterhin in der
tenantisolierten Sales-Datenbank.

Beide Implementierungen delegieren an `CrmSynchronizationService`. Der Service
liest `crm.integration` aus der app-eigenen Tenant-Datenbank und wählt über
`CrmSynchronizationAdapterRegistry` genau einen `ICrmSynchronizationAdapter`.
Aktuell implementiert `ZohoSyncService` diese High-Level-Grenze. Paging,
Zoho-Module, Related-Lists, Mapping und OAuth liegen hinter dem Adapter; die
Jobs selbst enthalten keine Providerlogik. Der Adapter schreibt ausschließlich
über `ISalesCrmRepository` in das kanonische Modell.

Fachlicher Detailfortschritt wird weiterhin laufbezogen in
`integration_sync_runs`, `integration_sync_run_items` und
`integration_sync_errors` gespeichert. Modulstände und Fehler werden als
providerneutrale Jobdetails an die Platform gemeldet. Die gemeinsame React-
Library zeigt `/jobs` im App-Header, konfiguriert Zeitpläne und beobachtet Läufe
über den zentralen SignalR-Hub. Beim Öffnen eines Laufs erscheinen die
Ereignisse live; strukturierte Details können als JSON aufgeklappt werden.
Ein Lauf kann aus derselben Detailansicht abgebrochen werden. Die Abbruchkette
läuft von Platform API und Outbox über den Shared-Worker bis zum
`CancellationToken` des CRM-Adapters. Die früheren Zoho-spezifischen Queue-,
Worker- und SignalR-Komponenten existieren nicht mehr.

Jeder CRM-Lauf meldet zuerst seinen vollständigen Modulplan und danach den
aktuellen Modulstatus. Die Fortschrittswerte unterscheiden gelesene,
geschriebene und fehlgeschlagene Records; aus diesen Werten wird die
Restmenge je Modul berechnet. Der Abschluss übergibt die geschriebenen Records
zusätzlich als strukturierte `writtenRecords`-Liste in den Plattformdetails.
Die Plattform ist für Transport, Speicherung und Live-Anzeige zuständig; die
SalesPlattform liefert fachliche Schritte, Zähler, Fehler und Payload-Details.

Der erste Lauf ist ein vollständiger Import. Danach laufen inkrementelle
Synchronisationen anhand des Änderungsstands des jeweiligen Anbieters. Jeder
Lauf muss wiederholbar sein: derselbe Datensatz darf bei einem Retry keine
Duplikate erzeugen.

E-Mails und Deal-Stage-Historie bleiben Related-List-Module desselben
CRM-Sync-Jobs. E-Mails werden beim Iterieren der geänderten bzw. vollständigen
Elternobjekte Accounts, Kontakte, Leads und Deals gelesen. Für die Related
Lists gelten vier parallele Leseoperationen und batchweise Writes; diese
Begrenzung schützt die Provider-API und die Tenant-Datenbank, während die
Elternbeziehungen erhalten bleiben. Ein separater E-Mail-Sync- oder
E-Mail-Versandjob ist fachlich nicht vorgesehen. Regelbenachrichtigungen werden
unmittelbar nach der Regelbewertung im selben Full- bzw. Incremental-Lauf
versendet; die Outbox dient nur der idempotenten Zustellung und dem Retry.

Auch die belastbaren Kennzahlen werden in diesem Lauf aktualisiert: Nach
Synchronisation und Regelbewertung wird der Tages-Snapshot für den aktuellen
Tenant neu berechnet. Dadurch gibt es für Mailversand und Kennzahlen keine
separaten Zeitpläne und keine unabhängigen Sales-Jobs.

### Exklusive Prozessgruppen

Jobs, die dieselben fachlichen Daten verändern und deshalb nicht parallel laufen
dürfen, deklarieren eine gemeinsame `ConcurrencyGroup`. Die Gruppe ist aktuell
mandantenbezogen und erlaubt genau einen aktiven oder eingeplanten Lauf. Der
CRM-Vollimport und der inkrementelle Crawl verwenden beide die Gruppe
`crm-synchronization`.

Die Identity Platform prüft die Gruppe beim manuellen Einplanen, im Scheduler
und unmittelbar vor dem Worker-Start unter derselben Datenbank-Sperre. Ein
konkurrierender manueller Lauf wird abgewiesen. Zeitgesteuerte Läufe bleiben
fällig und werden eingeplant, sobald die Gruppe frei ist; ein verwaister Lauf
wird zuerst über die bestehende Heartbeat-Prüfung bereinigt. Die zentrale
Job-UI zeigt den blockierenden Lauf an. Sales bleibt für fachlichen Fortschritt,
Logs, Datensätze und Fehler zuständig, besitzt aber keine zweite konkurrierende
Sperrlogik.

## Mandanten und Verbindungen

Eine CRM-Verbindung gehört immer zu einem Mandanten. Das Modell soll außerdem
mehr als eine Verbindung je Mandant zulassen, damit später beispielsweise
verschiedene CRM-Organisationen oder Anbieter parallel möglich sind.

```text
Tenant A -> Zoho Production
Tenant B -> Zoho Production
Tenant A -> später zusätzlich Pipedrive
```

Die Konfiguration folgt dem Application-Settings-Muster der Identity Platform:

- `crm.integration` liegt auf der Ebene `tenantApp` und wählt die allgemeine
  CRM-Integration des Mandanten. Aktuell sind `none` und `zoho` möglich.
- `zoho.datacenter`, `zoho.clientId` und `zoho.clientSecret` liegen ebenfalls
  auf `tenantApp`, werden im Tenant-Portal unter `AppSettings` nur bei Auswahl
  von `zoho` eingeblendet und provider-spezifisch gepflegt.
- `zoho.clientSecret` ist als `secret` definiert und wird verschlüsselt in der
  tenant-isolierten Sales-Datenbank gespeichert. Das Tenant Portal liest und
  schreibt diese Einstellung über seine Platform-API; diese proxied intern zum
  gemeinsamen Settings-Endpunkt des Sales-Backends. Der Secret-Wert wird nie
  in einer API-Antwort zurückgegeben.
- Der OAuth-Codeaustausch und die Erneuerung von Zoho-Access-Tokens erfolgen in
  der SalesPlattform. Der OAuth-Refresh-Token wird nach erfolgreicher
  Autorisierung über den gemeinsamen Secret-Store verschlüsselt und
  tenantbezogen in derselben Sales-Datenbank abgelegt. Die SalesPlattform
  besitzt dafür einen eigenen `TokenProtectionKey`, der ausschließlich als
  Kubernetes Secret injiziert wird.
- Der zentrale Credential-Dienst kennt nur Provider- und Verbindungs-Schlüssel;
  er kennt keine Zoho-URLs, Zoho-Settings und führt keine provider-spezifischen
  Tokenaufrufe aus. Das Zoho-Client-Secret wird von der SalesPlattform für den
  OAuth-Aufruf transient aus dem app-eigenen Secret-Store gelesen.

Das Frontend erhält niemals Access- oder Refresh-Tokens. Pipedrive und weitere
Adapter verwenden später dieselbe Trennung aus mandantenbezogener Konfiguration,
geheimer Credential-Ablage und provider-neutralem Domainmodell.

## Rückschreiben

Rückschreiben ist ein separates, optionales Port-/Capability-Thema. Das
Domainmodell darf eine Wiedervorlage oder einen Besitzerwechsel fachlich
ändern, ohne zu unterstellen, dass jeder Anbieter diese Änderung unterstützt.

```text
Domainänderung
  -> optionaler ICrmWriter
  -> Anbieter-API, falls Capability und Freigabe vorhanden
  -> erneuter Sync zur Bestätigung
```

Besitzerwechsel, Statusänderungen und Dubletten-Merges bleiben an die im
Pflichtenheft beschriebenen menschlichen Freigaben gebunden.

## Konsequenz für die Umsetzung

Die Initialimport-Implementierung besteht aus:

1. provider-neutrale kanonische Tabellen und externe Identitäten,
2. Integrations- und Sync-Tabellen,
3. eine Adapter-Schnittstelle,
4. den read-only Zoho-Adapter,
5. dem vollständigen Initialimport für Owner, Accounts, Kontakte, Leads,
   Produkte, Pipelines, Stufen und Deals,
6. dem Import der Related-Lists für E-Mails und Deal-Stage-Historie,
7. dem Import von Calls, Tasks und Events/Terminen.

Der Import läuft als zentraler Anwendungsjob der Identity Platform. Das
Frontend erhält Status und Fehler über die gemeinsame Jobseite; Modul- und
Datensatzdetails stammen aus der tenantisolierten Sales-Datenbank.

Die Zoho-Anbindung ist damit die erste Implementierung eines allgemeinen
Integrationsmusters und kein Sonderweg für die gesamte Anwendung.
