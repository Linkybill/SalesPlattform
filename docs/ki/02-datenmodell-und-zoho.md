# Datenmodell, Zoho und Synchronisation

## Systemgrenzen

Die erste Zielintegration ist Zoho CRM als lesende Quelle. Zoho ist dabei nur
der erste Anbieter-Adapter, nicht das Domainmodell. Die kanonischen
Sales-Daten liegen in der eigenen Datenbank; spätere Adapter wie Pipedrive
befüllen dieselben Tabellen. Das CRM ist für Stammdaten und Geschäftsprozesse
führend.

Die SalesPlattform darf nicht bei jedem Seitenaufruf live auf Zoho zugreifen,
sondern arbeitet mit einer eigenen Datenbank und periodischer Synchronisation.
Gründe sind API-Limits, Antwortzeiten und die dauerhafte Speicherung
historischer Daten. Anbieter-IDs werden über eine externe Identitätszuordnung
vom kanonischen Domainmodell getrennt.

## Sync-Zeitplan

- `crm-full-import`: tenantadmin-konfigurierbarer Standardzeitplan täglich um
  02:00 Uhr; zusätzlich manuell startbar.
- `crm-incremental-crawl`: fester Plattformzeitplan alle 15 Minuten; zusätzlich
  manuell startbar.
- Definition, Zeitplan, Queue, Run-Historie und SignalR-Live-Status liegen in
  der Identity Platform. Fachliche Modulstände, Cursor und Datensatzfehler
  bleiben tenantisoliert in der Sales-Datenbank und werden als Jobdetails
  gemeldet.
- Provider-Webhooks sind als zusätzlicher Sofort-Trigger vorgesehen. Der Crawl
  bleibt als Lückenschluss aktiv und der Vollimport als Reconciliation.
- Beim ersten Sync die vollständige Stage-Historie aus dem CRM abziehen und
  dauerhaft speichern.
- Speicherung in UTC, Anzeige in lokaler Zeit.
- Arbeitszeitfenster und konfigurierbares Geschäftsjahr berücksichtigen.

Wenn ein späteres Zoho-API-Limit andere Intervalle erzwingt, ist das eine
Konfigurations- bzw. Architekturentscheidung und kein Grund, die fachliche
Historie wegzulassen.

## Benötigte CRM-Daten

### Deals

Pflichtfelder: `id`, `account_id`, `amount`, `stage`, `pipeline`,
`created_date`, `closing_date`, `owner`, `produkt`, `laufzeit`,
`vertragsende`, `stage_history`.

Empfohlen: `verlustgrund` als Pflichtfeld im CRM konfigurieren. Ein Deal
entspricht genau einem Produkt. Angaben wie `Produkt 3;Produkt 5` sind keine
eigene Produktkategorie, sondern ein Hinweis für die Datenqualitätsansicht.

### Aktivitäten und Anrufe

Benötigt werden Bezug (`related_to`), Typ, Datum, Besitzer und
Gesprächsdauer. Die Anrufrichtung ist empfohlen. Der Verbindungsstatus sollte
verfügbar sein, damit eine Mailbox nicht allein wegen ihrer Dauer als Gespräch
gezählt wird.

### Accounts

Benötigt werden `id`, Branche, Land, PLZ/Ort, Besitzer, Status und
Erstellungsdatum. Land und Postleitzahl sind gemeinsam nötig, weil eine PLZ
international nicht eindeutig ist.

### Leads

Benötigt werden `id`, Erstellungsdatum, Lead-Quelle, Status, letzter Anruf und
Anzahl der Anrufversuche. `NULL` bei letztem Anruf bedeutet „noch nie
kontaktiert“ und wird vor einem historischen Datum einsortiert.

### Termine

Start, Ende und Status sind Pflicht. Ein Termin-Typ ist empfohlen. Für den
Meeting Report müssen mindestens geplant, stattgefunden, abgesagt,
verschoben und nicht erschienen unterscheidbar sein.

## Initialer Zoho-Import

Der Tenant-Admin startet den vollständigen Lauf auf der gemeinsamen Jobseite
oder aktiviert dort seinen Zeitplan. Für die fachlichen Anforderungen werden
folgende Datenbereiche gelesen und in das kanonische Modell übernommen:

- CRM-Benutzer als Owner,
- Accounts als Kundenorganisationen,
- Leads,
- Produkte und aus Produktkategorien abgeleitete Kategorien,
- Deal-Pipelines und Pipeline-Stufen,
- Deals einschließlich Produkt-, Kunde-, Besitzer-, Betrag-, Laufzeit- und
  Verlustinformationen,
