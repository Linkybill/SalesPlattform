# Abgleich der gelieferten Analytics-Screenshots – 09.10.2026

Der Benutzer meldet fachlich abweichende Ergebnisse trotz ausgerollter Kuchen.
Die ursprünglichen PNGs wurden jetzt tatsächlich unter dem lokalen Windows-
Screenshotpfad geöffnet. Dieser Abgleich ersetzt keine Prüfung der Analytics-
Reportdefinitionen oder der produktiven, angemeldeten Sales-API.

## Nachfolgende Umsetzung

Die Tabelle unten dokumentiert den Ausgangsbefund vor der Ergänzung. Der
aktuelle lokale Stand einschließlich neuer Kontaktlisten, individueller
Produktzählung, vollständiger Gruppen und Von/Bis-Spalte steht in
[11-report-umsetzungsstand.md](11-report-umsetzungsstand.md).
Erweiterung anschließend mit Release 37974521619 ausgerollt; produktive Zahlenparität bleibt offen.

## Referenzen und Implementierung beim ersten Abgleich

| Referenz | Sichtbarer Inhalt | Sales-Berechnung / Abweichung |
| --- | --- | --- |
| Diagramme1.png | Jahresübersicht; Umsatz nach Branchen im aktuellen Jahr, Gesamtbetrag 10.951 EUR | Sales summiert aktive gewonnene Deals nach Abschlussdatum, ersatzweise Änderungsdatum. Der aktuelle Benutzerscreenshot zeigt hingegen Monat/Oktober und 1.800 EUR. Jahresvergleich fehlt; Unterschied allein beweist keinen Syncfehler. |
| Diagramme1.png | Umsatz nach Produkte im aktuellen Jahr; mehrere Produktgruppen | Sales gruppiert dieselben gewonnenen Deals über ProductId/Produktname. „Ohne Produkt“ bedeutet fehlende kanonische Zuordnung; ob Quelle, Import oder Lookup schuld ist, muss je Datensatz geprüft werden. |
| Diagramme1.png | Top Produkte im aktuellen Jahr; Y-Achse „Produkt individuelle Zählung“, mehrere Gruppen mit Wert 1 | Sales zählt gewonnene Deals pro Produkt. Individuelle Produktzählung ist eine andere Metrik; identischer Titel belegt keine Gleichheit. Grundmenge/Originalaggregation noch ungeklärt. |
| Diagramme1_1.png | Erstgespräche nach Branche; mehr als acht Branchen einzeln | Sales filtert Terminbeginn im Reportzeitraum und exakte konfigurierte Terminarten, alle Status; Top 8 + Sonstige. Originalfilter und verwendetes Typfeld sind im PNG unsichtbar. Gruppierung ist sichtbar abweichend. |
| Diagramme1_1.png | Folgetermine nach Branche; sechs Branchen, insgesamt 12 | Sales verwendet eigene konfigurierte Folgeterminarten und Beginnfilter, nicht eine nachgewiesene Analytics-Definition. Produktiver Soll/Ist-Abgleich offen. |
| Diagramme1_1.png / diagramme1_2.png | Offene Angebot nach Branche; sechs Branchen, insgesamt 8 | Sales zeigt zwei getrennte Auswertungen: offene Deals in konfigurierten Angebotsstufen (aktueller Bestand) und offene Angebotsbelege nach Ausstellungsdatum. Welche Quelle und Filter das Original nutzt, ist nicht aus dem Bild feststellbar. Beide zählen als Implementierungen, aber keine ist als identischer Ersatz nachgewiesen. |
| meetings_AktuellerMonat.png | Im aktuellen Monat: durchgeführt 7, alle Meetings 61, abgesagt 3, verschoben 1; Wochenlisten abgesagt/verschoben, alle Meetings dieser Kalenderwoche | Entsprechende Kennzahlen und wählbare Listen vorhanden. Monatsbeginnfilter, Statusmapping, Wochen-/Zeitzonengrenzen und originale Datumswahl noch nicht abgeglichen. Sales zeigt die Listen nacheinander über Auswahl, Original nebeneinander. |
| TerminwiedervorlagenNaechste5Tage.png | Vorbereitung für nächsten Termin; Kunde, Besitzer, Von, Bis, Status | Sales bietet nächste fünf Kalendertage einschließlich heute in UTC, alle Status. Nachweistabelle zeigt Terminbeginn, aber keine eigene Bis-Spalte. Originalkalender/-Zeitzone und genaue Grenze noch offen. |
| schlummerndeLeads2.png | Interessenten mit Termin / Kontakt > 5 Monate; separat Kunden ohne Interesse und letzter Kontakt > 5 Monate | Sales-Arbeitsliste verwendet R-07 (Default 90 Tage) und separat verlorene Deals; Kontaktmarker aus E-Mail/qualifiziertem Anruf, nicht dieselbe nachgewiesene Kombination aus letztem Termin, letztem Kontakt und Interessentenstatus. Dies sind fachlich verschiedene Listen. |
| telefonwiedervorlagen_ErfolgloseAnrufe.png | Erfolglose Anrufversuche unter 5 Versuchen; PLZ/Ort, letzter Versuch, Besitzer | Sales R-01 verwendet Versuche seit letztem qualifizierten Gespräch und Fälligkeit/Arbeitslistenstatus. Originalzähler und Fälligkeitsfilter noch ungeklärt. |
| schlummerndeLeads.png | SharePoint-Mitgliederübersicht | Inhalt passt nicht zum Dateinamen; keine Sales-Reportreferenz. Keine personenbezogenen Zeilen werden übernommen. |

## Nachweisbare Importprobleme

ZohoFieldReader.Find beendet die Suche beim ersten vorhandenen JSON-Feld auch
bei null oder leerem String. Damit verdrängt Type:null ein befülltes
Appointment_Type; Product_Name:null ein befülltes Product oder Produkt.
Das ist ein reproduzierbarer Codefehler, kein nachgewiesener Livebefund.

Produkt ist bereits ein unterstützter Mappingalias, fehlt aber in bevorzugten
Deals-Feldern und im Produkt-ID-Lookup. Die normale Schemaauswahl hängt alle
Metadatenfelder an und begrenzt auf 50: ein späterer Produktalias kann fehlen;
Hook-Fetches verwenden direkt bevorzugte Felder. Fehlendes Produkt darf nicht
durch Raten aus Dealnamen oder künstliche Standardprodukte ersetzt werden.

## Abnahmegrenze und nächste fachliche Prüfung

Kuchendarstellung und synthetische Regressionen sind geprüft. Datenparität zum
Original ist ausdrücklich nicht abgenommen. Dafür müssen bei gleichem Tenant,
Datenstand, Zeitraum und Zeitzone die Analytics-Definitionen (Quellmodul, Feld,
Aggregation, Filter, Beziehungen) und Datensatz-IDs gegen Sales-Nachweise geprüft
werden. Aggregate aus Screenshots sind Vergleichswerte, keine zu importierenden
oder im Code zu hinterlegenden Geschäftsdaten.

Die Quelldefinitionen für Erstgespräche, Folgetermine und offene Angebote wurden
beim Benutzer angefragt. Aus Bildern allein wird keine fehlende Definition
erfunden. Ein Vollimport kann falsche Reportsemantik nicht korrigieren.
