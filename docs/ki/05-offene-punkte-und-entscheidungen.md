# Offene Punkte, Entscheidungen und Status

## Im Pflichtenheft ausdrücklich offen

1. **Mailbox vs. Gespräch:** Liefert das Telefonsystem einen
   Verbindungsstatus? Falls nicht, muss ein Gespräch in der Protokollierung
   korrigierbar sein.
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
- Ein erfolgreicher Anruf beginnt bei 20 Sekunden Gesprächsdauer, vorbehaltlich
  der Mailbox-Klärung.
- Es gibt einen Staffelungszähler seit dem letzten Gespräch und einen
  kumulativen Auswertungszähler.
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
  Tokenlogik.
- Pipedrive und weitere CRM-Systeme werden später über eigene Adapter an dasselbe
  kanonische Modell angeschlossen.
- Anbieter-IDs, Rohdaten, OAuth-Verbindungen und Sync-Zustände bleiben im
  Integrationsbereich und werden nicht in fachliche Regeln geleakt.

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
  [`07-ziel-datenmodell.md`](./07-ziel-datenmodell.md) geplant.
- Die bestehenden `Sales*`-Tabellen und die
  `AddCrmIntegrationFoundation`-Migration sind nur die technische Grundlage;
  sie sind noch nicht das vollständige fachliche Zielmodell.
- Ein produktiver Vollimport startet erst nach Bestätigung der dort genannten
  Zoho-Mappings, Pipelines, Rollen, Arbeitszeit-/Geschäftsjahres- und
  Aufbewahrungsentscheidungen.

## Implementierungsstatus

| Bereich | Status |
|---|---|
| React-Frontend-Grundgerüst | vorhanden |
| Geschützter HelloWorld-Endpunkt | vorhanden |
| Tenant-Datenbank-Grundgerüst | vorhanden |
| App-Manifest und `sales-user`-Rolle | vorhanden |
| Native Windows-Rebuilds | vorhanden |
| Pflichtenheft und KI-Kontext | vorhanden |
| Zoho-Authentifizierung, Adapter und erster Import | vorhanden |
| Eigene Sales-Domänenmodelle und Regelengine | Datenmodell-Grundlage vorhanden; Regelengine offen |
| Vollständiges Zieldatenmodell vor dem Vollimport | geplant in `07-ziel-datenmodell.md`; EF-Entitäten/Migration offen |
| Fachansichten und KPI-Cockpit | offen / nicht implementiert |
| Fachliche Rollenmatrix | offen |

Jede neue Implementierung soll diese Tabelle und die betroffenen Infodateien
mitpflegen. Ein Eintrag „vorhanden“ im Startgerüst bedeutet nicht, dass der
gesamte fachliche Zielumfang umgesetzt ist.
