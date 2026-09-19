# Offene Punkte, Entscheidungen und Status

## Tagesdiagramm für den CRM-Verbrauch (19.09.2026)

Benutzerauftrag: Verbrauch tagesweise als Chart anzeigen. Im bestehenden
Sales-Kontext sind damit die erfassten CRM-Verbrauchseinheiten gemeint (Zoho:
geschätzte Credits), nicht KI- oder OAuth-Tokens. Unter API-Verbrauch kommt ein
eigener Tagesverlauf mit 7/30/90 Kalendertagen inklusive heute und einer
zugänglichen Tageswerttabelle hinzu. Tagesgrenzen sind ausdrücklich UTC, der
heutige Tag ist unvollständig. Anbieter/Verbindungen/Einheiten werden getrennt
ausgewählt, niemals zu einer uneinheitlichen Verbrauchssumme addiert.
Gruppierung erfolgt serverseitig in der tenantisolierten Usage-Tabelle; keine
Zoho-Aufrufe, keine Migration oder Paketänderung. Tage ohne gespeicherte Events
erscheinen mit 0 (kein Beleg für durchgängige Messung). Zugriff nur Tenant-Admins.

Lokal umgesetzt: `/api/integrations/usage/daily`, serverseitige UTC-`DateOnly`-
Gruppierung, Lückenfüllung, Auswahl von Reihe/Kennzahl sowie klickbare Tageswerte
und Tabelle im Frontend. 23 synthetische Prüfungen einschließlich der echten
Npgsql-SQL-Übersetzung und Tenantfilter bestanden. Der Chrome-Test prüft 7/30/90
Tage, Einheitenwechsel, Balkenklick, Tabelle, Fehler/Refresh und 390px-Layout.
Keine produktiven Daten gelesen, kein Live-Rollout oder Commit/Push.

## Korrektur: Hook-URL ist ein Mandanten-Setting (19.09.2026)

Ausdrückliche Benutzerentscheidung: Jeder Kunde muss seine Hook-Basis-URL in
den Sales-AppSettings pflegen können. `zoho.webhookUrl` wird als normales
`tenantApp`-Setting unter „Zoho CRM“ registriert. Job und Übersicht verwenden
denselben tenantbezogenen Leser; kein OAuth-Neuverbinden oder Neustart zum
Übernehmen einer Änderung. Nur ein leerer/nicht gesetzter Wert nutzt den
vorhandenen Deploymentwert als abwärtskompatiblen Standard. Ein ungültiger
expliziter Wert wird angezeigt/abgewiesen, nicht still durch den Standard ersetzt.
Tenant-ID und Token werden weiterhin erst bei der Registrierung ergänzt.
Eine geänderte effektive URL erzwingt beim nächsten Hook-Job die Registrierung.
Die URL muss auf einen erreichbaren, für diesen Mandanten eingerichteten
Sales-Einstieg führen; das Setting provisioniert keine Domain oder Zertifikate.

Umgesetzt und nativ geprüft: 87 synthetische Hook-/Settings-Prüfungen (zwei
Mandanten mit verschiedenen Frontend-Hosts, isolierter Scope, sofortiges Lesen
geänderter Werte, ungültige Werte ohne Fallback), 18 Vertragstests, Backend-
Releasebuild ohne Warnungen/Fehler und Frontend-Build bestanden. Chrome prüft
auch Mandantenquelle/URL in der Übersicht. Bekannte Vite-Chunkgrößenwarnung.
Kein Commit/Push oder Rollout; keine produktiven Tenant-Settings geändert.

## Hook-Übersicht und sichere Ereignisprotokolle (19.09.2026)

Benutzerauftrag: Callback-URL und Integration erklären sowie empfangene Hooks
nachvollziehbar machen. Unter CRM-Integration entsteht eine Tenant-Admin-Ansicht
mit konfigurierter Basis-URL, Modul-Subscriptions (auch fehlenden), Ablaufzeiten
und paginierter Ereignisliste mit Status, Versuchen und sicheren Fehlerhinweisen.
Die Übersicht liest ausschließlich die Tenant-Datenbank, nicht die Zoho-API.
Eingang und Dubletten werden technisch protokolliert; Verarbeitung erhält die
Ereignis-ID als Korrelation in den bestehenden Plattform-Joblogs.

„Importiert“ bedeutet kanonische Daten übernommen, nicht erfolgreiche gesamte
Regel-/Task-/Benachrichtigungsnachverarbeitung. Diese bleibt im Joblauf sichtbar.
Der Zeitplan wird nicht geändert: Callback speichert, `CRM-Hooks erneuern`
verarbeitet maximal 100 Ereignisse je Lauf, Fehler maximal fünf Versuche.
Abgewiesene Aufrufe sind keine verifizierten Ereignisse und erscheinen nur im
technischen Log. Keine Rohpayloads, Tokens oder beliebigen Provider-Fehlertexte
in der neuen API. Neue Queue-Payloads speichern nur benötigte Felder ohne Token;
alte Payloads bleiben lesbar, die bestehende Deduplizierungs-ID bleibt stabil.
Keine Datenbankmigration, kein Paketupdate, keine Live-CRM-Schreibtests.

