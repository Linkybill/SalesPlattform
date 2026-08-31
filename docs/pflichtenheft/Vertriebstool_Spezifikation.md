# Vertriebstool – Pflichtenheft

> Konvertierte, tokenfreundliche Markdown-Fassung von
> `Vertriebstool_Spezifikation.html`.

**Charakter:** eigenständiges Dashboard- und Steuerungstool. Es liest Daten aus
dem CRM und hält Logik, Regeln und Auswertungen selbst.

**Datenquelle:** Zoho CRM, zunächst lesend.
**Beispieldaten:** anonymisiert (`Kunde 01–17`, `Mitarbeiter A–E`, `Produkt 1–8`).

Die Diagramme und das HTML-Layout der gelieferten Datei sind in dieser Fassung
nicht enthalten; ihre fachlichen Aussagen und Beschriftungen sind als Text
übernommen. Dieses Dokument ist eine Anforderungsspezifikation, keine
ausführbare Arbeitsanweisung.

## 01 Zweck und Prinzip

Das System kennt den Vertriebsprozess, nicht der Vertriebler. Beim Öffnen soll
eine fertige, sortierte Liste vorliegen: wen heute anrufen und warum. Das
Regelwerk entscheidet, welche Wiedervorlage ansteht, welcher Kunde vergessen
wurde und welcher Vertrag ausläuft.

- **Erinnern:** Kein Kunde und kein Lead fällt durchs Raster. Jeder Kontakt
  erhält automatisch einen nächsten Schritt oder wird bewusst geschlossen.
- **Priorisieren:** Aus vielen offenen Vorgängen entsteht eine Reihenfolge.
- **Messen:** Vertrieb sieht sein Ziel, die Leitung das Team und die
  Geschäftsführung das Gesamtbild.

### Abgrenzung zum CRM

Das CRM bleibt führend für Stammdaten und Vorgänge. Die Salesplattform liest,
rechnet und priorisiert; sie ersetzt das CRM nicht. Schreibende Zugriffe sind
auf protokollierte Anrufe, erledigte Wiedervorlagen und Besitzerwechsel
beschränkt, optional und abschaltbar.

## 02 Architektur

Das Tool greift nicht bei jedem Seitenaufruf live auf das CRM zu. Ein Sync-Job
spiegelt Daten periodisch in eine eigene Datenbank, auf der Regelwerk und
Berechnung arbeiten.

```text
CRM
  -> periodischer Sync
Eigene Datenbank
  -> Regelengine / Berechnung
Ansichten

Optionaler, abschaltbarer Weg:
Tool -> begrenztes Rückschreiben ins CRM
```

Die eigene Datenbank speichert zusätzlich zur CRM-Spiegelung Stage-Historie,
Snapshots, Ziele und Konfiguration. Für Deals und Aktivitäten ist ein
stündlicher Sync vorgesehen, für Accounts und Produkte ein täglicher Sync.
Ein manueller Refresh ist zusätzlich möglich.

## 03 Ansichten und Rollen

| Ansicht | Zielgruppe | Zweck und Inhalt |
|---|---|---|
| Meine Arbeitsliste | Vertrieb | Eine priorisierte Liste über alle Vorgangsarten: Wiedervorlagen, hängende Deals, auslaufende Verträge, schlummernde Leads |
| Cockpit | Geschäftsführung | Statusampel, acht Kennzahlen, Funnel mit Conversion-Raten, höchstens fünf Handlungspunkte |
| Team-Steuerung | Vertriebsleitung | Zielerreichung und Aktivität je Mitarbeiter, Termine nach Typ, Anrufe, Angebote, Win Rate, Pipeline |
| Analyse | Leitung, Geschäftsführung | Umsatz nach Branche/Produkt/Region, Verlustgründe, Verweildauer je Stufe, Cross-Selling, Monat/Jahr/Lifetime |
| Kundenstamm | Leitung, Geschäftsführung | Weltkarte, Start im deutschsprachigen Raum, Filter und Gebietsanalyse |
| Aufräumen | Leitung, Backoffice | Mögliche Dubletten mit Sicherheitsstufe; Merge nur nach manueller Freigabe |

### Sichtbarkeit

- Arbeitslisten zeigen standardmäßig eigene Vorgänge und können auf Teamansicht
  umgeschaltet werden.
- Zielerreichung und Ranking sind teamweit sichtbar, absolut und prozentual.
- Das Cockpit ist für Leitung und Geschäftsführung reserviert.

