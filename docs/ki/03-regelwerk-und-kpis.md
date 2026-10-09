# Regelwerk, Priorisierung und KPIs

## Ergänzte Reports aus Screenshot-Abgleich (09.10.2026)

- Erstgespräche/Folgetermine nach Branche: aktive, nicht gelöschte Termine
  nach Beginn im Reportzeitraum, alle Status. Exakte Terminarten aus
  sales.reports.firstMeetingTypes und sales.reports.followUpMeetingTypes
  (Semikolonlisten, Großschreibung/äußerer Leerraum ignoriert). Überlappende,
  fehlende oder andere Arten werden in „Terminarten prüfen“ separat gezeigt.
- Branche: Kundenverknüpfung direkt, über Deal oder über Lead mit Kundenbezug.
  Derselbe Termin zählt trotz mehrerer Beziehungen nur einmal. Unterschiedliche
  Branchen ergeben „Mehrere Branchen“, keine Zuordnung „Ohne Branche“.
  Unaufgelöste Kontakte werden nicht zu erfundenen Kunden/Branchen.
- Top-Produkte nach Anzahl: aktive gewonnene Deals mit Abschlussdatum im
  Zeitraum; ersatzweise Änderungsdatum. Ein Deal = ein Produkt. Ranking nach
  Anzahl, nicht Umsatz; fehlende Produkte bleiben enthalten. Top 8 plus
  Sonstige, vollständige Nachweise und Gesamtzahl.
- Offene Angebots-Deals nach Branche: aktueller offener nichtterminaler
  Dealbestand in den konfigurierten sales.reports.offerStageNames, unabhängig
  vom gewählten Abschlusszeitraum.
- Offene Angebotsbelege nach Branche: aktive nicht abgeschlossene CRM-Belege
  nach Ausstellungsdatum, ersatzweise Erstellungsdatum, im gewählten Zeitraum.
  Kundenbezug ersatzweise über verknüpften Deal. Belege und Angebots-Deals
  sind bewusst getrennt; keine Addition oder behauptete Gleichheit.
- Terminvorbereitung: sales.reports.preparationDays (Standard 5, 1–90),
  Kalendertage einschließlich heute mit UTC-Tagesgrenzen. Chronologisch,
  vom Reportzeitraum unabhängig, einschließlich abgesagter/verschobener
  Termine mit Status. Meeting Report zeigt sie standardmäßig.
- Abgesagt, verschoben, durchgeführt und nicht stattgefunden stehen zusätzlich
  als getrennte Zeitraum-/Wochenlisten bereit. Verschoben berücksichtigt
  auch eine protokollierte Verschiebung bei inzwischen anderem Status.

Alle Werte und Nachweiszeilen stammen aus derselben Dashboard-Antwort. Keine
neue CRM-Abfrage beim Öffnen. Native Abnahme: 185 Report-Assertions, 25
Kontaktregressionen und 113 zusätzliche Reportprüfungen bestanden, darunter
echte tenantisolierte EF-Datenladung mit Terminbeziehungen. TypeScript, Vite,
sechs Navigationsprüfungen und Chrome mit neuen Diagrammen/Detaildaten,
Terminvorschau, getrennten Statuslisten, Suche/Pagination, Escape/Fokus sowie
390px-Darstellung bestanden. Kein Live-Abgleich der Zoho-Analytics-Zahlen.

## Kontaktmarker und R-07-Fälligkeit (09.10.2026)

Die gemeldete geringe Trefferzahl wurde bei aktiver Team-Arbeitsliste eingegrenzt.
Importierte Tasks, einschließlich eigener CRM-Wiedervorlagen und zukünftiger
Fälligkeiten, zählen nicht als Kundenkontakt. E-Mails und qualifizierte Anrufe
bis zum Berechnungszeitpunkt zählen weiterhin. Die erste Lead-Aktivität wird
nicht durch das bloße Anlegen einer Aufgabe erfüllt. Last_Activity_Time ist
kein Ersatz für ein explizites CRM-Kontaktdatum.

R-07 mit fehlendem Kontaktdatum ist sofort fällig. Als Sortierdatum dient ein
vorhandenes vergangenes Erstellungsdatum, sonst jetzt; es werden keine 90 Tage
auf das Erstellungsdatum addiert. Bei vorhandenem Kontakt gilt unverändert
Kontaktzeitpunkt plus tenantbezogener Inaktivitätsschwelle.