Lokal umgesetzt und nativ geprüft: Backend-Releasebuild ohne Warnungen/Fehler,
Frontend-Typecheck/Build (bekannte Chunkgrößenwarnung), 71 synthetische Hook-
Prüfungen und 16 Deployment-/Theme-/Navigations-/URL-Verträge bestanden. Der
Chrome-Test prüft zusätzlich Status/Details, Modul- und Statusfilter, Pagination,
Leerzustand, HTTP-Fehler, erneutes Laden sowie 390px-Layout mit offener Hook-
Modulübersicht. Kein Serverrollout, kein produktiver Callback-Nachweis und kein
Commit/Push. Bereits gespeicherte ältere Payloads werden nicht bereinigt.

## Gemeinsames helles und dunkles Theme (19.09.2026)

Benutzerauftrag: zentrale Themes im Common-React-Paket. Sales verwendet dessen
semantische Farbvariablen; kein eigener Theme-Store oder zweiter Umschalter.
Der gemeinsame Header bietet Hell, Dunkel und System; Browserauswahl bleibt
je Origin erhalten. Reports, Arbeitsliste, Formulare und Dialoge wechseln mit.
Fachliche Daten, Berechtigungen und Navigation bleiben unverändert. Externe
Kartenkacheln sind kein Teil des UI-Themes. Veröffentlichung und Live-Rollout
sind getrennt vom lokalen Implementierungs-/Prüfstand nachzuweisen.

Lokal umgesetzt: Common React 0.1.59 enthält Themes und Umschalter; Sales-
Stylesheets verwenden dessen Variablen. Native Prüfungen: Common-Build/138 Tests,
Sales-Typecheck/Build, neun Navigations-/URL-Tests und Chrome-Smoke-Test mit
echtem Common-Header, Theme-Persistenz, Report-Dialog/Tabelle und 390px-Layout.
Auf Folgeauftrag Common 0.1.59 lokal frisch gebaut und in GitHub Packages
veröffentlicht; Sales-Paketreferenz und Registry-Lockfile sind aktualisiert.
`initializePlatformTheme()` läuft vor dem ersten React-Render. Zwölf Integrations-/
Navigations-/URL-Tests sowie Typecheck/Produktionsbuild bestanden.
`tests/ThemeBrowser.mjs` besteht jetzt mit dem tatsächlich installierten Registry-
Paket ohne lokalen Alias; Dark/Light/System, Reload-Persistenz und 390px-Layout
sind geprüft. Kein Serverrollout und kein Commit/Push dieses Arbeitsstands bei
dieser Integration. Kein NuGet-Update. Aufmass bleibt separat zu integrieren.

## Öffentlicher Zoho-Webhook (19.09.2026)

Benutzerauftrag: URL automatisch im Deployment konfigurieren und die sichere
öffentliche Durchleitung ergänzen. `Zoho__WebhookUrl` wird in den Remote-
Deploymentprofilen aus dem öffentlichen Frontend-Einstieg und dem app-eigenen
Webhook-Pfad abgeleitet. Local bleibt ohne öffentlich erreichbaren Override
absichtlich deaktiviert. Sales registriert genau diesen POST-Empfänger im
generischen Manifestvertrag `webhooks`; Plattform-API und Application Router
müssen diesen Vertrag vor dem Sales-Rollout unterstützen. Die Tenant-ID ist
Routingkontext, kein Tokenersatz. Sales prüft weiterhin Channel, Modul und
Subscription-Token innerhalb des Tenants, einschließlich Ablaufdatum.

Umgesetzt und nativ getestet: 37 synthetische URL-/Tenant-/Token-/Erneuerungs-
Prüfungen, vier Deployment-Vertragstests, bestehender HTTPS-Vertrag und Sales-
Releasebuild ohne Warnungen/Fehler. Echte Remote-Profile beider Ziele gerendert.
Noch nicht ausgerollt. Nach Plattform-API/Router und Sales den Job `CRM-Hooks
erneuern` einmal starten; er registriert die URL samt Tenant und Token selbst.
Keine NuGet-/npm-Veröffentlichung erforderlich.

## Gesprächsentscheidung: Arbeit und Steuerung (19.09.2026)