## 04 Datenquellen

Alle Felder werden lesend über die API bezogen und in der eigenen Datenbank
gespiegelt. Ein Deal entspricht genau einem Produkt, daher sind Umsätze direkt
summierbar.

| Modul | Pflichtfelder | Empfohlen | Verwendung |
|---|---|---|---|
| Deals | `id`, `account_id`, `amount`, `stage`, `pipeline`, `created_date`, `closing_date`, `owner`, `produkt`, `laufzeit`, `vertragsende`, `stage_history` | `verlustgrund` | Umsatz, Funnel, Win Rate, Renewal, ARR, Cross-Selling |
| Accounts | `id`, `branche`, `plz`, `ort`, `land`, `owner`, `status`, `created_date` | – | Kundenstamm, Region, Status, Neukunden |
| Leads | `id`, `created_date`, `lead_source`, `status`, `letzter_anruf`, `anrufversuche` | – | Response-SLA, Reaktivierung, Anrufstaffelung |
| Aktivitäten | `related_to`, `typ`, `datum`, `owner`, `gespraechsdauer` | `anrufrichtung` | Touchpoints, hängende Deals, Kontaktmessung |
| Termine | `start`, `ende`, `status` | `termin_typ` | Auslastung, Meeting Report, R-12 |

`NULL` bei `letzter_anruf` bedeutet „nie kontaktiert“. Platzhalterwerte wie
`1900-01-01` werden beim Import ebenfalls als `NULL` behandelt. Kombinierte
Produktangaben wie `Produkt 3;Produkt 5` sind ein Datenqualitätshinweis und
keine eigene Produktkategorie.

## 05 Priorisierung

Die vier möglichen Quelllisten werden zu einer einzigen Liste zusammengeführt.
Die Standardreihenfolge entsteht aus Vorgangsart, Alter und Wert:

```text
score = basiswert(vorgangsart) + altersbonus + wertbonus
altersbonus = min(tage_überfällig * 0.5, 30)
wertbonus = min(deal_betrag / 10.000, 20)
```

| Vorgangsart | Basiswert |
|---|---:|
| Vertrag unter 30 Tagen vor Ende | 100 |
| Lead-Reaktion offen | 95 |
| Hängender Deal | 80 |
| Vertrag unter 90 Tagen vor Ende | 70 |
| Wiedervorlage fällig | 60 |
| Agent-/Besitzerwechsel | 50 |
| Schlummernder Lead | 30 |
| Cross-Selling | 20 |

Der Altersbonus ist bei 30, der Wertbonus bei 20 Punkten gedeckelt. Basiswerte,
Deckelungen und Divisor sind konfigurierbar. Der Punktwert wird angezeigt;
optionale Filter nach Vorgangsart sind nicht der Standardweg.

Beispiel der erwarteten Darstellung: `Kunde 01 – Vertrag endet in 18 Tagen –
61.000 € – Score 126` vor einem Deal mit 67 Tagen ohne Aktivität und vor einem
nie kontaktierten Lead.

## 06 Regelwerk

Jede Regel prüft eine Bedingung, erzeugt bzw. verwaltet einen Vorgang und ordnet
ihn einem Besitzer zu. Alle Schwellwerte und Intervalle sind konfigurierbar.

### Was als Gespräch zählt

Maßgeblich ist standardmäßig die vom CRM protokollierte Gesprächsdauer:

| Dauer | Bewertung | Zähler |
|---|---|---|
| 0 Sekunden | nicht verbunden | Versuche seit Gespräch +1 |
| 1–19 Sekunden | kein Gespräch | Versuche seit Gespräch +1 |
| ab 20 Sekunden | Gespräch | Versuche seit Gespräch auf 0; Gesamtzähler läuft weiter |

Die Schwelle darf je Pipeline unterschiedlich konfiguriert werden. Es gibt zwei
Zähler:

- **Versuche seit letztem Gespräch:** Reset bei Gespräch; steuert R-01 bis R-04.
- **Versuche gesamt:** kumulativ; dient nur der Auswertung und Touchpoint-KPI.

Eine besprochene Mailbox kann länger als 20 Sekunden dauern. Liefert das
Telefonsystem einen Verbindungsstatus, hat dieser Vorrang. Sonst braucht die
Protokollierung eine schnelle Korrektur „war Mailbox“.

### Regeln

