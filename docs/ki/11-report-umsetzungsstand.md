# Report-Umsetzung nach Spezifikation und Screenshots

Stand: 09.10.2026. Lokal geprüft; anschließender Benutzerauftrag: push und deploy. Release-Nachweis im Betriebsstand.

## Einstieg in der bestehenden Oberfläche

| Bereich | Auswertungen |
| --- | --- |
| Schlummernde Leads | Interessenten mit letztem Termin/Kontakt älter als fünf Kalendermonate; ohne Interesse separat; nie kontaktierte Kunden; eigenständige Leads ohne aktuellen Kontakt |
| Wiedervorlagen | Erfolglose Anrufversuche vor E-Mail-Schwelle, an der Schwelle, Langläufer und nicht erreichbar; Terminvorbereitung für nächste Tage |
| Auslaufende Produkte | Vertragsliste innerhalb des konfigurierten Verlängerungshorizonts |
| Meeting Report | Neu/geplant/durchgeführt/abgesagt/verschoben, Durchführungs-/No-Show-/Verschiebe-/Angebotsquote; aktuelle Wochenlisten und Vorbereitung mit Von/Bis |
| Monatsreport / Jahresreport / Analyse | Umsatz nach Produkt/Branche als Kuchen, Erst-/Folgetermine und offene Angebots-Deals bzw. Belege nach Branche, individuelle Produktzählung und verkaufte Produkte getrennt, Top-Produkte letzte 30 Tage, Verlust-Pareto, Funnel/gewichtete Pipeline/Verweildauer je Pipeline, Cross-Selling-Matrix und Quote, Churn, Datenabdeckung |
| Cockpit | Bisherige Kennzahlen, verlorener/Neu-/Bestands-/nicht zugeordneter Umsatz, Deal-Mittelwert, Telefonate und neue Kunden; konfigurierbare Ampel; Vorquartalvergleich; annualisierter Vertragsumsatz; höchstens fünf Handlungspunkte |
| Vertriebsteam | Anrufe erreicht/nicht erreicht/ungeklärt, Termine nach Typ, Angebote/Win Rate, Response innerhalb Arbeitskalender, Touchpoints bis Gewinn je Pipeline, Zielerreichungsbalken mit gestricheltem Zeitanteil |
| Allgemein – Lifetime | Jahresumsätze gewonnen/verloren, Produkt-/Branchenmix als Fläche absolut/prozentual, Abschlüsse, Kundenbestände/Zu-/Abgänge/Churn, Telefon-/Terminaktivität, Deal-Mittelwert, Mitarbeiterumsätze, Umsatz je umsatzaktivem Besitzer, verkaufte Produkte |
| Kundenstamm | Gefilterte Kundenkarte und Tabelle; Punktgröße nach Umsatz bei gleicher Währung, Bündelung beim Herauszoomen; PLZ-/Land-/Regionsumsatz mit Vorjahr, weiße Flecken und Standortabdeckung, mittlere Betreuerentfernung |
| Ziele & Pace | Jahresziele, Monats-/Quartalsverteilung, erreichte Werte und gespeicherte Aktivitätsziele |

Alle Kennzahlen sind mit ihren Datensätzen, Berechnungen und Zeiträumen verknüpft.
Neue Ansichten verwenden dieselbe Evidence-Antwort wie die Detailtabellen.
Die größeren Tabellen bieten Suche und Seitenwechsel. Jahresmischungen lassen
sich zwischen absoluten Werten und Prozenten umschalten; der Klick zeigt immer
die ursprünglichen absoluten Nachweise.

## Fachliche Abgrenzungen

- Kuchen folgen dem ausdrücklichen Benutzerwunsch trotz abweichender Empfehlung
  in §13. Bei Umsatz sind alle Gruppen oder Top 8 plus Sonstige wählbar;
  Erst-/Folgetermin- und Angebotsverteilungen verlieren keine Gruppen.
- Die individuelle Produktzählung aus der Screenshot-Achsenbeschriftung zählt
  einen befüllten Produktnamen pro Produktgruppe einmal. Daneben bleibt die
  fachliche Produktstückzahl bestehen: ein gewonnener Deal = ein Produkt.
- Erst-/Folgeterminarten und Angebotsstufen werden exakt über AppSettings
  zugeordnet. Unbekannte Typen und fehlende Produkte bleiben prüfbar.
- Offene Angebots-Deals (aktueller Bestand) und Angebotsbelege
  (Ausstellungszeitraum) sind getrennte Auswertungen; keine verdeckte Vermischung.
- Kontaktlisten sind zusätzliche Leseauswertungen. R-07 bleibt unverändert.
  Der jüngste vergangene, nicht abgesagte/verschobene/ausgefallene Termin und
  ein echter importierter Kundenkontakt bestimmen das Kontaktalter.
  Zukünftige Termine verlängern keinen tatsächlichen Kontakt.
  Interessent bedeutet hier: kein gewonnener Deal und kein konfigurierter
  Ohne-Interesse-Status. Kunden ohne dokumentierten Kontakt stehen separat.
- Kein Report setzt LastContactAt oder schreibt ins CRM. Erfolglose Versuche
  werden aus importierten Anrufen seit dem letzten qualifizierten Gespräch
  gezählt; doppelte Relations zählen denselben Anruf nur einmal.
  Die Schwellen stammen aus denselben AppSettings wie die Arbeitslistenregeln.