Alte Task-bedingte Kontaktmarker werden nach einem fehlerfreien Vollimport
mit vollständig erfolgreichen Accounts-, Leads-, Calls- und Emails-Modulen
aus echten Aktivitäten und expliziten Quell-Kontaktdaten rekonstruiert. Danach
läuft die bestehende Vollbewertung aller Regeln. Teilimporte oder Fehler
lösen diese Rekonstruktion nicht aus. Deploy und erfolgreicher Vollimport
sind für die Bereinigung bereits betroffener Kunden erforderlich.
Dies ist keine vollständige Nachbildung der Zoho-Analytics-Auswahl (>5 Monate).

## Prioritätsscore

Die einheitliche Arbeitsliste wird absteigend nach folgendem Score sortiert;
bei Gleichstand ist der älteste Vorgang zuerst:

```text
score = basiswert(vorgangsart) + altersbonus + wertbonus
altersbonus = min(tage_ueberfaellig * 0.5, 30)
wertbonus = min(deal_betrag / 10.000, 20)
```

Vorgeschlagene Basiswerte:

| Vorgangsart | Punkte |
|---|---:|
| Vertrag läuft in unter 30 Tagen ab | 100 |
| Offene Lead-Reaktion | 95 |
| Hängender Deal | 80 |
| Vertrag läuft in unter 90 Tagen ab | 70 |
| Wiedervorlage fällig | 60 |
| Besitzerwechsel | 50 |
| Inaktiver Lead | 30 |
| Cross-Selling | 20 |

Basiswerte, Deckelungen und der Divisor sind konfigurierbar. Seit der
Gesprächsentscheidung vom 19.09.2026 bleiben Punkte intern für die Sortierung;
die Oberfläche zeigt nur die verständliche Prioritätsstufe.

### Nachvollziehbare Report-Kennzahlen (19.09.2026)

`SalesReportEvidence` liefert Kennzahlmetadaten und normalisierte Detailzeilen
in derselben Dashboard-Antwort. Das Frontend berechnet Summen/Quoten nicht
erneut und fragt beim Öffnen eines Nachweises nicht erneut das CRM ab.

- Umsatz: aktive gewonnene Deals nach Abschlussdatum, ersatzweise CRM-
  Änderungsdatum. Kein Rechnungsumsatz. Kunden-Lifetime zeigt dieselbe
  Deal-Basis über alle synchronisierten Zeiträume, nicht den optionalen
  CRM-Kunden-Umsatzwert.
- Zielerreichung/Pace: Umsatz des aktuellen Geschäftsjahres gegen dessen
  Jahresziele; auch auf Monats-/Lifetime-Seiten ausdrücklich als Jahresbezug
  beschriftet. Fehlende Ziele oder Nenner ergeben „Nicht berechenbar“.
- Pipeline: aktueller offener Bestand außerhalb terminaler Stufen.
  Deckung = Pipeline / verbleibendes Jahresziel als Vielfaches.
- Fehlende Währungen gelten gemäß Benutzerentscheidung vom 28.09.2026 als
  EUR, auch bei bestehenden Importdaten und in den Nachweiszeilen. Die
  Berechnungsbeschreibung nennt diese Annahme. Explizite Währungen bleiben
  erhalten. Fehlende Beträge zählen gemäß Folgeentscheidung vom 28.09.2026
  als 0; vorhandene Beträge gehen vollständig in die Summe ein. Gemischte
  Währungen verhindern weiterhin die Summe.
  Diagramme vergleichen unterschiedliche Währungen nicht über Balkenlängen.
- Wiederkehrende Vertragsbeträge werden nicht mehr fälschlich ARR genannt:
  eine verlässliche Monats-/Jahresbasis fehlt im Quellmodell.
- Neu angelegte Termine werden nach Erstellungsdatum gezählt, unabhängig
  vom Terminbeginn. Kalenderwoche und Terminquoten verwenden den Beginn.
  Quoten-Nachweise zeigen den vollständigen Nenner einschließlich Status.
- Stale-/Renewal-Grenzen kommen aus den Tenant-Einstellungen. Dringende
  Servicefälle werden vollständig gezählt, nicht nur eine Vorschau mit acht.
- Zeiträume verwenden wie bisher UTC und werden so beschriftet; Zeilen-Daten
  stellt der Browser lokal dar. Kalender-/Zeitzonenumbau ist nicht enthalten.
- Importzeitpunkt, Modus, Status und Fehleranzahl des letzten beendeten Laufs
  werden zusätzlich zum Berechnungszeitpunkt angezeigt. Ein Teilimport ist
  ausdrücklich kein Aktualitätsnachweis aller Entitäten.