| ID | Bedingung | Aktion |
|---|---|---|
| R-01 | Anruf mit `gespraechsdauer < 20 s` und `versuche_seit_gespraech <= 5` | Wiedervorlage nach 14 Tagen, gleicher Besitzer, Zähler erhöhen |
| R-02 | `versuche_seit_gespraech = 5`, weiterhin kein Gespräch | E-Mail-Vorlage vorschlagen, Wiedervorlage nach 14 Tagen, „Mail senden“ markieren; manuell/automatisch konfigurierbar |
| R-03 | Versuche seit Gespräch zwischen 6 und 10 | Intervall auf 30 Tage verlängern, als „Langläufer“ markieren |
| R-04 | Mehr als 10 Versuche ohne Gespräch | „Nicht erreichbar“ vorschlagen; keine automatische Statusänderung, Besitzerfreigabe nötig |
| R-05 | Offener Deal und mehr als 30 Tage seit letzter Aktivität | Rot in Besitzerliste; ab 60 Tagen Cockpit-Handlungspunkt |
| R-06 | Vertragsende innerhalb 90 Tagen | Renewal-Vorgang für Besitzer; unter 30 Tagen höchste Priorität und Management-Hinweis |
| R-07 | Letzter Kontakt älter als 3 Monate oder `NULL` | Reaktivierung, älteste Leads zuerst |
| R-08 | Stage „Agent Wechsel“ oder Regelbedingung | Besitzerwechsel-Liste mit altem Besitzer, Kontakt und Wert; Leitung entscheidet |
| R-09 | Neuer Lead ohne Aktivität nach 1 Arbeitsstunde | Besitzer benachrichtigen; nach 4 Arbeitsstunden eskalieren; höchste Priorität |
| R-10 | Aktiver Kunde ohne Deal in definierter Produktkategorie | Cross-Selling-Liste; Kategorien und Mindestkundenwert konfigurierbar |
| R-11 | Zielerreichung mehr als 15 Punkte unter Zeitanteil | Team-Flag und Benachrichtigung an Leitung |
| R-12 | Termin mindestens dreimal verschoben | Verdachtsfall in Arbeitsliste; Deal klären oder als verloren markieren |
| R-13 | Aktiver Kunde mit Umsatzhistorie und mehr als 3 Monate ohne Telefon | Account-Care-Liste; Zeitraum und Mindestumsatz konfigurierbar |
| R-14 | Verlorener Deal älter als 3 Monate, Verlustgrund Timing/Budget | Reaktivierungsvorschlag an früheren Besitzer |

Wiedervorlagen erzeugt und verwaltet die Salesplattform selbst; sie ist nicht
nur eine Anzeige vorhandener CRM-Aufgaben.

## 07 Sortierung je Liste

Umsortieren bleibt möglich, wird aber nicht als neue Standardsortierung
gespeichert.

| Liste | Standardsortierung |
|---|---|
| Meine Arbeitsliste | Score absteigend, bei Gleichstand ältester Vorgang zuerst |
| Schlummernde Leads | `letzter_kontakt` aufsteigend, `NULL` zuerst, danach ältestes Datum |
| Wiedervorlagen | Fälligkeit aufsteigend, überfällige zuerst |
| Hängende Deals | Tage ohne Aktivität absteigend, danach Betrag absteigend |
| Auslaufende Verträge | Vertragsende aufsteigend |
| Agent-Wechsel | Kundenwert absteigend, danach letzter Kontakt aufsteigend |
| Cross-Selling | Bisheriger Kundenumsatz absteigend |

## 08 Agent-Wechsel

Ein Kunde gelangt manuell über die Stage oder regelbasiert in dieselbe
Wechselliste. Eine Regel kann beispielsweise greifen, wenn der Kunde länger als
sechs Monate beim aktuellen Besitzer liegt und länger als drei Monate keinen
Kontakt hatte.

- Sortierung nach Kundenwert.
- Leitung wählt den neuen Besitzer; kein automatischer Besitzerwechsel.
- Vorschläge dürfen aktuelle Auslastung und regionale Nähe berücksichtigen.
- Nach Zuordnung entsteht eine Wiedervorlage für den neuen Besitzer mit sieben
  Tagen Frist.
- Protokollieren: alter Besitzer, neuer Besitzer, Zeitpunkt und auslösende
  Regel.
- Rückschreiben in Zoho ist optional und konfigurierbar.

## 09 Aufräumen – Dublettenprüfung

