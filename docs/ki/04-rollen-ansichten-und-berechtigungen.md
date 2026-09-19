# Rollen, Ansichten und Berechtigungen

## Nutzergruppen

Das Pflichtenheft unterscheidet fachlich:

- Vertrieb / Besitzer eines Vorgangs,
- Vertriebsleitung,
- Management bzw. Geschäftsführung,
- Backoffice für die Bereinigung.

Technisch gibt es die Identity-Platform-App-Rollen `sales-user` für den
Vertrieb, `sales-manager` für die Vertriebsleitung, `sales-management` für
Management/Geschäftsführung und `sales-backoffice` für das Backoffice.

## Ansichtsmatrix

| Ansicht | Vertrieb | Vertriebsleitung | Management/GF | Backoffice |
|---|---|---|---|---|
| Meine Arbeitsliste | eigene, Team umschaltbar | alle Vorgänge des Tenants sowie eigene | nach Freigabe | nach Aufgabe |
| Cockpit | ja, lesen | ja | ja | nein |
| Team-Steuerung | ja, lesen | ja | ja | nein |
| Meeting Report | ja, lesen | ja | ja | nein |
| Analyse | ja, lesen | ja | ja | nein |
| Kundenstamm/Karte | ja, lesen | ja | ja | nein |
| Ziele und Pace | teamweit sichtbar | ja | ja | nein |
| Aufräumen | nein | ja | ja | ja |

Die Vertriebsleitung erhält für die erste Arbeitsliste eine serverseitig
erzwungene tenantweite Ansicht. Normale Vertriebsbenutzer bleiben auf ihren
CRM-Besitzer und nicht zugeordnete Vorgänge begrenzt.

Die erweiterte Report-Lesesicht ist die ausdrückliche Gesprächsentscheidung
vom 19.09.2026. Sie gilt innerhalb des aktiven Tenants, auch für die zugehörigen
Datensatznachweise. Sie erweitert weder Arbeitslisten-Schreibrechte noch
Layout-/Benutzerverwaltung oder Bereinigungsaktionen. Kennzahlnachweise werden
serverseitig mit denselben Rollenfreigaben wie die Reports projiziert.

Die erste Arbeitslisten-API setzt diesen Grundsatz serverseitig um. Ein CRM-
Besitzer wird über die E-Mail des authentifizierten Plattform-Benutzers gesucht;
ein Benutzer ohne Zuordnung erhält nur nicht zugeordnete Vorgänge und eine
transparente Hinweismeldung. Ein Benutzer kann fremde, besitzerbezogene
Vorgänge auch über eine direkt bekannte ID nicht erledigen oder zurückstellen.
Eine explizite tenantbezogene Zuordnung in der Sales-App hat Vorrang vor dem
E-Mail-Fallback. Sie wird ausschließlich durch Tenant-Administratoren gepflegt
und verbindet die stabile Plattform-Subject-ID mit einer synchronisierten
`SalesOwner`-ID. Die Vertriebsleitung benötigt für die tenantweite Ansicht kein
persönliches Mapping.

## Direkt bearbeitbare Reportseite

Die fachlichen Reports sind eigenständige Komponenten in einem gemeinsamen
Seitenbaum. Tenant-Admins bearbeiten die Seite direkt über den Button
„Layout bearbeiten“: Sie können Grid, Tabs, Akkordeon, Überschrift und
Text hinzufügen, bei Tabs/Akkordeons Abschnitte anlegen und benennen und jeden
Report an beliebiger Stelle platzieren. Die Sales-App speichert den Baum als
internes JSON über `PUT /api/reports/layout`; es gibt dafür keine rohe
Webpart- oder Layout-Einstellung im Mandantenportal. Das Defaultmodell enthält
alle Reports, einschließlich Servicefälle sowie Angebote/Aufträge/Rechnungen.

Die normale Ansicht filtert den bestehenden Seitenbaum auf das links gewählte
Thema. Container, Begleittexte, Sichtbarkeit und Rollenfreigabe bleiben erhalten;
ausgeblendete Vorfahren können nicht über die Navigation umgangen werden.
Gespeicherte Layouts werden nicht automatisch überschrieben. Der Editor zeigt
weiterhin den gesamten Baum; ausgeblendete Reports werden als solche erklärt.

Die Serverantwort enthält für jeden Report die effektive Rollenfreigabe. Ein
nicht freigegebener Report wird nicht gerendert. Die UI-Komposition ersetzt
keine Backend-Autorisierung.

## Schreib- und Freigabegrenzen

- Wiedervorlagen entstehen und ändern sich in der SalesPlattform.
- Ein vorgeschlagener Besitzerwechsel wird nur nach Entscheidung der Leitung
  ausgeführt und mit altem Besitzer, neuem Besitzer, Zeitpunkt und Regel
  protokolliert.
- Die Plattform ändert Deal-, Lead- oder Kundenstatus nicht automatisch, wenn
  das Pflichtenheft eine Freigabe verlangt.
- Dubletten werden niemals automatisch zusammengeführt. „Kein Duplikat“ ist
  eine persistente Entscheidung.
- Bei aktiviertem Zoho-Rückschreiben wird ein Merge im CRM vollzogen; die
  Plattform synchronisiert anschließend.
- Erledigte Wiedervorlagen, protokollierte Anrufe und Besitzerwechsel sind die
  ausdrücklich genannten optionalen Rückschreibefälle.

## Identität und Mandantentrennung

Jeder geschützte Backend-Endpunkt muss die Identity Platform verwenden. Die
Tenant-ID wird aus dem authentifizierten Kontext genommen; Datenzugriffe müssen
tenant-isoliert sein. Keine Funktion darf Daten allein anhand einer vom Client
übergebenen Tenant-ID freigeben.

## Sichtbarkeit als Sicherheitsanforderung

Filter in der Oberfläche sind keine ausreichende Zugriffskontrolle. Besitzer-
und Rollenfilter müssen im Backend bzw. Datenzugriff durchgesetzt werden. Die
spätere Teamansicht braucht daher eine explizite Berechtigung und darf nicht
aus einer persönlichen Liste durch bloßes Entfernen eines UI-Filters entstehen.