Der aktuelle Benutzerauftrag ersetzt die frühere gemeinsame lange Reportseite:
Oben stehen Arbeit und daneben Steuerung. Arbeit erhält links die vier Themen
Auslaufende Produkte, Schlummernde Leads, Wiedervorlagen und Meeting Report;
vorhandene Regelarten werden darunter eingeordnet, nicht gelöscht oder mit neuen
Schwellwerten neu erfunden. Steuerung erhält ebenfalls eine linke Reportnavigation
(Cockpit, Monat, Team, Jahr, Lifetime, Analyse, Kunden, Ziele, Aufräumen und
bestehende Fachreports). Meeting Report gehört zur Arbeit. Numerische
Prioritätspunkte verschwinden aus der Oberfläche, die Priorisierung bleibt.

Alle Sales-Vertriebsrollen dürfen die Steuerung lesen; die frühere Beschränkung
von Cockpit/Analyse auf Leitung/GF ist damit aufgehoben. Persönliche
Arbeitslistenfilter, Tenant-Isolation, Layout-Schreibschutz und Berechtigungen
für Bereinigungsaktionen bleiben unverändert. Kennzahlen und Diagrammbalken
öffnen ein zugängliches Tabellen-Modal mit exakt ihrer Berechnungsgrundlage.
Quelle, Zeitraum, Formel, Datenstand und fehlende Voraussetzungen sind sichtbar;
fehlende Ziele/Nenner sind nicht 0. Details und Kennzahlen stammen aus derselben
Antwort; Tabellen werden paginiert, nicht still abgeschnitten. Vorhandene
gespeicherte Layouts bleiben erhalten. Änderungen erfolgen ausschließlich in
Sales, ohne CRM-Schreibzugriffe oder Änderungen an Aufmass/Identity Platform.

Umgesetzt in Sales-Frontend und -Backend. Das Backend liefert normalisierte
Nachweiszeilen einmal pro Antwort und referenziert sie aus mehreren Kennzahlen.
Die Arbeitsliste hat keine stille 250er-API-/100er-UI-Abschneidung mehr; die
UI blättert in 25er-Seiten. Fremde/unbekannte Regelarten bleiben unter Weitere
bzw. Alle erreichbar. Beim Tenant-/Benutzerwechsel wird der Seitenzustand
zurückgesetzt; veraltete Ladeantworten werden verworfen.

Validierung nativ unter Windows: Backend-/Frontend-Build, synthetische
Reportprojektion (`tests/Reports`), Navigations-/SSR-Tests und bestehende
Root-URL-Tests. Ein isolierter Chrome-Smoke-Test (`tests/ReportBrowser.mjs`)
prüft beide Navigationsbereiche, Themenfilter, Tabellen-Pagination, Modal-Suche,
KPI-/Diagrammklicks, Escape/Fokusrückgabe sowie 390px-Handylayout. Keine
produktive Anmeldung und keine Live-CRM-Schreibzugriffe. Für den Rollout müssen
Sales-Frontend und Sales-Backend gemeinsam aktualisiert werden; Platform-only
oder ein npm-/NuGet-Update ist hierfür nicht erforderlich.

## Zoho-Aufgaben: Lead-Verknüpfung (19.09.2026)