- vollständige Deal-Stage-Historie über die Zoho-Related-List,
- Calls, Tasks und Termine/Events,
- E-Mails über die Related-Lists der Accounts, Leads und Deals.
- Servicefälle/Beschwerden aus `Cases`, Angebote aus `Quotes`, Aufträge aus
  `Sales_Orders` und Rechnungen aus `Invoices`. Diese Module sind optional:
  fehlt ein Modul im Zoho-Tenant, wird es übersprungen und der übrige Lauf
  bleibt erfolgreich.

Jeder Import schreibt zusätzlich den unveränderten Anbieter-Datensatz in
`integration_raw_records` und ordnet ihn über
`integration_entity_links` genau einmal einer kanonischen Entität zu.
Die Zuordnung erfolgt immer über `(ProviderKey, ConnectionKey, EntityType,
ExternalId)`. Für Zoho-Aktivitäten ist die Remote-ID kanonisch präfixiert, zum
Beispiel `Tasks:<id>`, `Calls:<id>` oder `Emails:<id>`; normale Stammdaten
verwenden weiterhin die Zoho-ID selbst. Ein Full-Crawl löscht keine lokalen
Datensätze und legt bei jedem Lauf keine neuen internen IDs an.
Der Zoho-Adapter ergänzt für alle direkt adressierbaren CRM-Entitäten außerdem
die optionale provider-spezifische `ExternalUrl` in
`integration_entity_links`. Das gilt für Benutzer, Accounts, Leads,
Produkte, Deals, Calls, Tasks, Termine, E-Mails, Servicefälle, Angebote,
Aufträge und Rechnungen. Arbeitsliste und Reports können damit direkt zum
Ursprungsdatensatz springen. Abgeleitete interne Entitäten wie Pipeline-
Metadaten und Deal-Stage-Historie besitzen keine eigene Zoho-Datensatzseite;
ihre Zuordnung bleibt trotzdem erhalten.
Wiederholungen sind dadurch idempotent. Fehlende optionale Zoho-Module oder
einzelne fehlerhafte Datensätze beenden nicht den gesamten Lauf; sie werden
pro Modul bzw. Datensatz als Fehler protokolliert.

## Inkrementelle Synchronisation

Der feste Job `crm-incremental-crawl` läuft im Modus `incremental`:

- jeder Quellmodul-Cursor wird getrennt in `integration_sync_cursors` geführt;
- Zoho erhält `If-Modified-Since` mit einem kleinen Überlappungsfenster, damit
  Änderungen an der Zeitgrenze nicht verloren gehen;
- HTTP 304 von Zoho bedeutet bei diesen Abfragen „keine Änderung“ und wird als
  erfolgreicher leerer Modulstand behandelt, nicht als Importfehler;
- der Wasserstand wird vor dem Lesen erfasst und erst bei fehlerfreier
  Modulverarbeitung als `LastSuccessfulRunId` fortgeschrieben;
- gelöschte Zoho-Datensätze werden über `/deleted` erkannt und in der Sales-
  Datenbank als `SourceDeletedAt`/inaktiv markiert, nicht physisch entfernt;
- ein vollständiger Crawl gleicht nach einem fehlerfreien Modulabschluss die
  gelesenen Remote-IDs mit den noch aktiven Links ab. Eine fehlende ID wird
  ebenfalls als Source-Delete markiert; bei einem abgebrochenen oder fehler-
  haften Modul findet kein solcher Abgleich statt;
- wird eine CRM-Aufgabe gelöscht, wird der aktuelle lokale Arbeitsvorgang
  geschlossen, historisch begründet und als neue Vorgangsinstanz derselben
  Kette erneut angelegt. Die neue Instanz erhält eine neue CRM-Task und damit
  eine neue Remote-ID. Wird dagegen ein Lead, Kunde oder Deal
  gelöscht, werden betroffene offene Arbeitsvorgänge mit
  `target-deleted-in-crm` geschlossen; es wird kein fachlich sinnloser
  Nachfolger ohne Ziel erzeugt;
- E-Mails und Stage-Historie werden als Related-Lists nach den Elternobjekten
  synchronisiert. Der Incremental-Crawl fragt sie nur für im Überlappungsfenster
  geänderte Elternobjekte ab; der Vollimport gleicht weiterhin alle Eltern ab.

Ein fehlgeschlagenes Modul behält dadurch seinen letzten erfolgreichen Cursor
und wird beim nächsten inkrementellen Lauf erneut berücksichtigt. Der
Vollimport bleibt als expliziter Rebuild der CRM-Daten verfügbar.

### Laufprotokoll und geschriebene Records

Der CRM-Sync meldet seinen fachlichen Zustand an den zentralen Plattformlauf.
Die erste Meldung nennt den Modus und den vollständigen
Synchronisationsplan. Für jedes Modul folgen der aktuelle Arbeitsschritt,
gelesene, geschriebene und fehlerhafte Datensätze sowie — sobald die
Quellmenge bekannt ist — die verbleibende Restmenge. Ein Modulabschluss nennt
dieselben Zähler noch einmal und hält Fehler mit externer ID, Fehlercode,
Nachricht und Retry-Hinweis fest.