Das Tool erkennt Verdachtsfälle, entscheidet aber nicht selbst. Erst die
Kombination mehrerer Merkmale ergibt einen belastbaren Verdacht.

| Merkmal | Punkte | Prüfung |
|---|---:|---|
| Identische Steuer-/USt-ID | 60 | Exakter Vergleich |
| Identische E-Mail-Domain | 35 | Teil nach `@`; Freemail ausschließen |
| Identische Telefonnummer | 30 | Ländervorwahl, Leer- und Sonderzeichen normalisieren |
| Namensähnlichkeit >90 % | 30 | Nach Normalisierung |
| Namensähnlichkeit 75–90 % | 15 | Nach Normalisierung |
| Identische Anschrift | 25 | Straße, Hausnummer und PLZ |
| Identische PLZ | 10 | Allein schwach, in Kombination relevant |
| Identische Website-Domain | 30 | Protokoll und `www` entfernen |

Vor dem Vergleich werden Rechtsformen (`GmbH`, `AG`, `KG`, `e. K.`, `mbH`,
`gGmbH`, `UG`, `OHG`, `SE`, `Co.`), Groß-/Kleinschreibung, Umlaute,
Satzzeichen, typische Zusätze wie „Holding“ und „Gruppe“ sowie bekannte
Kodierungsfehler normalisiert.

| Punktzahl | Sicherheitsstufe |
|---:|---|
| ab 100 | sicher |
| ab 60 | wahrscheinlich |
| ab 40 | prüfen |
| unter 40 | ausblenden |

Zusammenführen ist nie automatisch, auch nicht bei „sicher“. Beide Datensätze
werden mit allen Abweichungen gegenübergestellt. Ein Mensch wählt je Feld den
führenden Wert; standardmäßig gilt mehr Umsatz und jüngerer Kontakt. Deals,
Aktivitäten und Termine werden übertragen, nicht still gelöscht. „Kein Duplikat“
bleibt gespeichert; jeder Merge wird protokolliert. Bei aktivem Rückschreiben
muss der Merge im CRM erfolgen, danach synchronisiert die Salesplattform.

Konzern und Tochter können wie Dubletten aussehen, aber echte getrennte Kunden
sein. Die Abbildung einer Konzernbeziehung statt nur „kein Duplikat“ ist offen.

## 10 Cockpit

Die Geschäftsführung soll in ungefähr 30 Sekunden erkennen, ob etwas nicht
stimmt. Das Cockpit enthält eine Statusampel, acht Kennzahlen, einen Funnel und
höchstens fünf Handlungspunkte. Jede Kachel ist klickbar.

Die acht Kern-KPIs sind: gewonnener Umsatz gegen Jahresziel, Win Rate gegen das
Vorquartal, Pipeline-Deckung gegen Zielkorridor, Sales-Cycle-Tage, ARR-Anteil,
Neu- vs. Bestandsumsatz, hängende Deals und auslaufende Verträge. Der Funnel
zeigt mindestens die Stufen Termin vereinbart, Follow-up-Termin, Angebot,
Commitment sowie POC. Handlungspunkte kommen insbesondere aus R-05, R-06 und
R-11, zum Beispiel dünne Pipeline, inaktive Deals oder sinkende Angebotsquote.

## 11 Team-Steuerung

Umsatz allein bewertet Mitarbeiter unfair, da Gebiete und Kundenstämme
unterschiedlich sind. Deshalb stehen Zielerreichung und Aktivität nebeneinander.

- Zeitanteil des Geschäftsjahres als gestrichelte Vergleichslinie.
- Sortierung nach Zielerreichung in Prozent, nicht absolutem Umsatz.
- Termine je Mitarbeiter nach Typ: Kennenlernen, Angebotsbesprechung, Feedback
  und Update, Demo und POC.
- Die Aufteilung zeigt, wer Deals bewegt und wer in der Anbahnung feststeckt;
  viele reine Kennenlerntermine können Coaching-Bedarf anzeigen.

## 12 Meeting Report

Termine sind ein Frühindikator: Was diese Woche entsteht, wird häufig in zwei
bis drei Monaten Umsatz. Daher werden nicht nur stattgefundene, sondern auch
abgesagte, verschobene und versäumte Termine gemessen.

