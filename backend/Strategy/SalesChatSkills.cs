using System.ComponentModel;
using ModelContextProtocol.Server;

namespace SalesPlattform.Backend.Strategy;

[McpServerPromptType]
public sealed class SalesChatSkills
{
    internal const string TaskInstructions = """
        Nutze sales_list_tasks für offene Aufgaben. Ohne Personen- oder Teamfilter bekommst du die eigenen Aufgaben.
        Benannte Teams suchst du mit sales_find_people. Bei „Welche Aufgaben hat Antonio Treglia?“ rufst du direkt
        sales_list_user_tasks mit firstName Antonio und lastName Treglia auf. Benannte Personen für Zuweisungen
        suchst du mit sales_find_users. Verwende die gelieferten IDs selbst. Frage niemals nach internen IDs
        oder nach Erlaubnis für die Suche. Bei mehreren Treffern zeige die Personen zur Auswahl.
        """;

    internal const string AssignmentInstructions = """
        Suche den Empfänger per Name und die genannten Aufgaben mit sales_list_tasks, search=<Titel> und
        includeAvailable=true. Sind die Aufgaben offen
        gelassen, rufe sales_list_tasks mit onlyUnassigned=true auf und zeige eine kurze nummerierte Liste zur Auswahl.
        Frage in diesem Fall, welche davon zugewiesen werden sollen. Nutze die gelieferten Aufgaben-IDs intern.
        Erstelle erst bei einem ausdrücklichen Auftrag und eindeutig gefundenen Aufgaben einen Vorschlag mit
        sales_prepare_assignment. Erwähne, dass er bestätigt werden muss. Behaupte nie, die Aufgabe sei schon zugewiesen.
        Im Sales-Chat bestätigt der Benutzer im Freigabedialog. Ein externer MCP-Client muss dem Benutzer denselben
        konkreten Vorschlag zeigen und dessen ausdrückliche Zustimmung einholen, bevor er sales_apply_change aufruft.
        """;

    internal const string DailyPlanInstructions = """
        Nutze sales_daily_plan_options für das aktuelle Datum, feste Aufgabentypen und verfügbare Branchen.
        Kläre die Stückzahlen und den Branchenfilter. Fehlende Mengen nur dann als null Stück eintragen, wenn der
        Benutzer klar macht, dass ausschließlich die genannten Typen verteilt werden sollen. Ein leerer Branchenfilter
        gilt für alle Branchen. Das Tagesprofil gilt für den ganzen Mandanten. Wenn nur ein Team oder eine Person
        gemeint ist, frage nach, bevor du ein mandantenweites Profil vorschlägst. Erstelle den Vorschlag mit
        sales_prepare_daily_plan und weise auf die nötige Bestätigung hin.
        """;

    [McpServerPrompt(Name = "sales_tasks"), Description("Offene Sales-Aufgaben nach Team oder Person abfragen.")]
    public static string Tasks() => TaskInstructions;

    [McpServerPrompt(Name = "sales_assignment"), Description("Konkrete Sales-Aufgaben zur Zuweisung vorbereiten.")]
    public static string Assignment() => AssignmentInstructions;

    [McpServerPrompt(Name = "sales_daily_plan"), Description("Ein mandantenweites Sales-Tagesprofil vorbereiten.")]
    public static string DailyPlan() => DailyPlanInstructions;
}
