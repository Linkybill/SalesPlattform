# Sales: Deployment- und Betriebsstand

## Spezifikationsreports erfolgreich ausgerollt – 09.10.2026

Code-Revision 6a798ccf27502dd6e08370b6db2259ae2e1888be auf main gepusht.
[Sales-Release 37974521619](https://github.com/Linkybill/SalesPlattform/actions/runs/37974521619)
erfolgreich abgeschlossen am 09.10.2026 um 20:41:29 Uhr Europe/Berlin
(18:41:29 UTC). Validierung, gesamte CI, beide Images und Deployment erfolgreich.
Ziel ax42-1/dev; ausschließlich Sales, keine Plattform-/Datenbankaktualisierung.

Öffentlicher HTTPS-Nachweis mit Windows-Zertifikatsprüfung:
Einstieg https://176.9.57.203:3003 HTTP 200;
JavaScript /assets/index-Cm24ePCn.js HTTP 200 mit specified-reports,
lifetime und postalAreas; CSS /assets/index-C4odvy8e.css HTTP 200 mit
.specified-report. Die Report-Erweiterung wird damit tatsächlich ausgeliefert.

Nach Workflow-Abschluss antwortete der öffentliche Einstieg vorübergehend mit
HTTP 503 und „No running frontend component container is available.“ Nach dem
Runtime-Anlauf war die neue Version ohne weiteren Eingriff erreichbar.
Kein zusätzlicher Infrastruktur-Neustart und kein erneuter Release notwendig.

Kein CRM-Import oder produktiver Datenabgleich wurde ausgelöst. Datenparität zu
den ursprünglichen Analytics-Screenshots bleibt von dieser erfolgreichen
Veröffentlichung getrennt; die dokumentierten Datenvoraussetzungen gelten weiter.
Dieser Nachtrag ändert ausschließlich Dokumentation und benötigt keinen Rollout.


## Veröffentlichung beauftragt – 09.10.2026

Nach Abschluss der lokalen Implementierung lautet der neue Benutzerauftrag
ausdrücklich: push und deploy. Das vorherige Deployment-Verbot ist damit
aufgehoben. Der geprüfte Reportstand wird auf main veröffentlicht und über
release.yml ausschließlich für Sales auf ax42-1/dev ausgerollt. Der darunter
dokumentierte lokale Prüfstand bleibt als zeitlich vorheriger Nachweis erhalten.
Ein erfolgreicher Rollout wird erst nach dem tatsächlichen Release-Ergebnis
bestätigt; produktive Analytics-Parität ist davon unabhängig.

## Spezifikationsreports lokal umgesetzt und geprüft – 09.10.2026

**Nicht ausgerollt.** Jüngste Benutzeranweisung: kein Deployment starten.
Kein Release-Workflow, kein Push, keine produktive Datenänderung und kein
CRM-Import wurden für diese Erweiterung ausgelöst. Änderungen liegen lokal
im SalesPlattform-Arbeitsverzeichnis; die älteren Rolloutbelege darunter
beziehen sich ausschließlich auf die jeweils genannten Revisionen.

Umfang und Datenvoraussetzungen:
[Report-Umsetzungsstand](11-report-umsetzungsstand.md).
Abgleich der ursprünglichen Screenshots:
[Ausgangsbefund](10-report-screenshot-abgleich.md).

Native Windows-Prüfungen bestanden:

- Backend Release mit -warnaserror: 0 Warnungen, 0 Fehler.
- Reportnachweise: 373 Assertions.
- Kontaktregressionen: 25; bisherige Zusatzreports: 113.
- Importalias-Regressionsfälle: 20; neue Spezifikationsreports: 94.
- Gemeinsamer Zoho-Feldleser: Webhook 282 Checks, Task-Payload 34 Fälle.
- TypeScript und Vite-Produktionsbuild; Vite meldet den Bundle-Größenhinweis
  für das gemeinsame Frontend-Bundle, keine Buildfehler.
- 13 Darstellungs-/Navigations-/URL-Tests, darunter Geld-/Währungs-/
  Negativwertgrenzen für Zusatzdiagramme.
- Native Chrome-Abnahme mit synthetischen API-Antworten: sechs Kuchen,
  individuelle Nachweise, Kontakt-/Anruflisten, jährliche Flächen und
  Prozentumschaltung, Zielzeitmarke, Matrix-Suche/Seitenwechsel,
  Kundenfilter/Kartenbündelung und PLZ-Polygonkarte, mobile 390px-Ansicht,
  Navigation/Reload/Zurück/Vorwärts, Fokus und Escape.
- Browserprüfung fand einen verzögerten Kartenaufruf nach dem Unmount.
  Timer wird nun abgeräumt; Resize-Aufrufe prüfen die noch aktive Karte.

Keine Gleichheit der produktiven Analytics-Zahlen behauptet. Fehlende
Historien, Typ-/Produktzuordnung, Kalender, Standorte oder Gebietsgrenzen
sind weiterhin echte Datenvoraussetzungen und werden in den Reports erklärt.


## Kuchendiagramme ausgerollt und öffentliche Dateien bestätigt – 09.10.2026

Code-Revision 0bc837364b8603b323394e2865aa643f47378e4f auf main gepusht.
[Release 37930929711](https://github.com/Linkybill/SalesPlattform/actions/runs/37930929711)
erfolgreich abgeschlossen um 12:39:00 UTC; einschließlich CI mit den neun
UI-/Navigationsprüfungen und App-Deployment. Arbeitsstand vor dieser reinen
Nachweisdokumentation sauber.

Direkt nach dem Bootstrap-Rollout lieferte der öffentliche Einstieg zunächst
HTTP 503 mit „No running frontend component container is available.“
Die zentrale Runtime läuft unabhängig vom Bootstrap; ohne Infrastrukturänderung
wurde das Frontend in der Folgeprüfung erreichbar. Der genaue technische
Auslöser der vorübergehend fehlenden Route wurde nicht separat nachgewiesen.

Öffentliche HTTPS-Prüfung mit Windows-Zertifikatsprüfung: Einstieg HTTP 200,
JavaScript /assets/index-SVG7Ynsu.js HTTP 200 mit Kuchendiagramm-Code sowie
CSS /assets/index-DQezKPwX.css HTTP 200 mit .evidence-pie-chart. Damit ist die
neue Darstellung tatsächlich ausgeliefert. Keine produktiven CRM-Daten,
Tokens oder angemeldeten Reportantworten für diese Prüfung ausgelesen.
Die fachliche Abnahme mit synthetischen Daten steht im folgenden Prüfstand.
Keine zusätzlichen Runtime-/Plattform-Neustarts oder CRM-Änderungen.

## Kuchendiagramme und sichtbarer Report-Einstieg – 09.10.2026

Benutzerkorrektur: Reports waren ergänzt, gewünschte Kuchendiagramme fehlten;
der Report-Einstieg zeigte außerdem nur das Cockpit. Sechs Verteilungen sind
jetzt interaktive SVG-Kuchen mit Werten/Prozenten und denselben Nachweisen.
Steuerung startet im Jahresreport, Cockpit enthält „Diagramme anzeigen“.
Keine Backend-/Import-/Paket-/Schemaänderung; kein erneuter Import für Kuchen.

TypeScript, neun UI-/Navigationsprüfungen und vollständige native Chrome-Abnahme
bestanden: sechs Kuchen, passende Detailzeilen bei Segment-/Legendenklicks,
Tastatur/Fokus, Standard-Einstieg und 390px-Ansicht. Desktop-Vorschau visuell
geprüft. Vite-Produktionsbuild bestanden; bekannte Chunkgrößenwarnung bleibt.
Commit/Push und Veröffentlichung folgen über den Sales-Release.

Vorheriger Report-Release
[37925361464](https://github.com/Linkybill/SalesPlattform/actions/runs/37925361464)
für 42bcd1ac8f43bf0582188e04b437dbe955112db4 erfolgreich abgeschlossen.
Dieser veröffentlichte Stand enthält die Reportdaten, noch keine Kuchendarstellung.

## Zusätzliche Reports implementiert und geprüft – 09.10.2026

Benutzerauftrag: alle im Screenshot-Abgleich fehlenden Reports ergänzen.
Umfang/Definitionen stehen in Projektkontext, Regelwerk und Entscheidungen.
Native Windows-Releasebuilds ohne Warnungen/Fehler; 185 Report-Assertions,
25 Kontaktregressionen und 113 neue Reportprüfungen bestanden. TypeScript,
Vite-Produktionsbuild, sechs Navigationsprüfungen und vollständiger Chrome-Test
einschließlich neuer Diagramme/Detaildaten, Terminvorbereitung, getrennter
Statuslisten und 390px-Ansicht bestanden. Desktop-Vorschau visuell geprüft.
Die bestehende Vite-Chunkgrößenwarnung bleibt. Bestehende Report-CI führt die
neuen Backendprüfungen automatisch aus.

Keine Migration, keine Paketaktualisierung, keine CRM-Schreibvorgänge.
Reports verwenden bereits synchronisierte Daten; fehlende individuelle
Terminarten können in den Tenant-AppSettings zugeordnet werden. Der
Zoho-Mapper fordert zusätzlich Appointment_Type an, sofern das Feld im
Schema existiert; bisher nicht geladene Feldwerte benötigen einen Import.
Produktive Übereinstimmung mit separaten Zoho-Analytics-Formeln ist nicht
nachgewiesen. Commit/Push und Release dieses Folgestands werden separat
über die genaue Quellrevision dokumentiert.

Vorheriger Rollout: b3415765003ba50336ae1f3473ae052bbb4744d8 über
[37920346947](https://github.com/Linkybill/SalesPlattform/actions/runs/37920346947)
erfolgreich abgeschlossen am 09.10.2026, 10:58:59 UTC. Enthält die
Kontakt-/R-07-Korrektur und die zuvor offenen Diagnosen, noch nicht diese
zusätzlichen Reports. Historische Kontaktmarker benötigen weiterhin einen
vollständig erfolgreichen Full-Import.

## Veröffentlichung des offenen Sales-Stands beauftragt – 09.10.2026

Benutzer beauftragt Commit/Push aller offenen Sales-Änderungen und den
GitHub-Release auf ax42-1/dev. Der Stand umfasst Kontaktzählung/R-07-Reparatur,
Shared 0.1.77, die bereits vorbereitete Organisationsdiagnose, vollständige
Tenant-Callback-Anzeige sowie zugehörige Tests und Betriebsdokumentation.
Zusätzlich zur Backend-Abnahme: native Windows-TypeScript-Prüfung,
Vite-Produktionsbuild und Theme-/Hook-/Usage-Browsertest bestanden; bekannte
Chunkgrößenwarnung bleibt. Der Release-Workflow prüft dieselbe Revision erneut
vor dem Rollout. Deploymenterfolg und Live-Erreichbarkeit separat nachweisen.
Nach erfolgreichem Backend-Rollout ist ein vollständiger fehlerfreier CRM-Import
für die historische Kontaktmarkerbereinigung nötig.

## Schlummernde Leads: Task-Kontaktfehler korrigiert – 09.10.2026

Benutzerscreenshot bestätigt TEAM-ARBEITSLISTE mit nur einem R-07-Treffer und
fälschlicher Fälligkeit im Januar 2027. Fehlende Sales-Manager-Rolle bzw.
persönlicher Besitzerfilter sind damit für diese Ansicht ausgeschlossen.
Codebefund: Nicht-Anruf-Aktivitäten, darunter eigene CRM-Tasks, schrieben
LastContactAt fort. Aufgaben konnten dadurch Reaktivierungen unterdrücken.
Zusätzlich wurde generisches Zoho Last_Activity_Time als Lead-Kontakt übernommen.

Lokal korrigiert: Tasks zählen nicht als Kontakt, echte E-Mails/qualifizierte
Gespräche bleiben maßgeblich; explizites Last_Contact bleibt erhalten. Ohne
Kontakt ist R-07 sofort fällig. Ein vollständig erfolgreicher Full-Import mit
Accounts, Leads, Calls und Emails rekonstruiert historische Kontaktmarker vor
der bestehenden Vollbewertung. Teilimporte und Importfehler blockieren die
Rekonstruktion. Kein Schemawechsel und kein neuer CRM-Schreibweg.

Native Windows-Abnahme: Releasebuild mit Warnungen als Fehlern ohne Warnungen/
Fehler, 153 bestehende Report-Assertions, 25 neue Kontakt-/Reaktivierungsprüfungen
und 282 bestehende Webhook-Prüfungen bestanden. Neue Fälle nutzen synthetische
EF-InMemory-Tenantdaten, die echte Repository-/R-07-Logik und PostgreSQL-
SQL-Übersetzung. Sie prüfen Tasks, Zukunftsdaten, gelöschte E-Mails,
Gesprächsqualifikation, historische Bereinigung, wiederkehrende Lead-Imports,
Tenant-Isolation, Fälligkeit und Schutz vor unvollständigen Reparaturen.
Die bestehende Report-CI führt diese neuen Prüfungen ebenfalls aus.

Prüfstand vor Veröffentlichung: kein Commit/Push/Deployment dieser Korrektur und kein Zugriff auf
produktive CRM-Antwortinhalte. Nach Backend-Rollout ist ein erfolgreicher
vollständiger CRM-Import erforderlich, anschließend die Arbeitsliste neu
bewerten. Tatsächliche Vollständigkeit gegenüber der Zoho-Analytics-Auswertung
ist noch nicht live nachgewiesen; fehlende Branchen-Diagramme sind ein
separater Implementierungsumfang. Bereits vorhandene lokale Änderungen aus
anderen Arbeiten wurden erhalten.

## HTTP 503 nach erfolgreichem Sales-Rollout – 08.10.2026

Release [37765883834](https://github.com/Linkybill/SalesPlattform/actions/runs/37765883834)
und CI [37765868077](https://github.com/Linkybill/SalesPlattform/actions/runs/37765868077)
für `1a332847e056affa85ca78da10e0b3dd3a3bc144` erfolgreich. Laufende Sales-Images
entsprechen den Release-Digests; das Frontend liefert die neue Reiternavigation.
Diese Nachweise allein bestätigen keine funktionsfähige Arbeitsliste.

Benutzer meldete danach HTTP 503. Auch das öffentliche Backend-Manifest gab
503 ohne Body zurück. Der RuntimeDrainReporter erhielt keine Bestätigung;
die vorgesehene Identitätsprüfung sperrte deshalb Backend-Anfragen, obwohl
die Container-Healthchecks erfolgreich waren. Live-Befund im Broker:
`identity-runtime-activity-v1.in.telemetry` hatte keinen Consumer und wartende
Nachrichten; die zugehörige Plattform-API-Verbindung hatte keinen Kanal mehr.
Der ursprüngliche Auslöser des Kanalverlusts ist nicht nachgewiesen.

Um 14:59 UTC ausschließlich die Plattform-API auf ax42-1 kontrolliert neu
gestartet, mit unverändertem Image-Digest
`89d97efac7d1459967f5f6c522c8bbafde0b0dd32e847d86ddf501e0d7600bac`.
Danach wieder aktiver Telemetrie-Consumer, abgearbeitete Warteschlange und
keine neuen Bestätigungsfehler im beobachteten Sales-Backend. Manifest über
öffentlichen HTTPS-Einstieg mit Windows-Zertifikatsprüfung: HTTP 200.
Arbeitsliste ohne Anmeldung: HTTP 401, wie vorgesehen. Bestehende angemeldete
Browser-Anfragen an `/api/worklist` um 17:00:47 und 17:01:47 CEST lieferten
HTTP 200. Dafür wurden nur technische Statuslogs geprüft; keine Tokens oder
CRM-Antwortinhalte ausgelesen. Eine vollständige fachliche Abnahme aller
Dashboard-Funktionen ist damit nicht verbunden.
Keine Sales-Geschäftslogik, Images, Berechtigungen oder Daten geändert.

## Sales-CI-Zugang eingerichtet – 08.10.2026

Der Release-Lauf `37618824152` vom 07.10. scheiterte vor dem Rollout an
fehlendem `APP_CI_KUBECONFIG`; der erfolgreiche Build war kein Deployment.
Auf Benutzerauftrag wurde auf dem bestehenden ax42-1 ein eingeschränkter
Sales-CI-Zugang eingerichtet und per Einzelassistent geprüft/veröffentlicht.
Das Environment `sales-plattform-ax42-1-dev` enthält jetzt drei `APP_CI_*`-
Secrets für SSH, Hostkey und Kubeconfig sowie sieben Zielvariablen. Dieser
Zugang verwendet den unterstützten direkten SSH-Tunnel; kein VPN-Secret.

SSH, TLS, eigene CI-Identität sowie Cluster-, Namespace- und Datenbank-UID
lokal verifiziert. Der GitHub-Zugangstest
[37765596356](https://github.com/Linkybill/SalesPlattform/actions/runs/37765596356)
von `main` ist erfolgreich. Er prüft noch keinen Rollout oder Image-Pull.
Clientzertifikat gültig bis 06.01.2027, 10:39:12 UTC; rechtzeitig erneuern.
Keine Schlüssel oder Zertifikatinhalte in Git. Plattformzugänge unverändert.
App-Rollout und tatsächlich laufende Image-Revision separat nachweisen.

## Gleichzeitiger Netz-/Logmitschnitt – 07.10.2026

Gemeinsame Aufnahme 01:48:12–01:51:13 CEST abgeschlossen; Benutzer meldete
währenddessen eine gespeicherte Zoho-Änderung. Kontrollaufruf um 01:48:46
mit leerem JSON erreicht Backend (HTTP 400, Trace
`ef35345206ad8fea7cc62497e78f6abf`). Derselbe Aufruf ist auf der externen
Schnittstelle enp5s0 und in Traefik-, Router- und Backend-Logs sichtbar.
Pcap: 31 erfasste Pakete, 31 vom Filter empfangen, 0 Kernel-Drops; alle
Pakete gehören einer Verbindung des Kontrollaufrufs an. Kein weiterer
beobachteter TCP-Verkehr auf Port 3003 im Aufnahmefenster. Podstände und
Restartzähler davor/danach unverändert. Logstreams enthalten Einträge bis
01:51:10; Fehlerdateien leer. Beim gesteuerten Stop meldeten die Logsammler
Exit 1; tcpdump endete mit dem geplanten Timeout (124).

Der Testaufruf geht über den Internetanschluss des Benutzer-Arbeitsplatzes,
nicht aus Zohos Netz. Die Beobachtung ist auf den aufgezeichneten Host-Port
und Zeitraum begrenzt: kein Nachweis über tatsächlichen Zoho-Versand,
Verzögerungen, Abweichungen beim Ziel oder Filter vor der Serverschnittstelle.
Insbesondere erklärt sie nicht rückwirkend die Support-403 vom September.
Originale liegen im Operator-Home in `zoho-capture.QKZl6Cyp`; keine Rohlogs
oder Paketdaten in Git aufnehmen.

## Beobachtungsgrenzen und gemeinsame Aufzeichnung – 07.10.2026

Access-Logging inzwischen durch Operator aktiviert und im Live-Export bestätigt:
308 Zeilen von 01:27:30 bis 01:35:30 CEST, darin nur der eigene leere
Webhook-Test um 01:27:41 (2 Byte, OriginStatus/DownstreamStatus jeweils 400).
Keine weitere protokollierte Callback-Anfrage; daraus folgt kein Nachweis,
dass Zoho nichts gesendet hat. TLS 1.2 ohne SNI wurde mit erfolgreicher
Zertifikatsprüfung getestet; das ist kein Test aus Zohos Netzwerk/Client.

Lesendes, zeitbegrenztes Serverskript `zoho-capture-20261007.sh` im
Operator-Home vorbereitet (lokale Kopie in `backend/obj/zoho-diagnostics`).
Es erfasst 180 Sekunden TCP/3003 auf enp5s0 in beiden Richtungen, parallel
Traefik-/Router-/Sales-Backend-Logs sowie Podstände davor/danach. Fehler und
tcpdump-Verluststatistik bleiben in eigenen Dateien; vorzeitiger Abbruch wird
vermerkt. Kein Neustart oder Firewall-Eingriff. Kontrollaufruf muss während
derselben Aufnahme von außen erfolgen, bevor negative Beobachtungen bewertet
werden. Pakete vor dem Server (etwa Provider-Firewall) und Zohos tatsächlicher
Versand sind damit weiterhin nicht beobachtbar. ShellCheck und Bash-Syntax
geprüft; die gemeinsame Aufnahme ist noch nicht gestartet.

## Prüfung der vorgeschalteten Infrastruktur – 07.10.2026

Export des laufenden `identity-platform-traefik` (Traefik 3.7.12), seiner
Boundary-ConfigMap und der Ingress-Ressourcen geprüft: App-Einstieg `:8444`
mit Prefix `/` zum Application-Router, Middleware `public-boundary,registered-app`,
keine zusätzliche Auth-/IP-Sperre, TLS mindestens 1.2 und `sniStrict: false`.
Zugriffslogging ist im Export nicht eingeschaltet. Die zuvor geprüften
Hostregeln und der Hetzner-Firewall-Screenshot zeigen Freigaben für IPv4/3003;
dies ist kein rückwirkender Nachweis für den Support-Aufruf vom September.

Temporäre JSON-Patches für Access-Logging und dessen Rücknahme unter
`backend/obj/zoho-diagnostics` vorbereitet und ins Operator-Home kopiert.
Sie prüfen Containername, Image und vorhandene Argumentliste, bevor sie nur
Logging-Argumente ersetzen. JSON-Logs erlauben ausgewählte Verbindungs-/Statusfelder;
Header und Queryparameter werden verworfen, Bodys nicht protokolliert.
Aktivierung benötigt interaktives sudo, ist noch nicht erfolgt und startet den
gemeinsamen Proxy wegen Recreate kurz neu. Ein späterer Helm-Rollout überschreibt
die temporäre Änderung. Kein behobener Zustellfehler oder echter Zoho-Eingang
nachgewiesen; TLS-Abbrüche vor HTTP erzeugen auch mit Access-Logging keinen
HTTP-Zugriffseintrag.

## Organisationsdiagnose – 06.10.2026

Der explizite Zoho-Verbindungstest liest jetzt die Organisations-ID über `/crm/v8/org`
und zeigt sie zusammen mit der API-Domain. Abruffehler liefern einen sicheren
Hinweis, keine bestätigte Organisation und keine Provider-Rohantwort.
Native Windows-Abnahme: Backend-Releasebuild mit Warnungen als Fehlern und
Frontend-TypeScript-/Vite-Build bestanden (bekannte Chunkgrößenwarnung).
Lokal vorbereitet, noch nicht deployt; keine Live-Organisationsabfrage und kein
Nachweis einer behobenen Callback-Zustellung. Beide Sales-Komponenten benötigen
einen Rollout. Interaktives Remote-sudo bleibt beim Operator.

## Vollständige Hook-URL sichtbar – 06.10.2026

Frontend zeigt unter „Callback-URL für diesen Mandanten“ die vollständige
Adresse aus validierter Basis-URL und aktiver Tenant-ID. Der Einstellungswert
steht getrennt im aufklappbaren Bereich „URL-Einstellung“. Die tatsächliche
Zoho-Registrierung bleibt separat prüfbar; Anzeige ändert keine Subscriptions.
Native TypeScript-/Vite-Abnahme und Chrome-Test mit exakter Tenant-URL sowie
390px-Layout bestanden. Bekannte Vite-Chunkgrößenwarnung. Für diese Änderung
ist das Frontend neu bereitzustellen; in diesem Lauf kein Server-Rollout.

## HTTP 503 nach gemeldetem Remote-Rollout – 06.10.2026

ReplicaSet-Vergleich durch Benutzer bestätigt: Die Runtime-Podvorlagen vom
28.09. verwendeten das Init-Image `runtime-agent@sha256:02161f6a78d6384cac7d8e314cd10f9c99b81b5bc52fd848f252df453c336cc2`.
Die neuen Vorlagen vom 06.10. um 01:26:19 CEST verwenden
`runtime-agent@sha256:a1db4fd9b788aae3023f4cfc749bec4873fa109589c218d7caaaab93d8db41b9`.
Beide Stände haben `IfNotPresent` und keine imagePullSecrets. Eine Änderung
der Pull-Policy ist damit ausgeschlossen. Der neue Digest und der zugehörige
anonyme GHCR-Pull mit 401 sind belegt; warum der alte Digest zuvor nutzbar
war (z.B. lokaler Imagebestand oder damaliger Registry-Zugriff), ergibt sich
nicht aus ReplicaSets. Explizites IfNotPresent im lokalen Fix allein behebt
diesen Vorfall nicht; maßgeblich ist die Weitergabe der Pull-Secret-Verweise.

Historischer Vergleich: Die lokalen Sales-Releasewerte vom 20.09., 28.09.
und 06.10. enthalten jeweils ein leeres `imagePullSecret`; dessen Fehlen ist
also keine neue Änderung dieses Sales-Rollouts. Der Plattform-Executor behält
laut bereits am 18.09. enthaltenem Code das bisherige Init-Image bei gleicher
App-Imageversion. Bei geändertem App-Image übernimmt er das aktuell konfigurierte
Helper-Image. Das ist ein belegter möglicher Auslöser beim nächsten App-Rollout,
aber der tatsächliche alte Init-Digest ist ohne alte Live-ReplicaSets noch
nicht bestätigt. Lesender ReplicaSet-Vergleich beim Benutzer angefragt, da
der eigene SSH-Aufruf weiterhin am interaktiven sudo-Erfordernis scheitert.

Nachfolgend durch Benutzer-Liveausgaben eingegrenzt: Bootstrap-Backend und
-Frontend laufen 1/1 Ready mit dem richtigen neuen Release. Beide Runtime-
Instanzpods hängen in `Init:ImagePullBackOff`; `identity-routing-address`
versucht das private Plattform-Runtime-Agent-Image anonym von GHCR zu laden
und erhält HTTP 401. Damit liegt der nachgewiesene Startfehler beim privaten
Init-Image, nicht beim Sales-App-Image. Plattformkorrektur für die Weitergabe
der Agent-Pull-Secret-Verweise an Runtime-Pods sowie explizites IfNotPresent
lokal umgesetzt und mit 39 .NET- und fünf Helm-/Pipeline-Tests geprüft.
Details im Plattform-KI-Kontext vom 06.10.2026. Agent-/Chart-Rollout und
Live-Erfolg der beschriebenen Betriebsreparatur stehen noch aus.

Benutzer meldet erfolgten Rollout, danach fehlendes Frontend. Öffentlich
reproduziert: `/` liefert 503 „No running frontend component container is
available.“, `/manifest.json` liefert 503 für das Backend. Router-Pod laut
Antwortheader: `identity-platform-application-router-7d4b98749b-5lq4w`.
Der lokale Releasebeleg
`app-sales-plattform-4efc8f6da7334c44979a960cf4dd7cc7` enthält Shared 0.1.77,
beide Komponenten mit Tag
`sha-f21bab5f18feaca5c3c05717acbeef852e01f0e5-1791242414468-1`, Namespace
`identity-platform` und die richtige öffentliche URL. Dies bestätigt die
Build-/Releasekonfiguration, nicht die Runtime-Registrierung.

Der zentrale Remote-App-Deploy wartet nur auf den Bootstrap-Rollout;
Registrierung, Runtime-Instanzplanung und Router-Erreichbarkeit sind keine
Erfolgskriterien dieses Skripts. Die 503-Antwort belegt fehlende nutzbare
Routingziele für beide Komponenten, noch nicht deren genaue Ursache.
Live-Pod-/Agent-/Registrierungsdiagnose benötigt Operatorzugriff: SSH funktioniert,
`sudo -n` verlangt ein Passwort und unprivilegiertes kubectl darf die
K3s-Konfiguration nicht lesen. Deploymentausgabe beim Benutzer angefragt;
keine spekulative Änderung an Replikas, Routing oder Berechtigungen.

## Bibliotheksupdate und Webhook-Abnahme 06.10.2026

GitHub Packages direkt abgefragt: Shared **0.1.77** ist die neueste stabile
NuGet-Version und ersetzt in Sales **0.1.74**. React **0.1.60** ist weiterhin
der aktuelle veröffentlichte npm-Stand; mit `npm ci` frisch installiert,
Manifest und Lockfile bleiben auf dieser exakten Version. Keine lokale
Paketkopie und keine neue Veröffentlichung.

Der bereits gesicherte Callback-Fix bleibt aktiv: Der Adapter erhält Basis-URI
und Tenant-ID getrennt und bildet `notify_url` mit `?tenant_id=<Mandanten-ID>`
direkt im ausgehenden Payload. Fehlender Tenant oder Query in der Basis-URI
werden vor Versand abgewiesen. Der manuelle Hook-Job ersetzt die alten Channels.

Nativ unter Windows bestanden: Releasebuild mit Warnungen als Fehlern,
282 Webhook-Prüfungen, 153 Report-Prüfungen, 23 Verbrauchs-/SQL-Prüfungen,
34 Zoho-Task-Payload-Fälle, 22 Webhook-/Routing-/Theme-/Navigationsverträge
sowie TypeScript-/Vite-Produktionsbuild. Bekannte Vite-Chunkgrößenwarnung.

Beide Linux/amd64-Releaseimages über die zentralen App-Buildfunktionen lokal
erfolgreich gebaut (kein Upload): Backend-Manifestdigest
`sha256:ac25e3229f4adab1841fff9c66972bd7f3ccb009cb36286e6ea23f9a213efcaa`,
Frontend-Manifestdigest
`sha256:59c688d203a85b67783e6781071f3d085cb51d2f80972dc36d58af2d159a76ea`.
Temporäre Releasewerte und Image-Tags liegen unter
`backend/obj/webhook-release-20261006/`. Der Build enthält die lokale
Shared-Pin-Änderung zusätzlich zum bestehenden Commit; der SHA-Anteil des
Release-Tags allein beschreibt diesen uncommittierten Unterschied nicht.

Deploymentplan für `ax42-1`/`dev` gegen Plattform `ax42-1` als Vorschau
aufgelöst; öffentlicher Sales-Einstieg weiterhin `https://176.9.57.203:3003`.
SSH und Docker erreichbar. Der Server verweigert `sudo -n k3s kubectl ...`
mit `sudo: a password is required`. Auch der vorgesehene Operator-Rollout
verlangt interaktives sudo; ohne diese Eingabe kein Server-Rollout und keine
Live-Neuregistrierung. In einer nativen PowerShell 7 im Sales-Appverzeichnis:

```powershell
./deploy-all.ps1 -Target ax42-1 -PlatformTarget ax42-1 -Environment dev
```

Danach im betroffenen Tenant „Hooks aktualisieren“ ausführen, die registrierte
URL einschließlich Tenant-Query prüfen und eine echte Zoho-Zustellung abnehmen.
Lokale Tests und gebaute Images sind kein Nachweis einer behobenen Live-Zustellung.

## Lokale Sicherung 04.10.2026

Alle offenen Code-, Test- und Kontextänderungen werden auf Benutzerauftrag
gemeinsam committed. Enthalten sind Zoho-Callback-/Schema-/Mapping-Korrekturen,
die ausschließliche Webhook-Konfiguration über Tenant-AppSettings,
Reportannahmen für fehlende EUR-Währung bzw. fehlenden Betrag und die
Jahresziel-Maske samt API, Validierung und Konfliktschutz. Die nachfolgenden
Arbeitsberichte behalten ihre ursprünglichen Datums- und Rolloutgrenzen.
Keine erneute Live-Zoho-Registrierung, kein Deployment oder Paket-Publish;
ein lokaler Commit ist kein Nachweis der produktiven Wirksamkeit.

Vor der Sicherung nativ unter Windows erneut geprüft: **153 Report-/Mapping-
Prüfungen, 282 synthetische Zoho-Webhook-Prüfungen, zehn Webhook-Vertragstests
und die Frontend-TypeScript-Prüfung bestanden**. Die Tests schreiben weder
in eine produktive Datenbank noch nach Zoho.

## Dokumentationsabgleich 02.–03.10.2026

Git-Historie und Arbeitsbaum wurden am 03.10. gelesen. Keine neuen Sales-Commits
in diesem Zeitfenster; bestehende lokale Änderungen betreffen Zoho-Adapter,
Schema-/Hook-Verarbeitung, Webhook-Settings und Deployment-Defaults sowie
Reports/Jahresziele mit Frontend und Tests. Die folgenden datierten Abschnitte
bleiben deren Nachweise. Weder neue Tests noch ein Sales-Rollout oder eine
Zoho-Live-Neuregistrierung wurden durch diesen Dokuauftrag durchgeführt.
Plattform-Shared 0.1.77 in Aufmass ist kein automatisches Sales-Paketupdate.
GAEB und Azure-Security sind in ihren jeweiligen Repository-Kontexten verlinkt,
siehe [Projektkontext](00-projektkontext.md).

## Callback-Registrierung erzwingt Tenant-ID – 30.09.2026

Lokal: Zoho-Adapter erhält Basis-URI und Tenant-ID getrennt und baut daraus
die vollständige notify_url direkt im Request-Payload. Ungültige Tenant-ID
oder Query in der Basisadresse werden vor Versand abgewiesen.
Native Windows-Abnahme: 282 synthetische Zoho-Prüfungen einschließlich
Releasebuild und zehn Webhook-Vertragstests bestanden. Zwei bereits
veraltete Teststellen (fehlender Abstractions-Import und Event-Parameter
bei CanKeepSubscription) an den vorhandenen Produktionsstand angepasst.
Kein Deployment, keine Live-Neuregistrierung oder erfolgreiche Zustellung
in diesem Lauf nachgewiesen. Der Supportbefund vom 28.09. bleibt dadurch
nicht als produktiv behoben gewertet.

## Zoho-Webhook-URL nur noch Mandanten-AppSetting – 20.09.2026

`Zoho__WebhookUrl` wird nicht mehr in `appsettings.Deployment.json`, im lokalen
Deployment-Environment oder über `Zoho:WebhookUrl` konfiguriert. Die Hook-
Basisadresse bleibt als Manifest-Setting `zoho.webhookUrl` im Scope `tenantApp`
erhalten und muss pro Mandant unter Tenant-Portal → SalesPlattform → AppSettings
gesetzt werden. Fehlt der Wert, blockieren Hook-Job, Übersicht und Live-Prüfung
sichtbar statt auf einen globalen Deployment-Default zurückzufallen. Die
Tenant-ID wird bei Registrierung weiterhin automatisch als Query ergänzt.

Statisch geprüft: Deploymentprofile und lokales Environment enthalten keinen
Webhook-Default mehr, der Backend-Resolver hängt nicht mehr von `ZohoOptions`
ab, und Doku/UI verweisen nicht mehr auf einen Deployment-Fallback. Native
Windows-Regressionen sind noch offen, weil Windows-PowerShell aus der aktuellen
WSL-Sitzung mit `UtilBindVsockAnyPort` nicht gestartet werden konnte.

## „Hooks aktualisieren“ baut Hooks neu auf – 20.09.2026

Der Benutzerbefund `SubscriptionsUnchanged: 12`, `SubscriptionsRenewed: 0`
belegte den bisherigen No-op bei einem manuellen Wartungslauf. Jetzt erzwingt
`Trigger = manual` neue Channels/Tokens für alle verfügbaren relevanten Module.
Geplante Läufe bleiben fristgesteuert. Der bisher irreführende reine Lesebutton
heißt „Übersicht aktualisieren“. „Hooks aktualisieren“ startet nach Bestätigung
den bestehenden tenantadmin-geschützten Plattformjob, einschließlich zentraler
Queue/Exklusivgruppe, ohne neue Plattform-API oder Shared-Paketversion.

Neu anlegen/bestätigen → lokal speichern → alten Channel deaktivieren/bestätigen.
Fehler werden pro Modul isoliert und sicher im Jobprotokoll sichtbar. Bei
Registrierungsfehler bleibt der alte lokale Status unangetastet, bei unklarem
DB-Commit werden keine Channels gelöscht; bei Cleanup-Fehler bleibt die neue
Zuordnung aktiv. Cancellation/Prozessabbruch können Channels bis zum Ablauf
hinterlassen; keine automatische Orphan-Bereinigung. Der bestehende Crawl bleibt
Lückenschluss. Siehe [Neuaufbau und Grenzen](02-datenmodell-und-zoho.md#hooks-wirklich-aktualisieren).

Native Windows-Abnahme: **276 synthetische Zoho-Prüfungen**, **10 Webhook-
Vertragstests**, Backend-Releasebuild ohne Warnungen/Fehler, TypeScript-/Vitebuild
und Chrome-Test bestanden. Abgedeckt sind Bestätigung/Abbruch/Doppelclick,
tenantbezogener POST, 403/409/unklare Antwort ohne automatischen Retry, sichere
Providerbestätigungen, Registrierungs-/Speicher-/Cleanup-Fehler, unklarer Commit
und Cancellation an den Phasengrenzen. Bekannte Vite-Chunkgrößenwarnung bleibt.

Noch kein Rollout oder Live-Zoho-Schreibtest. Sales-Backend und -Frontend neu
bereitstellen, danach im Tenant „Hooks aktualisieren“ bestätigen oder den Job
manuell starten. Bei zwölf vorhandenen verfügbaren Hooks und vollständigem
Erfolg erwartet: `SubscriptionsRenewed: 12`, `SubscriptionsUnchanged: 0`, keine
Warnungen. Eine Cleanup-Warnung kann trotz erhöhtem Renewed-Zähler auftreten;
immer Warnungen/Jobprotokoll prüfen, danach Live-Prüfung und neuen Testanruf.
Keine OAuth-/Firewall-/Schlüsseländerung, keine Migration, kein Commit/Push.

## Erweiterte Zoho-Hook-Diagnose – 20.09.2026

Der vorhandene manuelle Prüfbutton ergänzt URL/Ablauf/Ereignisse um Feldfilter,
sicheren Token-Hash-Vergleich und lokalen Channel-Status/Ablauf. Keine Rohdaten,
Tokens oder Hashes im Browser/Log; keine Registrierungsschreibzugriffe oder
Callback-Proben. Unbekannte Filterformate sind nicht prüfbar, nicht automatisch
filterfrei. Nach dem Providerabruf nochmals gelesener lokaler Zustand schützt
vor einer Bestätigung inzwischen erneuerter Channels. Details und Grenzen:
[Zoho-Diagnose](02-datenmodell-und-zoho.md#registrierung-direkt-bei-zoho-prüfen).

Native Windows-Abnahme: Backend-Releasebuild ohne Warnungen/Fehler, **213
synthetische Zoho-Prüfungen**, **9 Webhook-Vertragstests**, TypeScript-/Vitebuild
und Chrome-Smoke-Test bestanden. Browserfälle umfassen Erfolg, Feldbedingungen,
Token-Abweichung, lokalen Ablauf, Scope/403, Wiederholung und 390px-Layout.
Bekannte Vite-Warnung für einen JS-Chunk über 500 kB bleibt. Der erste Testlauf
hatte veraltete Paketauflösung; Restore mit `backend/NuGet.Config` und bereits
vorhandenen Paket-Credentials im Testprozess löste dies ohne Credentialänderung.

Noch nicht ausgerollt. Für diese Erweiterung **nur Sales-Backend und -Frontend**
neu bauen/deployen; keine Migration, kein Plattform-/Shared-Paketupdate.
Danach im gewünschten Tenant **Import → Hooks und Ereignisse → Calls →
Registrierung bei Zoho prüfen**. Die echte Providerantwort und Callback-Zustellung
bleiben live abzunehmen. Zum damaligen Diagnosestand erzwang ein manueller Start
noch keine Erneuerung; die nachfolgende Neuaufbau-Korrektur ändert dies explizit.
Keine Live-Zoho-Abfrage, kein Commit/Push oder Serverdeployment in diesem Lauf.

## Direkte Paketaktualisierung – 19.09.2026

Shared **0.1.74** und Common-React **0.1.60** aus GitHub Packages integriert.
Die Komponenten wurden lokal nativ gebaut und direkt veröffentlicht, ohne
Git-Push oder Publish-Workflow. Die Korrekturen betreffen zentrale Trace-Suche
und die lokale Diagnose von Logexportfehlern. Vorhandene Zoho-/UI-Änderungen
bleiben erhalten. Kein Serverdeployment durch dieses Paketupdate.

Nativ unter Windows: frischer Registry-Restore, Backend-/Frontendbuild,
TypeScript-Prüfung und zwölf Root-/Theme-/Navigationsverträge bestanden.
Der Theme-Vertrag prüft eine exakte Registry-Version samt Lockfile, installiertem
Paket und Theme-Funktionen, ohne die historische Version 0.1.59 festzuschreiben.
NuGet-Download und npm-Lockfile stimmen per Hash mit den veröffentlichten
Artefakten überein.

## Zoho-Registrierung live prüfen – 19.09.2026

Frontend/Backend um **Import → Hooks und Ereignisse → Registrierung bei Zoho
prüfen** erweitert. Modul wählen, manuelle lesende Zoho-Abfrage; die normale
Übersicht ruft Zoho nicht auf. Lokale Channel-ID/Soll-URL werden tenantisoliert
aufgelöst. Rückgabe ohne Tokens/Rohantworten; Vergleich von Callback-URL,
Ablauf und Ereignissen. Kein automatisches Reparieren oder Zustelltest.
Details: [Liveprüfung](02-datenmodell-und-zoho.md#registrierung-direkt-bei-zoho-prüfen).

OAuth-Defaults und Overrides ergänzen `ZohoCRM.notifications.READ`. Bestehende
Verbindungen benötigen bei Scope-Fehler nach dem Rollout eine erneute
Autorisierung über **Zoho verbinden**; kein stilles Upgrade bestehender Grants.
Kein Plattformupdate, keine Migration und keine neue Shared-/React-Version.

Nativ unter Windows bestanden: Releasebuild mit **161 synthetischen Webhook-
Prüfungen**, **9 Webhook-Verträge**, Scope-Tests, TypeScript-/Vitebuild sowie
Chrome-Smoke-Test einschließlich neuer Liveprüfungs-UI und bestehender
Theme-/Hook-/Usage-Fälle. Bekannte JS-Chunkgrößenwarnung bleibt. Keine echte
Zoho-Lese-/Schreibanfrage in diesen Tests, kein Commit/Push oder Deployment.
Zum Bereitstellen Sales-Backend und -Frontend neu bauen/ausrollen; Live-Abnahme
und die Ursache der bisher fehlenden Zoho-Zustellung bleiben offen.

## Hook-Registrierung: `channel_expiry` korrigiert – 19.09.2026

Gemeldeter Livefehler: Zoho lehnt `channel_expiry` bei `/crm/v8/actions/watch`
mit HTTP 400 / `INVALID_DATA` (erwartet `datetime`) ab. Der Sales-Adapter
verwendet jetzt ganze Sekunden mit explizitem Offset statt `.ToString("O")`.
Die Laufzeit bleibt unverändert unter sieben Tagen. Details:
[Zoho-Hook-Datumsformat](02-datenmodell-und-zoho.md#zoho-hook-registrierung-datumsformat).

Regression zuerst mit altem Format reproduziert (Zeitstempel mit sieben
Nachkommastellen), danach nativ unter Windows bestanden: **132 Webhook-/Settings-
Prüfungen** einschließlich 45 neuer Payload-Prüfungen sowie **34 Task-Payload-
Fälle**, jeweils mit Release-Build. Nur synthetische Daten, keine CRM-Aufrufe.
Kein Deployment, Push oder neuer Commit in diesem Korrekturlauf. Sales-Backend
neu bauen/ausrollen, anschließend `CRM-Hooks erneuern` manuell starten und
eine neue Zoho-Änderung bis Eingang und Verarbeitung prüfen. Live-Abnahme offen.
Für diesen Fix keine Plattform-, Shared-Paket- oder Datenbankänderung erforderlich.

## Aktueller Quellenabgleich und Sicherungsstand – 19.09.2026

Der gesamte lokale Arbeitsstand wird je Repository committed; dieser Auftrag
umfasst keinen Push oder Rollout. Die datierten Abschnitte darunter sind
historische Umsetzungsschritte. Angaben wie "kein Commit/Push" und alte Paket-
Pins beschreiben deren damaligen Zeitpunkt. Benutzer-Deployments wurden gemeldet,
ihre Image-/Quellrevisionen hier aber nicht erneut geprüft.

Aktuelle Paketdateien: Shared **0.1.73**, Common-React **0.1.59** einschließlich
Registry-Lockfile. Quellstand enthält Arbeit/Steuerung-Navigation, Report-
Nachweistabellen, Common-Themes, tägliche CRM-Verbrauchsauswertung, tenantbezogene
Hook-URL/Übersicht und Webhook-Vertrag sowie Zoho-Scope-/Task-Payload-Korrekturen.
Es gibt jetzt vier synthetische .NET-Prüfprogramme unter `tests/` (keine
Live-CRM-Integrationstests); ältere Aussagen "kein .NET-Testprojekt" gelten nicht
als Beschreibung dieser Regressionen.

Vor Sicherung erneut nativ unter Windows geprüft: 34 Task-Payload-Fälle,
87 Webhook-/Settings-Prüfungen, 129 Report-Assertions, 23 Verbrauchs-/SQL-
Prüfungen, 18 Node-Verträge sowie Scope-/HTTPS-/Pipeline-Prüfungen bestanden.
TypeScript-/Vite-Produktionsbuild bestanden; bekannte Warnung für einen JS-Chunk
über 500 kB bleibt. Der SQL-Test übersetzt die echte Npgsql-Abfrage, ohne eine
Datenbank zu kontaktieren. Keine produktiven CRM-Aufrufe, keine Schlüsseländerung.

Der zentrale SSH-Fix gilt bereits mit aktualisiertem lokalem Plattform-Tooling.
Die App-Fehlerisolation benötigt dagegen API/DeploymentController/Router-Rollout,
keine Sales-/Aufmass-Neuinstallation. Der Webhook-Vertrag erfordert separat
zuerst die Plattform und danach Sales-Manifest/Backend. Quellen, Tests und
Live-Grenzen: [Plattformstand 19.09.2026](../../../../IdentityPlattform/docs/stand-2026-09-19.md).

```powershell
# Sales: konfigurierte Platzierung nur anzeigen
.\deploy-all.ps1 -Environment dev -Preview
# Explizit: beide Targets sind Pflicht; kein stiller Standortwechsel
.\deploy-all.ps1 -Target ax42-1 -PlatformTarget ax42-1 -Environment dev -Preview
```

## Tagesverbrauchsdiagramm – 19.09.2026

Frontend/Backend um `/api/integrations/usage/daily` und den Tagesverlauf in
API-Verbrauch erweitert. 7/30/90 UTC-Tage, geschätzte Verbrauchseinheiten,
Requests und Fehler; getrennte Reihen je Provider/Verbindung/Einheit und
Tageswerttabelle. Tenant-Admin-Zugriff und lokale Tenant-Datenhaltung bleiben.
Keine neue Migration oder Shared-Paketversion. 23 synthetische Aggregations-/
SQL-Übersetzungsprüfungen und nativer Chrome-Test bestanden. Kein Rollout,
kein Commit/Push, keine produktiven CRM-/Datenbankabfragen.
Abschließender Backend-/Test-Releasebuild ohne Warnungen/Fehler, Frontendbuild
und 18 bestehende Theme-/Navigation-/URL-/Webhook-Verträge bestanden. Bekannte
Vite-Warnung wegen eines JavaScript-Chunks über 500 kB bleibt unverändert.

## Korrektur: mandantenbezogene Hook-URL – 19.09.2026

Das Sales-Manifest registriert jetzt `zoho.webhookUrl` im Scope `tenantApp`,
Anzeige **Zoho Webhook-URL** unter Tenant-Portal → SalesPlattform → AppSettings.
Kunden mit verschiedenen Frontend-Hosts verwenden jeweils ihre eigene URL.
Job und Übersicht lesen denselben Tenant-Wert; leer/nicht gesetzt blockiert
die Hook-Registrierung sichtbar. Es gibt keinen Deployment-Fallback. Ungültige
URLs blockieren die Registrierung; sie werden nicht durch eine andere Adresse
ersetzt. Eine Änderung wirkt beim nächsten Hook-Job ohne OAuth-Neuverbinden oder Backend-Neustart.
Zum Bereitstellen des neuen Settings muss dieser Backend-/Manifeststand ausgerollt
werden; Anzeige der Quelle benötigt auch das neue Frontend. Keine neue
Paketversion/Migration. Keine Domains/Zertifikate provisioniert, kein Live-Rollout.

Native Abnahme: 87 Hook-/Settings-Prüfungen, 18 Verträge, Backend-/Frontendbuild
und Chrome-Test bestanden. Zwei verschiedene Kunden-Hosts werden getrennt
aufgelöst; Benutzerwerte und fremde Tenant-Werte überschreiben den Mandantenwert
nicht. Frontend-Build weiterhin mit bekannter Chunkgrößenwarnung.

## Hook-Übersicht und Ereignislogs – 19.09.2026

Sales-Frontend und -Backend lokal erweitert: CRM-Integration zeigt Tenant-Admins
URL, Modulregistrierungen und Ereignisstatus; technische Logs und Joblogs nutzen
die Ereignis-ID. Endpoint `/api/integrations/zoho/hooks` ist lesend und zusätzlich
Tenant-Admin-geschützt. Neue Queue-Payloads werden ohne Prüf-Token gespeichert.
Kein Paketupdate und keine Migration. Bestehende Alt-Payloads bleiben unverändert.

Native Backend-/Frontend-Builds, 71 Hook-Prüfungen, 16 Vertragstests und Chrome-
Test (Filter, Pagination, Fehler/Refresh, Detailansicht, mobile Darstellung)
bestanden. Bekannte Frontend-Chunkgrößenwarnung. Kein Commit/Push oder Rollout.
Für diesen Arbeitsstand gelten weiterhin die unten beschriebenen API-/Router-
Voraussetzungen des öffentlichen Webhook-Vertrags. Nach dem Rollout Schema-Cache
und `CRM-Hooks erneuern` prüfen/starten. Der konfigurierte Job verarbeitet bis zu
100 wartende Ereignisse pro Lauf; die Übersicht ändert den Zeitplan nicht.

## Common-Themes integriert – React 0.1.59, 19.09.2026

Das lokal gebaute gemeinsame React-Paket **0.1.59** wurde in GitHub Packages
veröffentlicht. Sales verwendet es aus der Registry in `frontend/package.json`
und `package-lock.json`, kein lokaler Paketverweis. Der gemeinsame Umschalter
bietet Hell/Dunkel/System; Sales initialisiert die Browserpräferenz vor dem
ersten React-Render. Eigene Stylesheets verwenden die gemeinsamen Theme-Tokens.

Native Windows-Prüfungen: 138 Common-Tests, zwölf Sales-Integrations-/Navigations-/
URL-Tests, Typecheck und Produktionsbuild bestanden. Chrome-Test mit installiertem
Registry-Paket ohne Alias prüft Systemmodus, Umschaltung, gespeicherte Auswahl
nach Reload, Report-Dialog/Tabelle und 390px-Handylayout. Bekannte Vite-Warnung:
ein JS-Chunk über 500 kB. Kein Serverrollout, kein Commit/Push dieses Arbeitsstands.

Für die Themes kann Sales nun mit diesem lokalen Arbeitsstand gebaut/ausgerollt
werden. Git-/CI-Rollout benötigt zusätzlich dessen Commit/Push. Der ebenfalls
lokal enthaltene Webhook-Vertrag verlangt separat zuerst die aktualisierte
Plattform-API und den Application Router (siehe folgenden Abschnitt). Keine neue
NuGet-Version erforderlich. Aufmass wurde in dieser Sales-Integration nicht verändert.

## Webhook-Anbindung 19.09.2026 – lokaler Implementierungsstand

Remote-Deploymentprofile setzen keine `Zoho__WebhookUrl` mehr. Die öffentliche
Callback-Basis-URL ist ausschließlich das Tenant-AppSetting `zoho.webhookUrl`.
Das Backend-Manifest meldet den exakten POST-Pfad
über den generischen `webhooks`-Vertrag an der Plattform an. Hierfür sind
**zuerst Plattform-API und Application Router, danach Sales** neu auszurollen;
anders als beim separaten ValidateSet-Skriptfix ist das eine Laufzeitänderung.
Shared 0.1.73 überträgt das Manifest bereits unverändert; keine neuen NuGet-/npm-
Pakete und keine Datenbankmigration. Danach im Tenant `CRM-Hooks erneuern`
manuell starten; dort werden Channel und Token automatisch verwaltet. Keine
manuelle Channel-/Tokenpflege; die Tenant-ID wird bei Registrierung ergänzt.
URLs werden je Tenant unter `zoho.webhookUrl` gepflegt (siehe Korrektur oben).

Geprüft unter nativem Windows: synthetische Webhook-Sicherheits-/URL-/Erneuerungs-
Tests (`tests/ZohoWebhook`), vier Sales-Deployment-Vertragstests und Rendern der
echten Sales-Profile für ax42-1 und ax42-2. Plattform: komplette API-/Router-Suite
(955 bestanden, 13 übersprungen) und neun Runtime-Profiltests. Kein Serverrollout,
keine Live-Zoho-Anfrage. Für den Betriebsnachweis muss danach eine reale Änderung
in Zoho als verifiziertes Event und beim Hook-Wartungslauf als verarbeitet erscheinen.

## Paketupdate 19.09.2026 – React 0.1.58 / Job-Liveverbindung

Frontend-Manifest und Registry-Lockfile referenzieren das lokal gebaute und in
GitHub Packages veröffentlichte `@hammer2fall/identity-platform-react` **0.1.58**
(Plattform-Commit `7538c13`). Die zentrale SignalR-Anbindung verwendet bei
Cross-Origin-Aufrufen Bearer-Tokens ohne Browser-Credentials (`withCredentials:
false`). Der Fix ist im installierten Registry-Paket nachgewiesen; kein lokaler
Tarball-Verweis. NuGet bleibt auf dem bereits veröffentlichten
`IdentityPlatform.Shared` **0.1.73**.

Nativ unter Windows: Paketinstallation, TypeScript-Prüfung, Vite-Produktionsbuild
und alle drei Root-URL-/Runtime-Profiltests bestanden. Der Testloader bindet die
neue `SessionView`-Abhängigkeit aus dem tatsächlich installierten Paket ein.
Vite meldet weiterhin einen Chunk über 500 kB. Kein Serverrollout oder Nachweis
einer erfolgreichen Live-Verbindung; dafür Sales neu bauen/ausrollen und die
Jobübersicht im Browser prüfen. Die folgenden Abschnitte sind historische Stände.

## Paketupdate 17.09.2026 – Shared 0.1.73

Das Backend referenziert das veröffentlichte `IdentityPlatform.Shared` **0.1.73**
(Plattform `cc8c7e1`, erfolgreicher Publish-Lauf `35222731466`). Nativ unter
Windows: frischer Restore aus GitHub Packages und Release-Build ohne Warnungen
oder Fehler bestanden. Sales hat weiterhin kein eigenes .NET-Testprojekt;
der Build ist kein API-Test. React bleibt unverändert.
Shared liefert aggregierte Broker-Drain-Meldungen; die vorhandene Jobintegration
bleibt appseitig bestehen. Server-Enrolment ist Aufgabe des Infrastruktur-Agenten.
Kompatible Plattform und Runtime-Infrastruktur vor der App separat aktualisieren.
Kein Serverrollout; ältere Paketstände unten sind historische Nachweise.

## CI-VPN 17.09.2026

Der Release-Workflow übergibt `APP_CI_TRANSPORT`/`APP_CI_VPN` an das zentrale
Plattform-Tooling. Serverbootstrap 00 richtet optional zum bestehenden
Plattformbetrieb einen gemeinsamen App-CI-Zugang einschließlich separatem
WireGuard-VPN auf UDP 51821 ein; 02 veröffentlicht den Repository-/Environment-
Peer als Secret. Diese 02-Phase läuft im vollständigen 00-Aufruf automatisch,
einschließlich Setzen des geprüften Tooling-Pins nach erfolgreicher Plattform-CI.
Deployment bleibt separat. Die bestehenden Plattform-Credentials bleiben unverändert.
Workflow und VPN-fähigen Plattform-Commit veröffentlichen; vollständigen
geprüften SHA in `IDENTITY_PLATFORM_DEPLOY_REF` setzen. Alter Pin mit VPN wird
abgewiesen, kein öffentlicher SSH-Fallback. Keine App-/Paketänderung und kein
durch diese Änderung nachgewiesener Live-Rollout.

## Pipeline-Umstellung 16.09.2026

`Deploy SalesPlattform` (`.github/workflows/release.yml`) rollt ausschließlich
Sales auf `ax42-1/dev` im Namespace `identity-platform` aus, neben der Plattform.
Keine Plattform-/Datenbankaktualisierung. App-CI, GHCR-Builds und digest-gepinnter
Helm-Rollout nutzen dasselbe zentrale Tooling wie Aufmass. Kein SCP/Remote-Shell-
Deployment. `access_only` prüft nur den Zugang; Standard ist echter Rollout.
Environment: `sales-plattform-ax42-1-dev`, eigene `APP_CI_*`-Zugangsdaten,
Repository-Secret `PACKAGES_TOKEN` und vollständiger geprüfter Plattform-SHA
in `IDENTITY_PLATFORM_DEPLOY_REF`. Deployment ausschließlich von `main`,
ohne Branch-Override; das Environment ebenfalls auf `main` begrenzen.
Anleitung: [App-Pipelines](../../../../IdentityPlattform/docs/app-pipeline.md).
Veröffentlichung auf `main` beauftragt. GitHub-Environment ausschließlich für
`main` eingerichtet; App-CI-Secrets/Zielvariablen und Repository-Secret
`PACKAGES_TOKEN` fehlen noch (Prüfung 16.09.2026). Deshalb noch kein Deployment,
keine Secret-/RBAC-/Paketänderung. Ältere Meldungen unten sind historische Stände.

## Paketupdate 15.09.2026 – zentrale Browser-Authentifizierung

Frontend-Manifest und Registry-Lockfile verwenden das veröffentlichte
`@hammer2fall/identity-platform-react` **0.1.56** (Plattform `039b179`,
erfolgreicher Publish `34949426286`). NuGet bleibt unverändert auf **0.1.71**.
Die vorhandenen `authorizedFetch`-Aufrufe profitieren ohne Axios-Umbau von
gemeinsamer Token-Erneuerung, Erkennung verspäteter 401 und höchstens einem
GET-/HEAD-Replay. Schreibaktionen werden nicht automatisch wiederholt;
Netzwerkfehler und beliebige 401 löschen keine gültige Sitzung. Job-Liveverbindungen
und Browser-Logs beziehen Tokens ebenfalls zentral.

Nativ unter Windows: Neuinstallation aus dem Registry-Lockfile, Typprüfung
und Frontend-Produktionsbuild bestanden. Kein Serverrollout.
Die folgenden Abschnitte dokumentieren ältere Stände.

## Paketupdate 14.09.2026 – Shared 0.1.71 / geordneter Instanz-Auslauf

Das Backend referenziert das veröffentlichte **IdentityPlatform.Shared 0.1.71**
(Plattform `623c901`, erfolgreicher Publish `34899664801`). Der vorhandene
`AddIdentityPlatform()`-Aufruf bindet den neuen Drain-/Aktivitätsvertrag ein;
zentrale CRM-Jobs werden dabei bis zu ihrem tatsächlichen Abschluss erfasst.
React bleibt auf **0.1.55**, Manifest und Registry-Lockfile unverändert.

Nativ unter Windows: frischer Registry-Restore mit bestätigter GitHub-Packages-
Herkunft und Backend-Releasebuild ohne Warnungen/Fehler bestanden. Sales hat
kein eigenes .NET-Testprojekt. Eine veraltete separate NuGet-Credentialvariable
wurde bei der Prüfung durch den gültigen vorhandenen `GITHUB_PACKAGES_TOKEN`
nur im Prüfprozess übersteuert; keine Secrets gespeichert/geändert.
Kein Serverrollout. Plattform, Runtime-Agent und App separat aktualisieren.
Die folgenden Abschnitte dokumentieren ältere Stände.

## Veröffentlichungsstand 14.09.2026 – reguläre Logging-Pakete

Integration `b3a904e` auf `main` gepusht. Nach erfolgreichem Plattform-Publish
(`28f7bd5`, Lauf `34868012893`) sind die regulären Referenzen auf
`IdentityPlatform.Shared` **0.1.70** und `@hammer2fall/identity-platform-react`
**0.1.55** einschließlich Registry-Lockfile aktualisiert. Keine lokalen Archive
oder temporären Versionsoverrides in der Releasekonfiguration. HelloWorld wird
weiterhin im Plattform-Repository mit Projekt-/Dateiverweisen gebaut.

Native Windows-Prüfungen mit frischen Registry-Caches bestanden: Backend-
Releasebuild ohne Warnungen/Fehler, Frontend-`npm ci`, Typprüfung und
Produktionsbuild. Kein eigenes .NET-Testprojekt; der Build ist kein API-Test.
Sales-GitHub-CI bleibt durch das fehlende Repository-Secret `PACKAGES_TOKEN`
blockiert (Lauf `34868020405`); es wurden keine Secrets verändert.
Kein Image-/Serverrollout. Für den Betrieb zuerst die Plattform aktualisieren,
danach die App neu bauen/deployen. Ältere Abschnitte sind Zwischenstände.

## Änderung 14.09.2026 – gemeinsamer App-Rollout

Sales, Aufmass und HelloWorld nutzen jetzt denselben zentralen Builder und
Rolloutablauf. `deploy-all.ps1` und `rebuild-all.ps1` sind nur noch Delegationen.
`appsettings.Build.json` beschreibt Dockerfile-/Kontext-/Manifestpfade;
`deploy/local-environment.ps1` erhält ausschließlich die bisherigen lokalen
Sales-Mailpit-/Zoho-Einstellungen. App-Startup registriert über Shared,
kein Warten auf Registrierungslogs oder öffentlichen HTTP-200 im Deployment.
Native Windows-Tests mit simulierten Containerwerkzeugen sowie Sales-HTTPS-/
Delegationsverträge bestanden. Kein Image-/Serverrollout, kein Paketupdate.
Details: [Zentrales Tooling](../../../../IdentityPlattform/docs/app-deployment-tooling.md).

## Korrektur 13.09.2026 – Installation ohne öffentlichen Web-Smoke

Lokaler Rebuild und gemeinsames Remote-/CI-Tooling hängen keine automatische
Startseiten-/Asset-Abnahme mehr an den Container-Rollout an. Startup-Registrierung
über Shared bleibt im Startup, Kubernetes-Bereitschaft wird geprüft; Browserzugriff, Login
und Routing sind separat abzunehmen. Der HTTPS-Vertragstest schützt diese
Trennung. Skriptänderungen lokal, noch nicht nativ getestet/committed/gepusht;
keine Library-/Imageänderung. Ältere Hinweise auf automatische HTTPS-Abnahme
im Rebuild beschreiben den vorherigen Ablauf.

## Ergänzung 13.09.2026 – Shared 0.1.62

Das Backend wurde nach erfolgreicher Paketveröffentlichung auf
`IdentityPlatform.Shared` **0.1.62** aktualisiert (Library-Commit `bbe4bd5`,
Publish-Lauf `34755308925`). Registry-Restore und tatsächliche Auflösung dieser
Version sind geprüft. Der bestehende Aufruf `builder.AddIdentityPlatform()`
meldet mit injizierter Runtime-Konfiguration Manifest und Standort beim App-Start;
das Deployment übernimmt keine separate Registrierung. Vor dem App-Rollout die
Plattform für diesen Startup-Vertrag aktualisieren. Keine Secret-/URL-Änderungen.

Nativ unter Windows bestanden: Release-Build ohne Warnungen/Fehler, drei
Root-/OIDC-Tests und HTTPS-/Lifecycle-Verträge. Auch der linux/amd64-Backend-
Imagebuild über Windows Docker Desktop mit dem Registry-Paket bestand.
Ein eigenes .NET-Testprojekt
existiert weiterhin nicht; der Build wird nicht als API-Test ausgegeben.
React unverändert. Consumer-Update lokal, noch nicht committed/gepusht und
nicht auf einem Server ausgerollt; ältere Paketstände unten sind historisch.

## Ergänzung 12.09.2026 – Remote-Integration

`IdentityPlatform.Shared` wurde nach erfolgreicher Veröffentlichung auf
`0.1.60` aktualisiert; React bleibt `0.1.50`. Der obsolete HTTP-/Container-DNS-
Default für die Platform API wurde entfernt. Der gemeinsame Deploymentplan
liefert die öffentliche HTTPS-Origin; API-Aufrufe nutzen `/api`.
`dev/main` installiert Sales weiterhin auf AX42-1 bei derselben Plattform.
WireGuard-/Remote-Bootstrap wird ausschließlich vom gemeinsamen Plattform-
Tooling geliefert, nicht als zweite Sales-Implementierung. Grenzen:
[Server-VPN](../../../../IdentityPlattform/docs/server-vpn.md).

Die ersten nativen Consumer-Builds wurden beim Registry-Restore mit HTTP 401
blockiert (kein Compilerfehlernachweis). `GITHUB_PACKAGES_TOKEN` lokal und
`PACKAGES_TOKEN` in CI benötigen gültigen Paketzugriff. Kein Live-Rollout.

Dieser Abgleich konsolidiert die Arbeit vom 08.–10.09.2026. Architektur,
gemeinsame Fehlerkorrekturen und Betriebsgrenzen stehen im
[Plattform-Gesamtstand](../../../../IdentityPlattform/docs/stand-2026-09-10.md).
Ältere Tagesnotizen sind historische Nachweise, keine noch offenen Aufträge.

## Auslieferungsstand

- Sales-Commit `691e9c2` wurde auf `main` gepusht.
- Gemeinsame Plattformkorrekturen: `bd533b7` auf `main` gepusht.
- React-Library `0.1.50` ist veröffentlicht und in Sales-Paketmanifest und
  Lockfile übernommen. Das Backend verwendet `IdentityPlatform.Shared` `0.1.59`
  mit dem einheitlichen `/api`-Vertrag; NuGet-Versionen werden unabhängig verwaltet.
- Native Windows-Prüfungen: Backend-Release-Build ohne Warnungen/Fehler,
  Frontend-Lint/-Build, drei Root-/OIDC-Vertragstests und HTTPS-Vertragstests
  bestanden. Ein eigenes Sales-.NET-Testprojekt existiert nicht.
- Das ist kein Nachweis eines erfolgreichen vollständigen Hetzner-Rollouts
  oder eines interaktiven Microsoft-Logins. Diese Abnahmen bleiben offen.

## Profile und Aufruf

Lokale Befehle ausschließlich in Windows PowerShell, kein WSL. Im Sales-Ordner:

```powershell
.\deploy-all.ps1 -Target local -PlatformTarget local -Environment dev
# Alternativer Zielrechner, kein zusätzlicher Plattform-Rollout:
.\deploy-all.ps1 -Target ax42-1 -PlatformTarget ax42-1 -Environment dev

# Separater Runtime-Cluster, zugehörige Plattform bleibt AX42-1
.\deploy-all.ps1 -Target ax42-2 -PlatformTarget ax42-1 -Environment dev
```

`appsettings.Deployment.json` ist die Quelle der Sales-URLs:

| Profil | Sales-Frontend |
| --- | --- |
| local / dev | `https://127.0.0.1:3003` |
| ax42-1 / dev | `https://176.9.57.203:3003` |

Zoho-Redirect-/Frontend-Callbacks stehen ebenfalls hier unter `BackendUrls`.
Das öffentliche `assets/deployment-config.js` liefert die aufgelösten
Runtime-URLs, niemals Secrets. Vite-Defaults sind kein Remote-Deploymentprofil.
`ClusterNamePrefix` verändert Namen, nicht Ports/URLs. Keine zweite Installation
auf demselben Target/Environment mit unverändertem Portprofil erstellen.

## Zuständigkeiten und korrigierte Fehler

Sales deployt nur Sales. Die Plattform muss vorher separat installiert und mit
dem Vertrag `registration-v1` aktualisiert sein. Das Sales-Manifest meldet seine
URLs; die Plattform richtet daraus die HTTPS-Einstiege ein und lädt bekannte
aktive Manifeste auch nach ihrem Neustart. Kein statischer Sales-Eintrag in
einem zweiten Plattform-Helm-Profil.

Der Weg ist Traefik → zusammengeführter Application Router → Sales. Der Router
wählt tenant-/app-bezogen zulässige Shared-/Dedicated-Ziele; Sales führt keinen
eigenen Cluster- oder Routerpool. Backend-Runtime-Env und zentrale Secret-
Verweise kommen aus dem Plattformgenerator. Keine App-Brokerpasswörter,
keine Backend-Credentials im Frontend, keine pauschalen Scale-0/1-Rebuilds.

Die gemeldeten Deployabbrüche betrafen unter anderem Kubernetes-Ausgabe/
Profilabfrage, den alten Registrierungsvertrag, Helm-Ownership einer vorhandenen
ConfigMap, Feldbesitz von `manifest.json` sowie gleichzeitige `value`- und
`valueFrom`-Setter. Korrekturen und Schutzgrenzen sind im Gesamtstand beschrieben.
Ein erneuter Fehler ist gezielt zu diagnostizieren; weder Cluster/PVCs löschen
noch fremde ConfigMaps ungeprüft übernehmen. Image-Import oder ein erfolgreicher
Registrierungsjob allein beweist keinen erfolgreichen Helm-Rollout.

## Anmeldung und Settings

React `0.1.49` stabilisierte Token-Erneuerung/Settings-Entwürfe; `0.1.50`
trennt App-Root, Tenant-Pfad und OIDC-Callback und verhindert identische
Redirectschleifen. Sales nutzt diese gemeinsame Implementierung, keine eigene
Renewal-/Loginlogik. TLS umfasst auch nginx/Kestrel und interne Backchannels;
Entwicklungszertifikate benötigen eine vertrauenswürdige lokale Root-CA.

Die Einladung eines bereits bekannten Entra-Benutzers ist ein anderer Fehler:
die Korrektur liegt in Platform API/Tenant Portal, nicht in Sales oder einem
weiteren Library-Update. Tenant-Mitgliedschaft entsteht erst bei authentifizierter
Annahme; E-Mail-Gleichheit allein darf keine beliebigen Konten verknüpfen.

## Nächste Abnahme

Plattform separat aktualisieren, danach Sales separat deployen und den korrekten
Kontext prüfen. Anschließend HTTPS, Microsoft-Login/Einladung, Tenant-Wechsel,
Settings-Entwürfe bei Token-Erneuerung und Zoho-Callbacks live prüfen. Keine
Produktions-, Backup-/Restore- oder unbeaufsichtigte Pipeline-Freigabe aus den
lokalen Buildresultaten ableiten.