Die erste produktive Projektion berechnet die Werte für die Arbeitsliste mit
diesen Startwerten. Vertragsenden unter 30 Tagen werden als eigener kritischer
Vorgang geführt; Cross-Selling verwendet 20 Basispunkte. Eine Änderung durch
Praxiswerte muss später über das konfigurierbare Prioritätsprofil erfolgen.

Euro-Standard (28.09.2026): lokal umgesetzt, 132 native Windows-Reportprüfungen
bestanden, einschließlich leerer Währungen, Nachweiszeilen, Zielquoten und
Schutz vor gemischten Währungen. Kein Deployment; kein Neuimport erforderlich.

Fehlende Beträge als 0 (28.09.2026): lokal umgesetzt; 137 native
Windows-Reportprüfungen bestanden, einschließlich gemischter vorhandener und
fehlender Beträge, Pipeline-Stufensummen und Pipeline-Deckung. Kein Deployment.

Zeit- und Versuchsschwellen werden als tenantbezogene App-Einstellungen
(`sales.rules.*`) gepflegt und bei jeder Regelbewertung neu geladen. Die
aktuellen Defaults aus dem Pflichtenheft sind: 14 Tage Anruf-Wiedervorlage,
E-Mail-Aktion beim 5. Versuch mit ebenfalls 14 Tagen Wiedervorlage,
Langläufer ab Versuch 6 bis Versuch 10 mit 30 Tagen Abstand, „nicht
erreichbar“ ab mehr als 10 Versuchen, hängender Deal nach 30 Tagen mit
Cockpit-Eskalation nach 60 Tagen, Renewal innerhalb 90 Tagen mit kritischer
Stufe ab 30 Tagen, überfälliger Kontakt nach 90 Tagen, Lead-Erstreaktion
nach 1 und Eskalation nach 4 Arbeitsstunden sowie dreimal verschobener Termin.
Die Parameter für Account-Care, Deal-Reaktivierung, Mindestwerte und Pace sind
ebenfalls vorbereitet und verwenden die aktuellen Vorgaben als Defaults.
Für die ergänzte CRM-Kette gelten zusätzlich: Servicefall-Reaktion nach 2
Tagen, Angebots-Folgeaktion nach 7 Tagen, Lieferverzug nach 1 Toleranztag und
Rechnungsüberfälligkeit ab dem Fälligkeitstag. Alle Zeitwerte werden in Tagen
als Tenant-App-Einstellungen geführt.

## Gespräch und Anrufzähler

Ein Gespräch zählt ab der appweit konfigurierten Mindestdauer, standardmäßig
ab 20 Sekunden. 0 Sekunden bzw. nicht verbunden und jede Dauer unterhalb der
konfigurierten Schwelle zählen als Versuch. Es gibt zwei Zähler:

- Versuche seit dem letzten Gespräch: wird bei einem Gespräch zurückgesetzt und
  steuert die Staffelung.
- Kumulierte Gesamtversuche: bleibt für Auswertungen erhalten.

Der Verbindungsstatus soll Vorrang vor der Dauer haben. Ist ein Anruf technisch
eine Mailbox, braucht die Oberfläche eine Korrekturmöglichkeit. Die Behandlung
eingehender Anrufe ist noch offen.

## Geschäftsregeln R-01 bis R-18

