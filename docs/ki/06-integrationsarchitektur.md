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
                              gemeinsame Fachschicht
                         Regeln, CRM-Tasks, KPIs, Ansichten
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
zoho_schema_cache
integration_entity_links
integration_sync_runs
integration_sync_cursors
integration_raw_records
integration_errors
integration_api_usage_events
```

- `integration_connections`: Anbieter und Mandantenverbindung, niemals rohe
  Secrets im Klartext.
- `zoho_schema_cache`: tenantisolierter Snapshot der für den verbundenen
  Zoho-Account verfügbaren Module, Felddefinitionen, Layouts,
  Pipeline-/Stufendefinitionen und Related-List-Metadaten. Dieser Snapshot wird
  ausschließlich durch den manuell startbaren Job `zoho-schema-cache`
  aktualisiert. Full- und Incremental-Sync sowie die Metadaten-API lesen nur
  den lokalen Snapshot und rufen Zoho-Settings nicht selbst auf. Ein
  fehlgeschlagener Refresh ersetzt den bisherigen Snapshot nicht.
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
- `integration_api_usage_events`: append-only Verbrauchsereignisse für jeden
  tatsächlichen ausgehenden CRM-HTTP-Versuch. Gespeichert werden Mandant,
  Verbindung, optionaler Joblauf (`RunId`), Herkunft (`Origin` = Job,
  Benutzeroberfläche oder System), auslösender Benutzer (`RequestedBy`),
  optionale Korrelation, normalisierter Endpoint, Kategorie, Status,
  Fehler-/Retry-Information, Dauer, betroffene Datensätze und die
  providerabhängige Kostenschätzung und Einheit. Externe Record-IDs werden nicht als
  Endpoint gespeichert. Ein UI-Aufruf hat bewusst keine `RunId`, wird aber über
  Herkunft, Benutzer und HTTP-Korrelation nachvollziehbar.

Die Verbrauchsschicht ist provider-neutral: Adapter melden ihre Versuche über
`ICrmApiUsageRecorder`; ein optionales `ICrmApiUsageCostModel` bewertet die
jeweiligen Einheiten des Providers. Der Zoho-Adapter liest zusätzlich den
Response-Header `X-API-CREDITS-REMAINING`, sofern Zoho ihn liefert. Die
mandantenbezogene 24-Stunden-Auswertung ist über `/api/integrations/usage`
verfügbar und wird auf der Importseite angezeigt. Die Zoho-Organisationszahl
im Anbieter-Dashboard bleibt für die Gesamtzahl maßgeblich, weil sie auch
andere Apps, Funktionen und manuelle Integrationen enthalten kann.

Ein HTTP-Middleware-Flush speichert auch Messungen aus normalen API-Requests;
die Full-, Incremental- und Schema-Cache-Jobs setzen zusätzlich ihre
Run-ID. Ein neuer Adapter muss daher nur seine echten HTTP-Versuche melden und
kann bei Bedarf ein eigenes Kostenmodell registrieren.

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
3. gecachtes Anbieter-Schema und Mapping laden; der Cache enthält auch
   Layouts, Pipelines/Stufen und Related Lists
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

Das Zoho-Schema wird nicht implizit beim Hochfahren oder bei einem CRM-Sync
geladen. Nach einer neuen Zoho-Verbindung oder einer Änderung des Zoho-Schemas
startet ein berechtigter Benutzer den Job `Zoho-Schema cachen` einmal manuell.
Ohne vorhandenen Snapshot wird ein Full- oder Incremental-Sync mit einer
verständlichen Fehlermeldung beendet, damit kein Sync ungeplant die Zoho-
Metadaten-API und deren Tageskontingent verbraucht.

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
Kunden, Deal oder Termin zeigen, werden mit dem Grund
`target-deleted-in-crm` geschlossen und nicht automatisch ohne Ziel ersetzt.
Eine gelöschte CRM-Task ist ein anderer Fall: Der verknüpfte lokale
Arbeitsvorgang wird geschlossen und als neue Instanz derselben
`WorkItemChainId` angelegt. Die neue Instanz wird über den Zoho-Adapter wieder
als CRM-Task angelegt und erhält deren neue Remote-ID. Alte Links bleiben mit
`SourceDeletedAt` für die Historie erhalten.

### Hintergrundjobs und Fortschritt

Die Regelbewertung nach dem CRM-Sync ist eine eigene Fortschrittsphase und wird
nicht erst beim Abschluss des Jobs sichtbar. Sie meldet Start, gepr��fte
Regelziele, verbleibende Menge und das Persistieren der Regelergebnisse als
Live-Fortschritt und Job-Logs. Danach werden CRM-Task-Abgleich, Kennzahlen-
Snapshot und Benachrichtigungen ebenfalls als laufende Nachverarbeitung mit
eigenen Fortschrittsstufen gemeldet.

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

Beide Eingangswege delegieren an `CrmSynchronizationService` bzw. an die
gemeinsame `ICrmBusinessChangeProcessor`-Fachschicht. Der Service
liest `crm.integration` aus der app-eigenen Tenant-Datenbank und wählt über
`CrmSynchronizationAdapterRegistry` genau einen `ICrmSynchronizationAdapter`.
Aktuell implementiert `ZohoSyncService` diese High-Level-Grenze. Paging,
Zoho-Module, Related-Lists, Mapping und OAuth liegen hinter dem Adapter; die
Jobs selbst enthalten keine Providerlogik. Der Adapter schreibt ausschließlich
über `ISalesCrmRepository` in das kanonische Modell. Danach wird unabhängig vom
Eingangsweg die Fachschicht aufgerufen. Sie führt die tenantbezogene Ableitung
der Anrufmarker, die gezielte oder vollständige Regelbewertung, die CRM-
Task-Spiegelung, Kennzahlen-Snapshots und den unmittelbaren Mailversand in
derselben Reihenfolge aus.

Webhooks verwenden zusätzlich die providerneutrale
`ICrmHookUpdateService`-Grenze. Der Platform-Job `CrmHookUpdateJob` dispatcht
an alle registrierten Anbieter-Services. Zoho kapselt darin ausschließlich
Registrierung, Erneuerung, Verifikation und das Lesen der gemeldeten Zoho-
Datensätze. Der Zoho-Payload wird über `ZohoCrmRecordMapper` in das kanonische
Modell übersetzt; danach konsumiert der Hook exakt dieselbe
`ICrmBusinessChangeProcessor`-Schicht wie Full- und Incremental-Crawl. Ein
späterer Pipedrive-Adapter liefert einen eigenen Mapper und eine eigene
Hook-Implementierung, muss aber keine Regel- oder UI-Logik kopieren. Ein
weiterer Anbieter registriert lediglich seinen eigenen
`ICrmHookUpdateService`; der gemeinsame Platform-Job übernimmt ihn automatisch.
Outbound-CRM-Tasks werden über `CrmAdapterRegistry` ebenfalls anhand des
Provider-Schlüssels aufgelöst; dadurch bleibt die Remote-ID-/Update-Logik
providerneutral.

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
Elternobjekte Accounts, Leads und Deals gelesen. Für die Related
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

### Zoho-Hooks als kostensparende Änderungserkennung

Zoho ist der erste Provider mit einer konkreten Subscription-Implementierung.
Die Tenant-App-Einstellung `crm.changeDetectionMode` steht standardmäßig auf
`hooks-plus-crawl`. Der Incremental-Crawl bleibt dabei als Sicherheitsnetz
aktiv; Hooks ersetzen ihn nicht vollständig. Mit `crawl-only` kann ein
Mandant die Hook-Verarbeitung abschalten.

Der technische Job `CRM-Hooks erneuern` läuft alle fünf Minuten. Er dispatcht
an die registrierten Hook-Update-Services. Diese erneuern ihre Subscriptions,
verarbeiten die wartenden Callback-Ereignisse und führen danach
die fachliche Nachverarbeitung aus. Es gibt keine separaten Jobs pro Modul,
keinen E-Mail-Job und keinen vollständigen Crawl als Reaktion auf einen Hook.
Das Callback selbst schreibt nur ein verifiziertes Ereignis in
`integration_webhook_events`; die Verarbeitung lädt ausschließlich die von
Zoho gemeldeten Remote-IDs. Der Verification-Token wird nicht gespeichert,
sondern nur als SHA-256-Hash in `integration_subscriptions`.

Für Sales relevante Zoho-Subscriptions sind:

| Zoho-Modul | Ereignisse | Reaktion in der SalesPlattform |
| --- | --- | --- |
| Users | all | Owner-/Vertreterauflösung und betroffene Zuständigkeiten neu bewerten |
| Leads | create, edit, delete | Lead-Regeln R-01 bis R-04 und R-09 gezielt bewerten |
| Accounts | create, edit, delete | Kundenpflege, Zuständigkeit, Cross-Selling und Account-Care neu bewerten |
| Deals | create, edit, delete | Deal-Inaktivität, Reaktivierung, Verträge und Cockpit-Kennzahlen aktualisieren |
| Products | create, edit, delete | Produktbeziehungen und produktbezogene Reports aktualisieren |
| Calls | create, edit, delete | Versuch/Gespräch anhand Dauer und Ergebnis neu berechnen; Lead-Regeln auswerten |
| Tasks | create, edit, delete | CRM-Task-Link und lokale Vorgangskette synchronisieren; bei Delete Nachfolger erzeugen |
| Events/Meetings/Appointments | create, edit, delete | Terminstatus, Verschiebungszähler und R-12 auswerten |
| Cases | create, edit, delete | Servicefall-Fristen und R-15 auswerten |
| Quotes | create, edit, delete | Angebots-Folgeaktion und R-16 auswerten |
| Sales_Orders | create, edit, delete | Lieferverzug und R-17 auswerten |
| Invoices | create, edit, delete | Überfälligkeit und R-18 auswerten |

Subscriptions werden nur für Module angelegt, die im lokalen Zoho-Schema-Cache
als verfügbar bekannt sind. Der Hook-Wartungslauf ruft daher keine Zoho-
Metadatenendpunkte auf. Kontakte werden weder synchronisiert noch als
Subscription registriert; ein eventuell veralteter Contacts-Webhook wird
verworfen. Für die Aktivierung sind die minimal nötigen Notifications-OAuth-
Berechtigungen `ZohoCRM.notifications.CREATE` und
`ZohoCRM.notifications.DELETE` sowie eine von Zoho erreichbare
`Zoho:WebhookUrl` erforderlich. Die CRM-Leserechte sind auf die tatsächlich
verwendeten Module begrenzt; für CRM-Aufgaben kommen nur
`ZohoCRM.modules.tasks.CREATE` und `ZohoCRM.modules.tasks.UPDATE` hinzu.
Bestehende OAuth-Verbindungen müssen nach der Scope-Änderung einmal neu
autorisiert werden.

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
5. dem vollständigen Initialimport für Owner, Accounts, Leads,
   Produkte, Pipelines, Stufen und Deals,
6. dem Import der Related-Lists für E-Mails und Deal-Stage-Historie,
7. dem Import von Calls, Tasks und Events/Terminen.

Der Import läuft als zentraler Anwendungsjob der Identity Platform. Das
Frontend erhält Status und Fehler über die gemeinsame Jobseite; Modul- und
Datensatzdetails stammen aus der tenantisolierten Sales-Datenbank.

Die Zoho-Anbindung ist damit die erste Implementierung eines allgemeinen
Integrationsmusters und kein Sonderweg für die gesamte Anwendung.
