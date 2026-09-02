# Offene Punkte, Entscheidungen und Status

## Im Pflichtenheft ausdrücklich offen

1. **Mailbox vs. Gespräch:** Der Mapper berücksichtigt vorhandene
   Verbindungs-/Ergebnisfelder und behandelt Mailbox, nicht erreicht und
   falschen Ansprechpartner als Versuch. Falls Zoho dafür abweichende Werte
   liefert, müssen diese Werte in der Mapping-Konfiguration ergänzt werden.
2. **Eingehende Anrufe:** Setzen eingehende Anrufe den Staffelungszähler
   zurück? Für Kontakt spricht ein Reset, für Akquise-Messung eine getrennte
   Zählung.
3. **Basiswerte der Priorisierung:** Die vorgeschlagenen Punkte sind ein
   Startwert und sollen nach einigen Wochen Praxisbetrieb anhand echter
   Abarbeitungsreihenfolgen nachjustiert werden.
4. **Konzernstrukturen:** Konzern und Tochter sollen möglicherweise als
   Beziehung statt ausschließlich als „kein Duplikat“ modelliert werden.

## Geklärte fachliche Leitentscheidungen

- Das Tool erzeugt und verwaltet Wiedervorlagen selbst.
- Ein qualifiziertes Gespräch beginnt ab der appweiten Einstellung
  `sales.callConversationThresholdSeconds` (Standard: 20 Sekunden). Die
  Einstellung gilt für den gesamten Tenant und kann nicht pro Benutzer
  überschrieben werden; Mailbox, Nichterreichen und falscher Ansprechpartner
  bleiben unabhängig von der Dauer Versuche.
- Es gibt einen Staffelungszähler seit dem letzten Gespräch und einen
  kumulativen Auswertungszähler.
- Alle Regel-Zeit- und Versuchsschwellen werden als tenantbezogene
  `sales.rules.*`-App-Einstellungen mit den Pflichtenheft-Defaults gepflegt.
  Eine Änderung wirkt bei der nächsten Regelbewertung; die Regelengine hält
  keine fest verdrahteten Zeitgrenzen mehr vor.
- Die Karte ist weltweit, startet aber im deutschsprachigen Raum und nutzt Land
  plus PLZ.
- Ein Deal entspricht genau einem Produkt.
- Die Stage-Historie wird beim ersten Sync vollständig und dauerhaft übernommen.
- Ziele sind Gesamtziele je Mitarbeiter und Geschäftsjahr und teamweit sichtbar.

## Neue Architekturentscheidung

- Das Domainmodell und die Datenbank sind CRM-anbieterneutral.
- Zoho CRM wird als erster read-only Adapter umgesetzt.
- Die CRM-Auswahl wird nach dem Aufmaß-Muster je App/Mandant über das
  Application Setting `crm.integration` verwaltet. Zoho ist aktuell der erste
  auswählbare Provider; seine Client-Einstellungen erscheinen erst bei der
  Auswahl von Zoho. Die SalesPlattform führt den Zoho-OAuth-Codeaustausch und
  die Token-Erneuerung aus. OAuth-Refresh-Tokens werden über die allgemeine,
  provider-neutrale Credential-API der Identity Platform verschlüsselt
  verwaltet, nicht in der Tenant-Datenbank der SalesPlattform. Die Identity
  Platform enthält dabei keine Zoho-spezifischen URLs, Einstellungen oder
  Tokenlogik. Client-Secret und Refresh-Token liegen verschlüsselt in der
  app-eigenen, tenantisolierten Sales-Datenbank.
- Pipedrive und weitere CRM-Systeme werden später über eigene Adapter an dasselbe
  kanonische Modell angeschlossen.
- Anbieter-IDs, Rohdaten, OAuth-Verbindungen und Sync-Zustände bleiben im
  Integrationsbereich und werden nicht in fachliche Regeln geleakt.
- Jobdefinitionen, tenantbezogene Cron-Zeitpläne, Queue-Zustellung, Runhistorie
  und Live-UI sind generische Funktionen der Identity Platform. Die Sales-
  Implementierungen sind providerneutral; Zoho ist ein austauschbarer
  `ICrmSynchronizationAdapter`.
- Provider-Webhooks ergänzen später den festen 15-Minuten-Crawl. Sie ersetzen
  weder den Lückenschluss durch Incremental-Crawls noch den Reconciliation-
  Vollimport.
- Öffnende Links zu Ursprungsdatensätzen werden vom jeweiligen CRM-Adapter
  beim Import als optionale `ExternalUrl` an der Integrationszuordnung
  gespeichert. Sales-Arbeitsliste und Reports können daraus den Absprung
  rendern; Common und Identity Platform bleiben CRM-anbieterneutral.
- Das Zurückstellen eines Arbeitslisteneintrags erzeugt eine neue lokale
  Vorgangsinstanz mit `AvailableFrom` („Bearbeitung beginnen ab“). Die alte
  Instanz bleibt als `deferred`-Vorgänger historisch erhalten; der Nachfolger
  wird erst ab dem gespeicherten Zeitpunkt ausgeliefert.
- Die technische Laufzeitprüfung generischer Jobs erfolgt über eine Heartbeat-
  Lease des `IdentityPlatform.Shared`-Workers. Verwaiste `queued`- oder
  `running`-Läufe werden nach 90 Sekunden ohne Heartbeat aus der Platform-
  Runhistorie entfernt, bevor ein Lauf angezeigt oder ein neuer gestartet wird.
- Jobs können eine gemeinsame mandantenbezogene `ConcurrencyGroup` deklarieren.
  Die Plattform verhindert dann parallel eingeplante bzw. laufende Jobs dieser
  Gruppe. Der CRM-Vollimport und der inkrementelle Crawl teilen die Gruppe
  `crm-synchronization`; die Prüfung erfolgt beim Einplanen und nochmals beim
  atomaren Worker-Claim.
