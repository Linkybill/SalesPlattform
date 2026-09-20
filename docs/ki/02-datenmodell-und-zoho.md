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

## Zoho-Hook-Registrierung: Datumsformat

Korrektur vom 19.09.2026: `POST /crm/v8/actions/watch` sendet
`channel_expiry` im Format `yyyy-MM-dd'T'HH:mm:sszzz`, ohne Sekundenbruchteile
und mit explizitem Offset. Das bisherige .NET-Roundtripformat `O` erzeugte
Sekundenbruchteile; Zoho wies die Registrierung mit HTTP 400 / `INVALID_DATA`
für `channel_expiry` zurück. Die angeforderte Laufzeit bleibt 6 Tage und
23 Stunden, unter der maximalen Woche laut
[Zoho-V8-Vertrag](https://www.zoho.com/crm/developer/docs/api/v8/notifications/enable.html).

Die synthetischen Payload-Tests prüfen Calls/Tasks/Events, drei Kulturen,
Jahreswechsel, sekundengenaues Format, Laufzeit sowie unveränderte Callback-,
Channel-, Token- und Ereignisfelder. Kein Live-Registrierungsnachweis.
Nach Sales-Backend-Rollout `CRM-Hooks erneuern` starten und Modulstatus prüfen;
kein Plattform-/Paketupdate und keine Migration für diese Formatkorrektur.

### Registrierung direkt bei Zoho prüfen

Unter **Import → Hooks und Ereignisse → Registrierung bei Zoho prüfen** ein
Modul wählen (Default Calls) und die Schaltfläche betätigen. Die normale
Übersicht und „Übersicht aktualisieren“ bleiben lokale Datenbankabfragen.
„Hooks aktualisieren“ startet dagegen nach Bestätigung einen echten Neuaufbau
über den bestehenden Plattformjob (Änderung vom 20.09.2026).

`POST /api/integrations/zoho/hooks/check` ist authentifiziert und zusätzlich
Tenant-Admin-geschützt. Der Browser liefert nur das Modul; Channel-ID und
aktuelle Soll-URL stammen aus dem aktuellen Tenant. Der Adapter liest
`GET /crm/v8/actions/watch?channel_id=…&module=…` über die vorhandene
Zoho-Verbindung. Maximal fünf Seiten à 200 Einträge und 30 Sekunden;
unvollständige Abfragen werden nicht als Erfolg ausgegeben. Nur passender
Channel und passendes Modul werden ausgewertet. Keine Subscription-Writes,
Neuregistrierungen oder Callback-Proben; übliche OAuth-Erneuerung und
Verbrauchstelemetrie bleiben aktiv. Die manuelle Prüfung verbraucht API-Anfragen.

Die Antwort zeigt bestätigte Registrierung, fehlenden Channel, URL-Abweichung,
Ablaufproblem, unvollständige Ereignisse oder einen sicheren Fehlerhinweis.
Token, Userinfo, Fragment und fremde Querywerte werden nicht ausgegeben;
unbekannte URL-Pfade werden verborgen. Rohantworten/Providerfehler werden weder
an den Browser geliefert noch durch die Diagnose protokolliert. „Bestätigt“
beweist die Registrierung, **nicht** die Zustellung eines echten Callbacks.

Benötigte zusätzliche Berechtigung: `ZohoCRM.notifications.READ`, entsprechend
[Zoho Get Notification Details](https://www.zoho.com/crm/developer/docs/api/v8/notifications/get-details.html).
Defaults und bestehende Scope-Overrides werden ergänzt; vorhandene OAuth-Grants
erhalten dadurch keine neuen Rechte. Bei `OAUTH_SCOPE_MISMATCH` nach dem
Sales-Rollout **Zoho verbinden** erneut ausführen und Berechtigungen bestätigen.
Die Prüfung selbst fordert keine Rechte an und verändert keine Verbindungen.

Erweiterung vom 20.09.2026: Die Diagnose zeigt zusätzlich Feldfilter,
Verification-Token-Abgleich, lokalen Channel-Status und lokale Ablaufzeit.
`notification_condition` wird auf bekannte `field_selection`-Strukturen begrenzt;
nur Modul-/Feld-API-Namen und UND/ODER-Verknüpfungen werden ausgegeben, keine
Rohdaten, Feldwerte oder IDs. Explizites `null`/leeres Array bedeutet keine
Feldbedingungen. Fehlende Metadaten, unbekannte Formate, überschrittene Grenzen
oder nichtleere Legacy-`fields` ergeben „nicht vollständig prüfbar“, niemals
„keine Filter“. Gesetzte Filter erscheinen als Warnung, nicht als Beweis einer
Fehlkonfiguration. `notify_on_related_action` wird separat erklärt; `false`
unterdrückt keine direkte Erstellung eines Datensatzes im gewählten Modul.

Der Provider-Token wird unmittelbar serverseitig mit SHA-256 gehasht. Der
Vergleich mit dem lokalen Hash ist zeitkonstant; Token und Hash fehlen in
Antwort und Logs. Fehlender/ungültiger Token oder lokaler Hash verhindern den
Gesamterfolg. Nach der Zoho-Abfrage wird der tenantisolierte Channel nochmals
gelesen, damit zwischenzeitliche Erneuerung/Löschung nicht als passender alter
Stand bestätigt wird. Status und Ablauf werden nach denselben Kriterien wie
beim Callback-Empfänger geprüft. Die Diagnose ist eine Momentaufnahme, kein
Nachweis von TLS-/Router-Erreichbarkeit oder erfolgreicher Callback-Zustellung.

Wichtig: Nur ein geplanter Wartungslauf behält aktive Channels bei gleicher URL
außerhalb der 36-Stunden-Frist bei. Jeder manuelle Jobstart baut seit der
Neuaufbau-Korrektur vom 20.09.2026 alle verfügbaren relevanten Modul-Hooks mit
neuen Channels/Tokens ohne Feldbedingungen neu auf. Die lesende Diagnose selbst
ändert weiterhin nichts und nimmt keine automatische Reparatur vor.

### Hooks wirklich aktualisieren

„Hooks aktualisieren“ bestätigt den mandantenweiten Neuaufbau und sendet einen
POST an den bestehenden tenantadmin-geschützten Plattform-Jobstart für
`crm-subscription-maintenance`. Listenfilter beeinflussen die Modulauswahl nicht.
Die Plattform übernimmt Queue, mandantenbezogene Exklusivgruppe und Jobhistorie.
Die UI bestätigt nur die Annahme des Laufs, nicht dessen Erfolg. Bei 409 einen
blockierenden Job prüfen; bei unklarer Netzwerkantwort vor erneutem Start die
Jobliste prüfen. Kein automatisches Wiederholen des POST.

Je Modul: neuen Channel registrieren; bestätigte Channel-ID, Modul und Ablauf
prüfen; Zuordnung/Token-Hash lokal speichern; erst danach alten Channel löschen.
Auch die Löschbestätigung wird geprüft (nicht nur HTTP-Erfolg). Eigener DB-Kontext
je Modul verhindert versehentliches Nachspeichern fehlgeschlagener Änderungen.
Ein Modulfehler lässt weitere Module weiterlaufen; der Lauf erhält Warnungen.
Eine fehlgeschlagene Registrierung markiert den bisherigen Channel nicht als
fehlgeschlagen. Bei unbestätigtem Commit wird keiner der beiden Channels gelöscht.
Fehler beim Deaktivieren lassen die neue Zuordnung bestehen. Das Jobprotokoll
enthält Phase und Channel-IDs, aber keine Tokens oder rohen Providerfehler.
Manueller Start ohne aktive Zoho-Verbindung, bei deaktivierten Hooks oder ohne
verfügbare Module meldet einen nicht ausgeführten Neuaufbau als Warnung.

Grenzen: kein atomarer Wechsel über Zoho und Datenbank hinweg. Cancellation,
Prozessabbruch oder unklare Providerantwort können einen verwaisten neuen/alten
Channel hinterlassen; keine automatische nachträgliche Bereinigung implementiert.
Warnungen prüfen; alte Channels können bis zu ihrem Ablauf weiter senden, werden
aber nach dem lokalen Wechsel nicht mehr akzeptiert. Bereits gespeicherte Events
bleiben erhalten; der Crawl schließt Lücken. Keine Änderung der OAuth-Verbindung.

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