| Kennzahl | Bewertung | Aussage |
|---|---|---|
| Neu angelegte Termine | positiv | Aktivitätsindikator; sinkt der Wert mehrere Wochen, droht späterer Pipeline-Einbruch |
| Termine der Kalenderwoche | neutral | Auslastung und Kapazitätsplanung |
| Abgesagte Termine | beobachten | Hohe Quote kann auf schwache Erstqualifizierung deuten |
| Verschobene Termine | beobachten | Einmalig unkritisch; mehrfach verschoben faktisch tote Deals |
| Nicht erschienen | kritisch | Zeitverlust ohne Absage; Bestätigung am Vortag kann helfen |

| Quote | Berechnung | Zielwert |
|---|---|---|
| Durchführungsquote | `stattgefunden / geplant × 100` | über 80 % |
| No-Show-Quote | `nicht_erschienen / geplant × 100` | unter 5 % |
| Verschiebequote | `verschoben / geplant × 100` | unter 15 % |
| Termin-zu-Angebot | `angebote / stattgefundene_termine × 100` | je Pipeline verschieden |

Dreimal verschobene Termine sind ein eigener Zustand und fallen unter R-12.

## 13 Analyse

Struktur geht vor Vollständigkeit. Verteilungen werden auf relevante Positionen
begrenzt; der Rest erscheint als aufklappbare Sammelposition „Sonstige“.

| Auswertung | Darstellung | Begrenzung |
|---|---|---|
| Umsatz nach Produkt | Balken, absteigend | Top 8 plus Sonstige |
| Umsatz nach Branche | Balken, absteigend | Top 8 plus Sonstige; kein Kreisdiagramm |
| Umsatz nach Region | Karte plus Rangliste | zweistelliger PLZ-Bereich |
| Verlustgründe | Balken mit kumulierter Linie | alle Gründe plus Anteil ohne Angabe |
| Verweildauer je Stufe | Balken je Stufe | je Pipeline getrennt |
| Cross-Selling | Matrix Kunde × Kategorie | leere Zellen sind Verkaufschancen |

## 14 Zeitebenen und Lifetime

Die aktuelle Ebene muss immer sichtbar sein.

| Ebene | Frage | Zielgruppe | Typische Inhalte |
|---|---|---|---|
| Monat | Läuft es gerade? | Leitung, Team | gewonnener/verlorener Umsatz, Abschlüsse, Termine, Anrufe, Top-Produkte der letzten 30 Tage, neue Kunden |
| Jahr | Erreichen wir die Ziele? | Leitung, GF | Umsatz gegen Ziel, Pipeline, Win Rate, Angebotsquote, Branche/Produkt, Prozessquoten |
| Lifetime | Wohin entwickelt sich das Unternehmen? | GF | Umsatzentwicklung, Produktportfolio, Branchenmix, Kundenbestand, Mitarbeiterentwicklung |

Lifetime ist keine vergrößerte Jahresansicht. Sie zeigt unter anderem Umsatz je
Jahr, Produktmix, Branchenmix, verkaufte Produkte, Kundenbestand mit Zu- und
Abgang, gewonnene/verlorene Abschlüsse, verlorenen gegen gewonnenen Umsatz,
Telefon- und Terminaktivität je Mitarbeiter, durchschnittlichen Deal-Wert und
Umsatz je Kopf. Absolute und prozentuale Darstellung müssen umschaltbar sein:
absolut zeigt Wachstum, prozentual Abhängigkeit bzw. Mixverschiebung.

## 15 Kundenstamm – Karte

Eine zoombare Weltkarte startet im deutschsprachigen Raum. Verortung erfolgt
über Land plus Postleitzahl; Ortsebene genügt. Beim Herauszoomen werden
internationale Kunden sichtbar.

- Zoomstufen: weltweit, Kontinent, Land, Bundesland/Region und PLZ-Bereich.
- Ein Punkt je Kunde, Punktgröße nach Umsatz.
- Bei hoher Dichte Bündelung mit Anzahl; beim Hineinzoomen Auflösung.
- Alternative Flächendarstellung durch Einfärbung von PLZ-Bereichen nach Umsatz
  oder Kundenanzahl.
- Klick zeigt Kundenname, Betreuer, Umsatz, letzten Kontakt und offene Deals.
- Filter: Betreuer, Branche, Produkt, Kundenstatus, Kontaktalter und
  Umsatzschwellwert.

Neben der Karte erscheinen PLZ-Ranglisten nach Umsatz und Anzahl, Umsatz je
Land/Region mit Vorjahresvergleich, weiße Flecken mit vorhandenen Leads und die
durchschnittliche Entfernung zwischen Betreuer und Kunden.

