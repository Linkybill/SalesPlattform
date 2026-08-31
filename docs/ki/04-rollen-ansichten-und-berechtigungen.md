# Rollen, Ansichten und Berechtigungen

## Nutzergruppen

Das Pflichtenheft unterscheidet fachlich:

- Vertrieb / Besitzer eines Vorgangs,
- Vertriebsleitung,
- Management bzw. Geschäftsführung,
- Backoffice für die Bereinigung.

Im technischen Startgerüst existiert zunächst nur die Identity-Platform-App-
Rolle `sales-user`. Die fachliche Differenzierung der Rechte ist Zielumfang
und darf nicht durch das Vorhandensein dieser einen Startrolle als erledigt
gelten.

## Ansichtsmatrix

| Ansicht | Vertrieb | Vertriebsleitung | Management/GF | Backoffice |
|---|---|---|---|---|
| Meine Arbeitsliste | eigene, Team umschaltbar | Team und eigene | nach Freigabe | nach Aufgabe |
| Cockpit | nicht standardmäßig | ja | ja | nein |
| Team-Steuerung | Lesesicht nach Entscheidung | ja | ja | nein |
| Meeting Report | nach Entscheidung | ja | ja | nein |
| Analyse | eingeschränkt/nach Entscheidung | ja | ja | nein |
| Kundenstamm/Karte | eingeschränkt/nach Entscheidung | ja | ja | nein |
| Ziele und Pace | teamweit sichtbar | ja | ja | nein |
| Aufräumen | nein | ja | ja | ja |

Die exakte technische Rollenmatrix ist vor der Umsetzung der Fachansichten zu
bestätigen. „Teamweit sichtbar“ bedeutet, dass alle Mitarbeiter Ziele und
Zielerreichungen sehen dürfen; die Arbeitsliste bleibt standardmäßig auf den
jeweiligen Besitzer gefiltert.

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
