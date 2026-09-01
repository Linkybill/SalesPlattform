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

- Deals und Aktivitäten: stündlich.
- Accounts und Produkte/Stammdaten: täglich.
- Manueller Refresh zusätzlich vorsehen.
- Der manuelle Refresh startet einen Hintergrundjob. Das Frontend wartet nicht
  auf den gesamten CRM-Lauf, sondern beobachtet den persistierten Laufstatus
  per SignalR und kann ihn über die `RunId` erneut laden.
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

Rückschreiben ist optional, explizit und abschaltbar. Im Pflichtenheft genannt
sind erledigte Wiedervorlagen, protokollierte Anrufe und Besitzerwechsel. Ein
Besitzerwechsel wird nicht automatisch ausgeführt; die Leitung entscheidet.
Ein Dubletten-Merge wird bei aktiviertem Rückschreiben im CRM vorgenommen, die
SalesPlattform übernimmt danach den neuen Stand per Sync.

## Datenqualität

Eine eigene Ansicht muss mindestens Deals ohne Betrag, verlorene Deals ohne
Verlustgrund, Accounts ohne Branche, kombinierte Produktangaben sowie nicht
verortbare Kunden anzeigen. `NULL` und `1900-01-01` bei Kontaktangaben werden
vor der Berechnung als „nie kontaktiert“ normalisiert.