- Stufenconversion: unterschiedliche Deals mit Eintritt im ausgewählten
  Zeitraum, die danach bis heute die unmittelbar folgende Stufe erreichten.
  Rückkehr in eine Stufe erzeugt keinen zweiten Kohortenteilnehmer.
  Verweildauer zählt abgeschlossene Aufenthalte; Pipeline-Bestand ist aktuell.
- ARR = Gesamtbetrag / Laufzeitmonate × 12 für aktuell laufende gewonnene
  Vertragsdeals. Fehlende Laufzeiten machen ARR nicht berechenbar.
  Das separate RecurringAmount-Feld wird nicht als gleichwertiger ARR addiert.
- Kundenbestand und Churn benötigen historische Statusintervalle und
  Erstellungsdaten. Fehlende Ausgangshistorie wird nicht als Bestand 0 ausgegeben.
  Historische Branchenverteilungen verwenden die aktuell importierte Branche.
- Umsatz je Kopf verwendet unterschiedliche Besitzer mit gewonnenem Umsatz.
  Ein historischer Personalbestand ist nicht vorhanden; die UI benennt den
  tatsächlich verwendeten Nenner ausdrücklich.
- Monats-/Quartalsziele: expliziter Periodenwert vor Jahresverteilung. Ohne
  Perioden werden zwölf Monate und vier Quartale erzeugt. Gewichte müssen
  insgesamt 1 bzw. 100 ergeben; ungültige Summen werden nicht normalisiert.
  Differenzen gerundeter kumulierter Ziele sichern die Cent-Summe.
- Währungen werden nicht still umgerechnet. Nicht vergleichbare Summen,
  Durchschnittswerte und gemeinsame Diagrammskalen werden als nicht
  berechenbar angezeigt. Fehlende Währung bleibt nach bestehendem Vertrag EUR.
- Kontaktfilter verwenden den Zeitpunkt der Reportantwort. Kundenfilter
  wirken auf Karte und Kundentabelle; regionale Gesamtberichte sind
  ausdrücklich als unfilterter Bestand erkennbar.

## Voraussetzungen und verbleibende Abnahmegrenzen

1. Die produktiven Analytics-Zahlen sind nicht als identisch abgenommen.
   Screenshots zeigen nicht alle Quellfilter und Joins. Gleicher Tenant,
   Importstand, Zeitraum und Datensatz-IDs müssen verglichen werden; keine
   Soll-Zahlen aus Bildern wurden in die Anwendung eingebaut.
2. Für historische Bestände, Stage-Historie und Arbeitszeiten sind tatsächliche
   CRM-Historien bzw. konfigurierte Kalender notwendig. Keine erfundenen
   Rückrechnungen oder künstlichen Kontakte.
3. Kanonische Leads besitzen derzeit keine eigene Adresse, Besitzer keinen
   Standort. Optionale tenantbezogene Report-Standorte überbrücken dies
   anbieterneutral. Bei fehlender Verortung wird die Abdeckung ausgewiesen.
   Distanzberechnung benötigt vollständige Koordinaten und zeigt Luftlinie,
   keine Fahrstrecke. Weiße Flecken ohne irgendeine Lead-Adresse sind nicht
   belastbar bestimmbar.
4. Die alternative PLZ-Flächenkarte nach Umsatz oder Kundenanzahl ist
   implementiert. Sie benötigt echte GeoJSON-Gebietsgrenzen in
   sales.reports.postalAreas (FeatureCollection; Polygon/MultiPolygon mit
   countryCode und postalPrefix in properties). Ohne diesen im Projekt nicht
   vorhandenen Grenzdatensatz erklärt die UI die fehlende Voraussetzung.
   Näherungspositionen werden nicht als erfundene PLZ-Grenzen ausgegeben.
5. Standorte anhand Stadt/PLZ/Land können nur Näherungen sein; die Karte
   kennzeichnet sie. Kunden ohne Standort bleiben in Tabelle/Zähler enthalten.
6. Keine DB-Migration für diese Reportprojektionen erforderlich. Die UI allein
   benötigt keinen neuen Import. Der korrigierte Produkt-/Termin-Aliasleser
   wirkt erst bei erneuter Verarbeitung betroffener CRM-Datensätze; ein
   vollständiger Import kann nötig sein, wenn diese im CRM unverändert sind.
7. Neue Report-Katalogeinträge werden in alten Layouts ergänzt. Ausdrücklich
   ausgeblendete Reports und Rollenrechte bleiben wirksam.

## Umsetzung

Backend: SalesSpecifiedReports, SalesProcessReports, SalesLifetimeReports,
SalesSourceReports; gemeinsame SalesReportEvidence. Frontend: SpecifiedReports
innerhalb der bestehenden ReportsPage und thematisch eingebettet in WorklistWidget.
Kanonische Rohdaten werden über die vorhandene tenantisolierte Lesesession geladen.
Keine neuen ungeschützten Endpunkte, keine zusätzliche Datenbank.

Prüfungen verwenden synthetische kanonische Datensätze, keine produktiven Daten.
Das aktuelle lokale Prüfprotokoll steht in 09-deployment-und-betriebsstand.md.