Der Fehler `INVALID_DATA` auf `$.data[0].Who_Id.id` entsteht bei der
Aufgabenspiegelung durch das falsche Lookup-Feld für Leads. Der Task-Payload
muss die externe Lead-ID als `What_Id.id` zusammen mit `$se_module = Leads`
übergeben. `Who_Id` ist bei Tasks für Kontakte vorgesehen; Kontakte bleiben
außerhalb der Sales-Integration. Quelle: [Zoho Tasks API](https://help.zoho.com/portal/en/community/topic/kaizen-36-tasks-api).
Die Korrektur gilt für Create und Update. Andere Zielmodule, Owner-Zuordnung,
Remote-ID-Normalisierung und das Beibehalten von Status/Priorität bei Updates
bleiben unverändert. Keine Scope- oder OAuth-Änderung für diesen Fehler.

Umgesetzt in `ZohoCrmAdapter.BuildTaskPayload`. Der ausführbare Regressionstest
`tests/ZohoTaskPayload` reproduzierte vor der Korrektur das falsche Lead-Lookup.
Nach der Korrektur bestehen 34 Payload-Fälle für Create/Update, sieben Zielmodule,
rohe/präfixierte IDs sowie fehlende/unsupported Ziele. Natives Windows-Releasebuild
ohne Warnungen/Fehler und Scope-/Deployment-Verträge bestanden. Der Test läuft
auch in CI; er sendet keine CRM-Anfragen. Nicht ausgerollt, keine Live-Aufgaben
angelegt. Fehler bei Create erzeugen keinen erfolgreichen CRM-Link; weiterhin
aktive Vorgänge ohne Link werden beim nächsten Aufgabenabgleich erneut versucht.

## Zoho-Scopes für Deal-Historie (19.09.2026)

Die zunächst anhand eines älteren Field-Trackers-Artikels ergänzte Berechtigung
`ZohoCRM.modules.DealHistory.READ` ist zurückgenommen. Beim erneuten Verbinden
meldete Zoho „Ungültiger Authentifizierungsumfang / Umfang nicht vorhanden“.
Der ergänzte Scope fehlt in der aktuellen v8-Scope-Liste; der Screenshot benennt
den einzelnen abgelehnten Scope nicht. Defaults und effektive OAuth-Anforderung
entfernen die Ergänzung, auch aus alten `Zoho__Scopes`-/`ZOHO_SCOPES`-Overrides.
Auf ausdrücklichen Auftrag werden jetzt die für die Integration vorgesehenen
Rechte vollständig angefordert: `ZohoCRM.modules.READ` für modulübergreifendes
Lesen, zusätzlich `modules.emails.READ`, `users.READ`, `org.READ` sowie die
vorhandenen Settings-Leserechte für Module, Felder, Layouts, Pipelines und
Related Lists. Schreibrechte bleiben auf Tasks CREATE/UPDATE und Notifications
CREATE/DELETE beschränkt. Kein `modules.ALL`, keine neuen CRM-Löschrechte.
Dies erweitert bewusst die bisherigen modulspezifischen Leserechte; welche
Entitäten Sales synchronisiert, bleibt unverändert. Die gesamte erforderliche
Scope-Menge wird auch bei älteren Overrides ergänzt, nicht nur ein einzelner
History-Scope. Die Zustimmung bleibt interaktiv beim jeweiligen Zoho-Benutzer.
Leere bzw. ausschließlich aus dem verworfenen Scope bestehende Optionswerte
bleiben ungültig. Quellen: [Zoho v8 Scopes](https://www.zoho.com/crm/developer/docs/api/v8/scopes.html)
und [Related Records](https://www.zoho.com/crm/developer/docs/api/v8/get-related-records.html).

Nach Deployment muss jeder betroffene Mandant Zoho erneut verbinden. Vorhandene
Refresh-Tokens werden nicht automatisch erweitert. Die Fehlerflut im Import ist
eine separate offene Fehlerbehandlung und nicht Bestandteil dieser Scope-Korrektur.

Umgesetzt in Options-Defaults, `appsettings.json`, effektiver OAuth-Scope-Auflösung
und Deployment-Override. Ein einzelner `ZOHO_SCOPES`-Eintrag wird jetzt als Liste
behandelt, statt mit weiteren Scopes ohne Komma verkettet zu werden.
Nativ unter Windows bestanden: neue Scope-Vertragstests (auch in CI eingebunden),
App-Pipeline-/HTTPS-Verträge und Backend-Releasebuild ohne Warnungen/Fehler.
Die bisherigen Tests prüfen Konfigurationsverarbeitung, nicht die Anerkennung
von Scope-Namen durch Zoho. Kein Deployment und kein authentifizierter
Zoho-Livetest; erfolgreiche Zustimmung und Stage-History-Abruf bleiben offen.
Die Rücknahme repariert die von uns geänderte OAuth-Anforderung, ist aber kein
nachgewiesener Fix für das ursprüngliche `OAUTH_SCOPE_MISMATCH` beim Import.

## Correlation-ID für technische Diagnose (14.09.2026)

Browser-Requests erhalten zentral im Shared-Frontendpaket einen W3C-Tracekontext.
Report-/Arbeitslistenaktionen binden zusätzliche Meldungen an denselben Vorgang
über `createPlatformLogOperation`; keine zweite Logging-Infrastruktur und keine
CRM-Daten in Logpayloads. Die .NET-Library trägt denselben Trace durch Backend-
und Serviceaufrufe. npm 0.1.54 / NuGet 0.1.69 sind Quellversionen, Veröffentlichung
und reguläre Paketumstellung bleiben separate Schritte.

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
- Eine eigene Fachfunktion für Kontakte, Ansprechpartnerrollen sowie Konzern-,
  Tochter- und sonstige Kundenbeziehungen wird nicht umgesetzt. Es gibt dafür
  keine kanonische Entität, keine CRM-Synchronisation und keine UI-Funktion.

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
- Der gemeinsame Job `crm-subscription-maintenance` / `CRM-Hooks erneuern`
  hat einen konfigurierbaren Zeitplan mit Standard täglich 03:00 Uhr in
  `Europe/Berlin`. Manueller Start bleibt möglich. Nur diese Jobregistrierung
  wird auf den täglichen Standard umgestellt; die übrigen Jobs bleiben
  unverändert. Hook-Erneuerung und wartende Callback-Verarbeitung erfolgen
  weiterhin zusammen in diesem einen Job.
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
  Kunden oder Deal wird der betroffene Vorgang mit
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