| ID | Auslöser | Ergebnis |
|---|---|---|
| R-01 | Keine Verbindung, Versuche seit Gespräch höchstens 5 | Wiedervorlage in 14 Tagen, gleicher Besitzer, Zähler erhöhen, eigene Liste |
| R-02 | Fünfter Versuch | E-Mail-Vorlage vorschlagen und 14-Tage-Wiedervorlage; „Mail senden“ manuell oder konfigurierbar automatisch markieren |
| R-03 | Versuche 6–10 ohne Gespräch | 30-Tage-Intervall und Kennzeichnung „Langläufer“ |
| R-04 | Mehr als 10 Versuche ohne Gespräch | Status „nicht erreichbar“ nur vorschlagen; keine automatische Statusänderung, Besitzerfreigabe und Bereinigung |
| R-05 | Offener Deal länger als 30 Tage ohne Aktivität | Rot in Besitzerliste; ab 60 Tagen zusätzlich Cockpit-Handlungspunkt |
| R-06 | Vertragsende innerhalb 90 Tagen | Verlängerungsvorgang für Besitzer; unter 30 Tagen höchste Priorität und Management-Hinweis |
| R-07 | Letzter Kontakt älter als 90 Tage oder `NULL` | Reaktivierungsvorgang, älteste zuerst |
| R-08 | Stufe „Agent Wechsel“ oder entsprechende Regel | Besitzerwechsel-Liste mit altem Besitzer, Kontakt und Wert; Leitung entscheidet, kein Auto-Wechsel |
| R-09 | Neuer Lead ohne Aktivität nach 1 Arbeitsstunde | Besitzer benachrichtigen; nach 4 Arbeitsstunden eskalieren; höchste Priorität |
| R-10 | Aktiver Kunde ohne Deal in definierter Produktkategorie | Cross-Selling-Liste; Kategorien und Mindestkundenwert konfigurierbar |
| R-11 | Zielerreichung mehr als 15 Punkte unter Zeitanteil | Team-Flag und Hinweis an die Leitung |
| R-12 | Termin mindestens dreimal verschoben | Verdachtsfall in Arbeitsliste; Deal klären oder als verloren markieren |
| R-13 | Aktiver Kunde mit Umsatzhistorie, aber mehr als 90 Tage ohne Telefon | Account-Care-Liste; Zeitraum und Mindestumsatz konfigurierbar |
| R-14 | Verlorener Deal älter als 90 Tage, Grund Timing/Budget | Reaktivierungsvorschlag an früheren Besitzer |
| R-15 | Offener/dringender Servicefall ohne rechtzeitige Bearbeitung | Servicefall-Vorgang; bei Fristüberschreitung bzw. hoher Priorität eskalieren |
| R-16 | Gesendetes Angebot ohne Entscheidung nach 7 Tagen | Folgekontakt beim Besitzer |
| R-17 | Offener Auftrag nach zugesagtem Liefertermin plus Toleranz | Lieferverzug prüfen und eskalieren |
| R-18 | Offene Rechnung nach Fälligkeit plus Toleranz | Zahlungsstatus prüfen; offener Betrag anzeigen |

Wiedervorlagen erzeugt und verwaltet das Tool selbst. Grenzwerte, Intervalle,
Besitzerlogik und Automatisierungsgrade gehören in die Konfiguration.

### Umsetzungsstand der ersten Arbeitsliste

Aktiv ausgewertet werden R-01 bis R-18. Die Projektion schreibt passende
`SalesWorkItem`-Einträge, Regelruns und Regelauswertungen. Ein
Vorgang wird nicht mehr lokal als fachlich erledigt markiert: Die Auflösung
erfolgt, wenn der nächste CRM-Sync den zugrunde liegenden Zustand nicht mehr
als Treffer liefert. Zurückstellen bleibt eine lokale Arbeitslisten-Funktion
und wird als geschlossene Vorgängerinstanz mit neuem Nachfolger, `AvailableFrom`,
WorkItem-Ketten-ID und Audit protokolliert. R-09 verwendet den konfigurierten
Arbeitszeitkalender und eskaliert nach der konfigurierten Arbeitszeit. R-11
berechnet die Pace je Mitarbeiter aus dem laufenden Geschäftsjahr, R-13 prüft
Umsatzhistorie plus Telefonalter und R-14 prüft verlorene Deals mit Timing-/
Budgetgrund. Alle Regeln laufen nach Full- und Incremental-Crawl; der
Incremental-Crawl erweitert den Scope nur auf betroffene Datensätze und ihre
fachlichen Abhängigkeiten.

R-09 und R-11 erzeugen einen priorisierten Vorgang und zusätzlich eine
idempotente E-Mail-Outbox-Benachrichtigung: R-09 geht zunächst an den Besitzer
und ab der Eskalationsgrenze an die hinterlegten Empfänger der
Vertriebsleitung; R-11 geht an diese Leitungsempfänger. Die Zustellung läuft
über den providerbasierten SMTP-Transport. Die lokalen Defaults zeigen auf
Mailpit (`mailpit:1025`); O365/Graph kann später als weiterer Provider ergänzt
werden. Zustellstatus, Versuche, Sperrfrist und letzter Fehler liegen in
`sales_notifications`.

Anrufe werden als Aktivitäten importiert. Nicht erreicht, Mailbox und falscher
Ansprechpartner bleiben Versuche und setzen den Staffelzähler nicht zurück;
ein qualifiziertes Gespräch beginnt ab der appweit konfigurierten Mindestdauer
(Standard: 20 Sekunden). Die
Incremental-Bewertung folgt nach dem Sync von der Aktivität über Lead,
Kontakt, Kunde oder Deal nur den betroffenen Regelzielen.

## Sortierung