Kunden ohne Land oder PLZ dürfen nicht unsichtbar verschwinden. Die Anzahl
nicht verortbarer Kunden wird neben der Karte angezeigt. Fehlt das Land, kann
beim Import Deutschland vorbelegt und der Datensatz zur Prüfung markiert werden.

## 16 Ziele und Pace

Jeder Mitarbeiter erhält ein Jahresziel, das auf Quartal und Monat verteilt
wird. Standardmäßig ist die Verteilung gleichmäßig; saisonale Quartalsgewichte
wie `20 / 25 / 20 / 35 %` sind optional und müssen 100 % ergeben.

```text
zeitanteil = vergangene_tage_im_gj / gesamttage_im_gj × 100
zielerreichung = umsatz_ytd / jahresziel × 100
pace = zielerreichung − zeitanteil
```

| Pace | Status | Bedeutung |
|---:|---|---|
| ≥ +5 | Vor Plan | über zeitanteiligem Ziel |
| −5 bis +5 | Im Plan | auf Kurs |
| −15 bis −5 | Rückstand | aufholbar, beobachten |
| < −15 | Kritisch | löst R-11 aus |

Das Datenmodell speichert ein Ziel je Mitarbeiter, Zeitraum und Zielart – nicht
als einzelnes Mitarbeiterfeld. Ziele werden im Tool gepflegt, nicht im CRM oder
im Code, und sind teamweit sichtbar.

Zusätzliche Aktivitätsziele: erreichte Gespräche pro Woche, neue Termine pro
Monat, versendete Angebote pro Monat und neue Deals in der Pipeline pro Monat.
Nur erreichte Gespräche zählen als Anrufziel; Termine können nach Typ getrennt
werden.

## 17 KPI-Katalog

Die Berechnung liegt vollständig im Tool; das CRM liefert Rohdaten.

### Cockpit

| Nr. | Kennzahl | Berechnung | Ampel/Hinweis |
|---|---|---|---|
| 1.1 | Umsatz gewonnen | `SUM(amount) WHERE stage='Gewonnen' AND closing_date IN gj` | grün >90 % Ziel, rot <70 % |
| 1.2 | Win Rate | `COUNT(gewonnen) / (COUNT(gewonnen)+COUNT(verloren)) × 100` | grün >35 %, rot <20 % |
| 1.3 | Pipeline-Deckung | `SUM(offene.amount) / (jahresziel − umsatz_ytd)` | grün >3×, rot <2× |
| 1.4 | Sales Cycle | `AVG(closing_date − created_date) WHERE gewonnen` | Vergleich Vorquartal |
| 1.5 | ARR | `SUM(amount / laufzeit_jahre) WHERE vertrag aktiv` | Anteil am Gesamtumsatz |
| 1.6 | Neu vs. Bestand | Neukunde, wenn erster gewonnener Deal des Accounts | informativ |
| 1.7 | Hängende Deals | `COUNT WHERE offen AND (heute − letzte_aktivität) > 30` | rot ab 10 oder 100 T€ |
| 1.8 | Auslaufende Verträge | Anzahl und Summe bei Vertragsende in 90 Tagen | gelb 90 Tage, rot 30 Tage |

### Funnel

| Nr. | Kennzahl | Berechnung | Hinweis |
|---|---|---|---|
| 2.1 | Conversion je Stufe | `COUNT(erreicht stufe n+1) / COUNT(erreicht stufe n) × 100` | aus `stage_history` |
| 2.2 | Pipeline je Stufe | `SUM(amount) GROUP BY stage WHERE offen` | je Pipeline getrennt |
| 2.3 | Gewichtete Pipeline | `SUM(amount × wahrscheinlichkeit(stage))` | Wahrscheinlichkeiten im Tool pflegen |
| 2.4 | Verweildauer je Stufe | `AVG(datum stufe n+1 − datum stufe n)` | Basis für R-05 |
| 2.5 | Verlustgründe | `COUNT GROUP BY verlustgrund WHERE verloren` | Anteil ohne Grund ausweisen |

### Team