Die Plattform speichert diese Meldungen als Job-Events und überträgt sie live
an `/jobs`. Im Laufdetail werden die fachlichen Details als JSON geöffnet. Die
Abschlussdetails enthalten für jeden geschriebenen Record mindestens
`entityType`, `externalId`, Änderungszeitpunkt, Synchronisationszeitpunkt und
das unveränderte Roh-Payload. Damit ist nachvollziehbar, welche Datensätze
der Lauf tatsächlich geschrieben hat; die dauerhafte Fachhistorie bleibt in
`integration_sync_runs`, `integration_sync_run_items` und
`integration_sync_errors`.

E-Mails sind kein eigener Plattformjob. Sie werden innerhalb desselben
CRM-Laufs als Related-List auf Basis der gelesenen Elternobjekte verarbeitet.
Ein Vollimport verarbeitet alle Elternobjekte, ein inkrementeller Lauf nur
Elternobjekte im Änderungsfenster. Die Related-List-Anfragen laufen mit begrenzter Parallelität
und die Writes batchweise; bei einem fehlerhaften Batch greift die bestehende
Einzelrecord-Isolation. So bleiben die CRM-Daten verknüpft, ohne eine zweite
E-Mail-Synchronisation oder unkontrollierte API-Parallelität einzuführen.
Regelbenachrichtigungen werden unmittelbar nach der Regelbewertung im selben
Full- bzw. Incremental-Lauf versendet. Die Outbox dient nur der idempotenten
Zustellung, dem Tageslimit von höchstens einer Mail pro Item und Empfänger
sowie dem Retry bei Fehlern.

Ein Abbruch wird in der zentralen Job-UI ausgelöst und über den Plattform-Worker
als CancellationToken an Adapter, Provider- und Datenbankoperationen
weitergegeben. Bereits erfolgreich persistierte Records bleiben erhalten; der
Lauf wird als abgebrochen bzw. unvollständig sichtbar und der Cursor eines
nicht erfolgreich abgeschlossenen Moduls wird nicht fortgeschrieben.

## Eigene persistierte Daten

Die Datenbank braucht neben kanonischen Fachdaten und externer
Identitätszuordnung mindestens:

- Stage-Historie je Deal,
- tägliche Snapshots von Pipeline-Wert, offenen Deals, ARR und Zielerreichung,
- synchronisierte Aktivitäten und Terminzustände,
- Mitarbeiter-, Ziel- und Aktivitätszielperioden,
- Produkt-/Pipeline-Konfiguration und Regelparameter,
- berechnete Prozessvorgänge und eigene Wiedervorlagen,
- Regel- und Berechnungsläufe mit Zeitstempel,
- Datenqualitätsbefunde,
- Dublettenentscheidungen und Merge-Protokolle,
- Änderungsprotokolle für Besitzerwechsel und freigegebene Rückschreibungen.

Die fachlichen Tabellen dürfen keine Zoho-spezifischen Feldnamen oder DTOs als
Voraussetzung haben. Anbieter-spezifische Rohdaten und Sync-Zustände gehören in
den Integrationsbereich. Die vollständige Zielstruktur ist in
[`06-integrationsarchitektur.md`](./06-integrationsarchitektur.md) beschrieben;
die konkrete Entitäten-, Tabellen- und Constraint-Planung steht in
[`07-ziel-datenmodell.md`](./07-ziel-datenmodell.md).

## Rückschreiben

Die von der SalesPlattform erzeugten Arbeitsvorgänge werden als CRM-Tasks
gespiegelt, damit der Benutzer die Bearbeitung im CRM durchführen kann. Jede
Vorgangsinstanz besitzt dabei höchstens eine aktive CRM-Task-Remote-ID. Wird
die Task im CRM gelöscht, entsteht für dieselbe Vorgangskette eine neue
Vorgangsinstanz mit neuer Remote-ID. Ein erledigter CRM-Task wird dagegen beim
Sync als erledigte Aktivität übernommen; die fachliche Regelbewertung
entscheidet, ob daraus ein neuer Vorgang entsteht.

Weitere Rückschreibefunktionen bleiben optional, explizit und abschaltbar. Im
Pflichtenheft genannt sind protokollierte Anrufe, Besitzerwechsel und
Dubletten-Merges. Ein Besitzerwechsel wird nicht automatisch ausgeführt; die
Leitung entscheidet.

## Datenqualität

Eine eigene Ansicht muss mindestens Deals ohne Betrag, verlorene Deals ohne
Verlustgrund, Accounts ohne Branche, kombinierte Produktangaben sowie nicht
verortbare Kunden anzeigen. `NULL` und `1900-01-01` bei Kontaktangaben werden
vor der Berechnung als „nie kontaktiert“ normalisiert.