- Ein laufender Plattformjob kann aus der zentralen Detailansicht echt
  abgebrochen werden. Der Abbruch wird persistiert und über die Queue an den
  Worker weitergegeben; die Sales-Synchronisation reicht das
  `CancellationToken` bis zu Zoho-, Datenbank- und Batchoperationen durch.
- Ein neuer CRM-Lauf prüft neben der app-eigenen Sync-Historie den echten
  Plattformstatus. Ist ein gespeicherter Lauf nicht mehr `queued` oder
  `running`, wird der verwaiste app-eigene Lauf bereinigt und der neue Lauf
  darf starten. E-Mails bleiben eine Related-List desselben CRM-Laufs und
  werden nicht als eigener Job geführt.
- Die technische Zuordnung eines CRM-Datensatzes erfolgt ausschließlich über
  die bestehende Remote-ID im Integrationslink. Full-Crawls führen einen
  sicheren Missing-ID-Abgleich durch und löschen lokale Fachdaten niemals
  physisch. Bei einer gelöschten CRM-Task wird der lokale Vorgang geschlossen
  und in derselben Vorgangskette neu angelegt; bei einem gelöschten Lead,
  Kunden, Kontakt oder Deal wird der betroffene Vorgang mit
  `target-deleted-in-crm` geschlossen und nicht ersetzt.

## Noch zu entscheiden, bevor die Fachintegration beginnt

Diese Punkte sind technische Folgeentscheidungen aus dem Pflichtenheft und noch
keine stillschweigend getroffenen Anforderungen:

- konkrete Zoho-Organisation, API-Version, Module, Feld- und Stage-Mappings,
  Paging, Rate-Limits und Fehler-/Retry-Strategie;
- die gemeinsame Adapter-Schnittstelle und die unterstützten Capabilities für
  Lesen, Schreiben, Historie und Löschung je Anbieter;
- die fünf Pipelines, ihre Stufen und je Pipeline gültigen Wahrscheinlichkeiten;
- die fachliche Rollenmatrix und die Zuordnung zu Identity-Platform-Rollen;
- Geocoding-/Kartendienst, Datenschutz und Verhalten bei fehlender Adresse;
- Standard-Zeitzone, Geschäftsjahr, Feiertage und Arbeitszeitkalender;
- welche Rückschreibefunktionen zum ersten Release aktiviert werden;
- Zielarten, Genehmigung und Historisierung von Zieländerungen;
- Aufbewahrung und Archivierung von CRM-Historie, Snapshots und Auditdaten.

## Datenmodellstatus

- Das vollständige CRM-neutrale Zieldatenmodell ist in
  [`07-ziel-datenmodell.md`](./07-ziel-datenmodell.md) beschrieben und als
  EF-Modell umgesetzt.
- Die bestehenden `Sales*`-Tabellen und die
  `AddCrmIntegrationFoundation`-Migration bleiben als technische Grundlage;
  die additive Migration `CompleteSalesDomainModel` erweitert sie um das
  vollständige fachliche Zielmodell.
- Ein produktiver Vollimport startet erst nach Bestätigung der dort genannten
  Zoho-Mappings, Pipelines, Rollen, Arbeitszeit-/Geschäftsjahres- und
  Aufbewahrungsentscheidungen.

## Implementierungsstatus

| Bereich | Status |
|---|---|
| React-Frontend-Grundgerüst | vorhanden |
| Geschützter HelloWorld-Endpunkt | vorhanden |
| Tenant-Datenbank-Grundgerüst | vorhanden |
| App-Manifest und `sales-user`-/`sales-manager`-Rollen | vorhanden |
| Native Windows-Rebuilds | vorhanden |
| Pflichtenheft und KI-Kontext | vorhanden |
| Zoho-Authentifizierung, Adapter und vollständiger fachlicher Hintergrundimport | vorhanden |
| Eigene Sales-Domänenmodelle und Regelengine | EF-Datenmodell vorhanden; Arbeitslisten-Projektion und Regelbewertung für R-01 bis R-18 umgesetzt |
| Vollständiges Zieldatenmodell vor dem Vollimport | EF-Entitäten und additive Migration umgesetzt; Zoho-Mappings für den fachlichen Initialimport umgesetzt |
| Start-Arbeitsliste | umgesetzt; Vorgangsketten, `AvailableFrom`, CRM-Absprung und CRM-geführte Auflösung umgesetzt |
| Reportseite und Seiteneditor | umgesetzt; Standardbaum, direkte Bearbeitung mit Grid/Tabs/Akkordeon/Überschrift/Text und tenantbezogene Speicherung vorhanden |
| Fachansichten und KPI-Cockpit | read-only Report-Projektion für Cockpit, Team, Meetings, Analyse, Kunden, Ziele/Pace, Aufräumen, Servicefälle und kommerzielle Kette umgesetzt |
| Historische KPI-Snapshots | werden unmittelbar in jedem Full- und Incremental-Sync für Pipeline, KPIs, Aktivitäten und Kundenstatus aktualisiert; Vergleichsvisualisierungen werden weiter ausgebaut |
| Fachliche Rollenmatrix | `sales-user`, `sales-manager`, `sales-management` und `sales-backoffice` mit serverseitiger Report-Freigabe angelegt |

Jede neue Implementierung soll diese Tabelle und die betroffenen Infodateien
mitpflegen. Ein Eintrag „vorhanden“ im Startgerüst bedeutet nicht, dass der
gesamte fachliche Zielumfang umgesetzt ist.
