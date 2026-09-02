# Regelwerk, Priorisierung und KPIs

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

Basiswerte, Deckelungen und der Divisor sind konfigurierbar. Die Punkte werden
im Listeneintrag angezeigt.

Die erste produktive Projektion berechnet die Werte für die Arbeitsliste mit
diesen Startwerten. Vertragsenden unter 30 Tagen werden als eigener kritischer
Vorgang geführt; Cross-Selling verwendet 20 Basispunkte. Eine Änderung durch
Praxiswerte muss später über das konfigurierbare Prioritätsprofil erfolgen.

## Gespräch und Anrufzähler

Ein Gespräch zählt standardmäßig ab 20 Sekunden. 0 Sekunden bzw. nicht
verbunden und 1–19 Sekunden zählen als Versuch. Es gibt zwei Zähler:

- Versuche seit dem letzten Gespräch: wird bei einem Gespräch zurückgesetzt und
  steuert die Staffelung.
- Kumulierte Gesamtversuche: bleibt für Auswertungen erhalten.

Der Verbindungsstatus soll Vorrang vor der Dauer haben. Ist ein Anruf technisch
eine Mailbox, braucht die Oberfläche eine Korrekturmöglichkeit. Die Behandlung
eingehender Anrufe ist noch offen.

## Geschäftsregeln R-01 bis R-14

| ID | Auslöser | Ergebnis |
|---|---|---|
| R-01 | Keine Verbindung, Versuche seit Gespräch höchstens 5 | Wiedervorlage in 14 Tagen, gleicher Besitzer, Zähler erhöhen, eigene Liste |
| R-02 | Fünfter Versuch | E-Mail-Vorlage vorschlagen und 14-Tage-Wiedervorlage; „Mail senden“ manuell oder konfigurierbar automatisch markieren |
| R-03 | Versuche 6–10 ohne Gespräch | 30-Tage-Intervall und Kennzeichnung „Langläufer“ |
| R-04 | Mehr als 10 Versuche ohne Gespräch | Status „nicht erreichbar“ nur vorschlagen; keine automatische Statusänderung, Besitzerfreigabe und Bereinigung |
| R-05 | Offener Deal länger als 30 Tage ohne Aktivität | Rot in Besitzerliste; ab 60 Tagen zusätzlich Cockpit-Handlungspunkt |
| R-06 | Vertragsende innerhalb 90 Tagen | Verlängerungsvorgang für Besitzer; unter 30 Tagen höchste Priorität und Management-Hinweis |
| R-07 | Letzter Kontakt älter als 3 Monate oder `NULL` | Reaktivierungsvorgang, älteste zuerst |
| R-08 | Stufe „Agent Wechsel“ oder entsprechende Regel | Besitzerwechsel-Liste mit altem Besitzer, Kontakt und Wert; Leitung entscheidet, kein Auto-Wechsel |
| R-09 | Neuer Lead ohne Aktivität nach 1 Arbeitsstunde | Besitzer benachrichtigen; nach 4 Arbeitsstunden eskalieren; höchste Priorität |
| R-10 | Aktiver Kunde ohne Deal in definierter Produktkategorie | Cross-Selling-Liste; Kategorien und Mindestkundenwert konfigurierbar |
| R-11 | Zielerreichung mehr als 15 Punkte unter Zeitanteil | Team-Flag und Hinweis an die Leitung |
| R-12 | Termin mindestens dreimal verschoben | Verdachtsfall in Arbeitsliste; Deal klären oder als verloren markieren |
| R-13 | Aktiver Kunde mit Umsatzhistorie, aber mehr als 3 Monate ohne Telefon | Account-Care-Liste; Zeitraum und Mindestumsatz konfigurierbar |
| R-14 | Verlorener Deal älter als 3 Monate, Grund Timing/Budget | Reaktivierungsvorschlag an früheren Besitzer |

Wiedervorlagen erzeugt und verwaltet das Tool selbst. Grenzwerte, Intervalle,
Besitzerlogik und Automatisierungsgrade gehören in die Konfiguration.

### Umsetzungsstand der ersten Arbeitsliste

Aktiv ausgewertet werden R-05, R-06, R-07, R-08, R-09, R-10 und R-12. Die
Projektion schreibt passende `SalesWorkItem`-Einträge, Regelruns und
Regelauswertungen. Erledigen und Zurückstellen werden als WorkItem-Event und
Audit protokolliert. R-09 nutzt bis zur Aktivierung eines Arbeitszeitkalenders
eine verstrichene Stunde; R-01 bis R-04, R-11, R-13 und R-14 bleiben weitere
Regelengine-Ausbaustufen.

## Sortierung

- Eigene Liste: Score absteigend, dann ältester Vorgang.
- Inaktive Leads: letzter Kontakt aufsteigend, `NULL` zuerst.
- Fällige Wiedervorlagen: Fälligkeit aufsteigend, überfällige zuerst.
- Hängende Deals: Tage ohne Aktivität absteigend, dann Betrag absteigend.
- Auslaufende Verträge: Vertragsende aufsteigend.
- Besitzerwechsel: Kundenwert absteigend, dann letzter Kontakt aufsteigend.
- Cross-Selling: historischer Umsatz absteigend.

## Ziel, Pace und Aktivität

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
