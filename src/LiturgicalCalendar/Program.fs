// Program.fs
namespace LiturgicalCalendar

module Program =

    open System
    open System.IO

    // Importez les modules nécessaires
    open LiturgicalCalendar.LiturgicalData

    [<EntryPoint>]
    let main argv =
        try
            let resourcesPath = Path.Combine(__SOURCE_DIRECTORY__, "Ressources", "calendars")

            let paths =
                [ Path.Combine(resourcesPath, "generalRomanCalendar.json")
                  Path.Combine(resourcesPath, "europeRomanCalendar.json")
                  Path.Combine(resourcesPath, "franceRomanCalendar.json") ]

            (*
            // Debug : vérifier le contenu brut du JSON
            let testJsonPath = Path.Combine(resourcesPath, "generalRomanCalendar.json")
            let jsonContent = File.ReadAllText(testJsonPath)
            printfn "\n--- DEBUG JSON BRUT ---"
            printfn "Taille du fichier : %d caractères" jsonContent.Length
            printfn "Contient 'nativitatisDomini' : %b" (jsonContent.Contains("nativitatisDomini"))
            printfn "Premiers 200 caractères : %s" (jsonContent.Substring(0, min 200 jsonContent.Length))
            *)

            // 1. Initialisation du calendrier avec les fichiers spécifiques à la France
            LiturgicalData.initializeFromMultipleJson paths "France"

            // 2. Recherche et affichage pour le 25 décembre
            printfn "\n--- RECHERCHE DU 25 DÉCEMBRE ---"
            let mainCelebration = LiturgicalData.getMainCelebrationForDate 12 25

            match mainCelebration with
            | Some(id, celebration) ->
                printfn "🎉 Célébration principale le 25 décembre: %s (%s)" celebration.Name id
                printfn "  - Rang : %A" celebration.Rank
                printfn "  - Couleur : %A" celebration.Color
            | None -> printfn "❌ Aucune célébration trouvée le 25 décembre."

            // 3. Affichage des statistiques pour validation
            printfn "\n--- STATISTIQUES DU CALENDRIER ---"
            LiturgicalData.printCalendarStats ()

            0 // Code de sortie

        with ex ->
            printfn "Erreur fatale : %s" ex.Message
            1 // Code d'erreur