| Nr. | Kennzahl | Berechnung | Hinweis |
|---|---|---|---|
| 3.1 | Termine je Mitarbeiter | `COUNT GROUP BY owner, termin_typ, zeitraum` | gestapelter Balken |
| 3.2 | Anrufe je Mitarbeiter | `COUNT GROUP BY owner, ergebnis, zeitraum` | erreicht/nicht erreicht trennen |
| 3.3 | Touchpoints bis Abschluss | `AVG(COUNT(aktivitäten je deal)) WHERE gewonnen` | je Pipeline getrennt |
| 3.4 | Lead-Response-Zeit | `AVG(erste_aktivität − lead.created_date)` | nur Arbeitszeit |
| 3.5 | Zielerreichung | `umsatz_ytd / jahresziel × 100` | zusammen mit Pace |

### Analyse

| Nr. | Kennzahl | Berechnung | Hinweis |
|---|---|---|---|
| 4.1 | Umsatz nach Branche | `SUM(amount) GROUP BY account.branche` | Top 8 plus Sonstige |
| 4.2 | Umsatz nach Produkt | `SUM(amount) GROUP BY produkt` | Top 8 plus Sonstige |
| 4.3 | Umsatz nach Region | `SUM(amount) GROUP BY plz_bereich` | zweistelliger PLZ-Bereich |
| 4.4 | Churn Rate | `verlorene_kunden / kunden_periodenstart × 100` | rot >10 % p. a. |
| 4.5 | Cross-Selling-Quote | `AVG(COUNT(DISTINCT produktkategorie) je aktivem Account)` | Matrix Kunde × Kategorie |

## 18 Technische Hinweise

| Thema | Festlegung |
|---|---|
| Datenhaltung | Eigene Datenbank mit periodischem Sync, kein Live-Durchgriff bei jedem Seitenaufruf |
| Sync | Deals/Aktivitäten stündlich, Accounts/Produkte täglich, manueller Refresh |
| Stage-Historie | Beim ersten Sync vollständig aus dem CRM abziehen und dauerhaft speichern |
| Snapshots | Täglich Pipeline-Wert, offene Deals, ARR und Zielerreichung speichern |
| Zeitzonen | UTC speichern, lokal anzeigen |
| Arbeitszeit | Arbeitszeitfenster für SLA-Berechnungen hinterlegen |
| Geschäftsjahr | Konfigurierbar, nicht zwingend Kalenderjahr |
| Schwellwerte | Grenzwerte, Intervalle und Basiswerte in Konfigurationsoberfläche |
| Berechtigungen | Arbeitslisten je Besitzer; Ziele teamweit; Cockpit nur Leitung/GF |
| Rückschreiben | Optional, explizit und abschaltbar: Wiedervorlagen, Anrufe, Besitzerwechsel |
| Pipelines | Von Beginn an fünf Pipelines mit eigenen Stufen und Schwellwerten |
| Datenqualität | Eigene Ansicht für fehlende Beträge, Verlustgründe, Branchen und kombinierte Produkte |

Ohne Snapshots sind zeitbezogene Aussagen wie „plus 18 Prozent zum Vormonat“
nicht belastbar.

## 19 Offene Punkte

### Geklärt

- Wiedervorlagen erzeugt und verwaltet das Tool selbst.
- Ein erfolgreicher Anruf beginnt ab 20 Sekunden; darunter ist es ein Versuch.
- Zwei Zähler: Reset-Zähler seit Gespräch und kumulativer Gesamtzähler.
- Weltkarte mit Startansicht deutschsprachiger Raum; Verortung über Land plus PLZ.
- Ein Deal entspricht einem Produkt; Umsätze sind direkt summierbar.
- Stage-Historie beim ersten Sync vollständig abziehen und dauerhaft speichern.
- Gesamtziel je Mitarbeiter und Geschäftsjahr, keine Aufteilung nach Pipeline,
  teamweit sichtbar.

### Noch offen

1. **Mailbox von Gespräch unterscheiden:** Verbindungsstatus des
   Telefonsystems oder Korrekturmöglichkeit in der Protokollierung.
2. **Eingehende Anrufe:** Ob sie den Staffelungszähler zurücksetzen; fachlicher
   Kontakt und Akquise-Messung sprechen für unterschiedliche Betrachtung.
3. **Basiswerte der Priorisierung:** Vorschlagswerte nach einigen Wochen
   Praxisbetrieb anhand echter Abarbeitungsreihenfolge nachjustieren.
4. **Konzernstrukturen:** Beziehung zwischen Konzern und Tochtergesellschaft
   möglicherweise explizit modellieren statt nur „kein Duplikat“ zu speichern.
