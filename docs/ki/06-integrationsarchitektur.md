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

Lesen und Schreiben werden getrennt modelliert. Der Zoho-Adapter startet
read-only. Ein Anbieter darf nur die Fähigkeiten anbieten, die seine
Verbindung und die Freigabe des Mandanten erlauben.

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
- `integration_entity_links`: Zuordnung `(Provider, ExternalId)` zu einer
  kanonischen Entität; dadurch können Zoho- und Pipedrive-IDs koexistieren.
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
```

Der erste Lauf ist ein vollständiger Import. Danach laufen inkrementelle
Synchronisationen anhand des Änderungsstands des jeweiligen Anbieters. Jeder
Lauf muss wiederholbar sein: derselbe Datensatz darf bei einem Retry keine
Duplikate erzeugen.

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

Die nächste Implementierung baut nicht zuerst `ZohoDeal`-Tabellen, sondern:

1. provider-neutrale kanonische Tabellen und externe Identitäten,
2. Integrations- und Sync-Tabellen,
3. eine Adapter-Schnittstelle,
4. den read-only Zoho-Adapter,
5. einen ersten vollständigen Account-/Deal-Import,
6. danach Leads, Aktivitäten, Termine und Stage-Historie.

Die Zoho-Anbindung ist damit die erste Implementierung eines allgemeinen
Integrationsmusters und kein Sonderweg für die gesamte Anwendung.