- Eigene Liste: Score absteigend, dann ältester Vorgang.
- Inaktive Leads: letzter Kontakt aufsteigend, `NULL` zuerst.
- Fällige Wiedervorlagen: Fälligkeit aufsteigend, überfällige zuerst.
- Hängende Deals: Tage ohne Aktivität absteigend, dann Betrag absteigend.
- Auslaufende Verträge: Vertragsende aufsteigend.
- Besitzerwechsel: Kundenwert absteigend, dann letzter Kontakt aufsteigend.
- Cross-Selling: historischer Umsatz absteigend.

## Ziel, Pace und Aktivität

### Jahresziele pflegen (28.09.2026)

Unter Steuerung öffnet „Jahresziel festlegen“ für Vertriebsleitung und
Geschäftsführung die Ziele des aktuellen Geschäftsjahres je CRM-Mitarbeiter.
EUR-Beträge werden über GET/PUT `/api/reports/annual-targets` ausschließlich in
`sales_targets` gespeichert; keine Zoho-Aufrufe oder Rücksynchronisierung.
Die Summe der Mitarbeiterziele ist das Cockpit-Jahresziel. Leere Eingaben
entfernen das jeweilige Jahresumsatzziel; 0 ist ein explizites Nullziel.
Monats-/Quartalsziele werden nicht verändert. Ohne konfiguriertes offenes
Geschäftsjahr wird beim Speichern das laufende Kalenderjahr angelegt, sofern
es keine bestehenden Geschäftsjahre überschneidet.

Serverseitige Rollenprüfung, mandantenisolierte Plattform-DB-Sessions,
Dezimalvalidierung und ein Formular-Revisionsvergleich schützen die Eingaben.
Serialisierbare Transaktionen verhindern doppelte Jahresziele bei parallelem
Anlegen. Bestehende doppelte Ziele oder Fremdwährungen blockieren die Maske,
statt still überschrieben zu werden. Nach dem Speichern lädt die UI die
Reports neu. Kein Schemawechsel erforderlich.

Lokal geprüft: 147 native .NET-Assertions für Reportprojektion, Rollen und
Eingaben; TypeScript-Prüfung und Chrome-Smoke-Test inklusive Speichern,
Wiederöffnen und Abbrechen der Zielmaske bestanden. Browser nutzt synthetische
API-Antworten; produktive DB-Persistenz und Rollout sind nicht live abgenommen.

```text
zeitanteil = vergangene_tage_im_gj / gesamttage_im_gj * 100
zielerreichung = umsatz_ytd / jahresziel * 100
pace = zielerreichung - zeitanteil
```

| Pace | Status |
|---:|---|
| ab +5 | Vor Plan |
| -5 bis +5 | Im Plan |
| -15 bis unter -5 | Rückstand |
| unter -15 | Kritisch, R-11 |

## KPI-Katalog

### Cockpit

1. Gewonnener Umsatz: `SUM(amount)` für gewonnen im Geschäftsjahr; Ampel grün
   über 90 % Ziel, rot unter 70 %.
2. Win Rate: gewonnen / (gewonnen + verloren) × 100; grün über 35 %, rot unter
   20 %.
3. Pipeline-Deckung: offene Pipeline / (Jahresziel − Umsatz YTD); grün über 3×,
   rot unter 2×.
4. Sales Cycle: Durchschnitt `closing_date - created_date` gewonnener Deals,
   Vergleich zum Vorquartal.
5. ARR: `SUM(amount / laufzeit_jahre)` bei aktivem Vertrag.
6. Neu vs. Bestand: Neukunde, wenn es der erste gewonnene Deal des Accounts ist.
7. Hängende Deals: offene Deals mit mehr als 30 Tagen seit letzter Aktivität;
   rot ab 10 Deals oder 100 T€.
8. Auslaufende Verträge: Anzahl und Summe bei Vertragsende in 90 Tagen; gelb
   bei 90 Tagen, rot bei 30 Tagen.

### Funnel und Team

- Conversion je Stufe aus der Stage-Historie.
- Pipeline je Stufe, je Pipeline getrennt.
- Gewichtete Pipeline über konfigurierbare Stufenwahrscheinlichkeiten.
- Verweildauer je Stufe.
- Verlustgründe inklusive Anteil ohne Grund.
- Termine und Anrufe je Mitarbeiter und Zeitraum.
- Touchpoints bis Abschluss je Pipeline.
- Lead-Response-Zeit nur in Arbeitszeit.
- Zielerreichung zusammen mit Pace.

### Analyse

Umsatz nach Branche, Produkt und Region, Churn Rate, durchschnittliche Zahl
verschiedener Produktkategorien je aktivem Account sowie die Cross-Selling-
Matrix. Branchen und Produkte werden auf Top 8 plus „Sonstige“ reduziert,
Regionen auf zweistellige PLZ-Bereiche gruppiert.
