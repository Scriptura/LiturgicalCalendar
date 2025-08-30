namespace LiturgicalCalendar

open System
open LiturgicalCalendar.EasterCalculation
open LiturgicalCalendar.LiturgicalJsonLoader

module Program =

    // Fonction pour parser l'année depuis argv
    let parseYear (argv: string[]) : Result<int, string> =
        match argv with
        | [||] ->
            // Aucun argument -> année courante par défaut
            Ok DateTime.Now.Year
        | [| yearStr |] ->
            // Un argument -> tenter de parser l'année
            match Int32.TryParse(yearStr) with
            | true, year when year >= 1 && year <= 9999 -> Ok year
            | true, year -> Error $"Année invalide: {year}. L'année doit être entre 1 et 9999."
            | false, _ -> Error $"Format invalide: '{yearStr}' n'est pas un nombre."
        | _ ->
            // Trop d'arguments
            Error "Trop d'arguments. Usage: dotnet run [année]"

    // Fonction d'affichage de l'aide
    let displayUsage () =
        printfn ""
        printfn "📖 USAGE:"
        printfn "   dotnet run              # Utilise l'année courante (%d)" DateTime.Now.Year
        printfn "   dotnet run 2025         # Utilise l'année spécifiée"
        printfn "   dotnet run --help       # Affiche cette aide"
        printfn ""
        printfn "📝 EXEMPLES:"
        printfn "   dotnet run 2024         # Pâques 2024"
        printfn "   dotnet run 2030         # Pâques 2030"
        printfn ""

    let displayError (error: LiturgicalError) =
        match error with
        | FileNotFound path -> printfn "❌ Fichier introuvable: %s" path
        | InvalidJson msg -> printfn "❌ JSON invalide: %s" msg
        | MissingKey key -> printfn "❌ Clé liturgique manquante: %s" key
        | EasterCalculationError msg -> printfn "❌ Erreur calcul Pâques: %s" msg
        | UnknownLiturgicalColor color -> printfn "❌ Couleur liturgique inconnue: %s" color
        | UnknownLiturgicalRank rank -> printfn "❌ Rang liturgique inconnu: %s" rank

    let createLiturgicalDate (key: string) (info: LiturgicalInfo) (date: DateTime) (year: int) =
        { Info = info
          Date = date
          Year = year
          Key = key }

    let displayEasterInfo (paques: LiturgicalDate) =
        printfn ""
        printfn "🌟 ═══════════════════════════════════════════════════"
        printfn "   CALENDARIUM LITURGICUM - DOMINICA RESURRECTIONIS %d" paques.Year
        printfn "═══════════════════════════════════════════════════"
        printfn ""

        printfn
            "📅 Dies        : %s"
            (paques.Date.ToString("dddd dd MMMM yyyy", System.Globalization.CultureInfo("fr-FR")))

        printfn "🏷️  Nomen       : %s" paques.Info.Name
        printfn "🎨 Color       : %s (%s)" paques.Info.Color paques.ColorFrench
        printfn "⭐ Gradus      : %s (%s)" (paques.Info.Rank |> Option.defaultValue "Non spécifié") paques.RankFrench
        printfn "🔑 Clavis      : %s" paques.Key

        match paques.Info.Extra with
        | Some extra -> printfn "ℹ️  Additio     : %s" extra
        | None -> ()

        // Informations supplémentaires sur l'année
        let isLeapYear = DateTime.IsLeapYear(paques.Year)

        printfn
            "📊 Anno        : %s"
            (if isLeapYear then
                 "bisextilis (bissextile)"
             else
                 "ordinarius")

        match paques.Color, paques.Rank with
        | Some color, Some rank ->
            printfn ""
            printfn "📊 Typi liturgici:"
            printfn "   Color: %A (%s)" color (color.ToLatin())
            printfn "   Gradus: %A (%s)" rank (rank.ToLatin())
        | _ -> ()

        printfn ""
        printfn "═══════════════════════════════════════════════════"

    [<EntryPoint>]
    let main argv =
        // Vérification de l'aide
        if argv |> Array.contains "--help" || argv |> Array.contains "-h" then
            displayUsage ()
            0
        else
            // Parsing de l'année
            match parseYear argv with
            | Error errorMsg ->
                printfn "❌ %s" errorMsg
                displayUsage ()
                1 // Code d'erreur
            | Ok annee ->
                let jsonPath = "Ressources/calendarium_romanum/de_tempore.json"
                let easterKey = "dominicaResurrectionis"

                printfn "🔍 Recherche des informations liturgiques pour l'année %d..." annee

                match
                    EasterCalculation.calculateEaster CalendarType.Gregorian annee,
                    LiturgicalJsonLoader.loadJsonFile jsonPath
                with

                | Ok paquesDate, Ok liturgicalData ->
                    match liturgicalData |> Map.tryFind easterKey with
                    | Some paquesInfo ->
                        let paques = createLiturgicalDate easterKey paquesInfo paquesDate annee
                        displayEasterInfo paques
                        0 // Succès
                    | None ->
                        displayError (MissingKey easterKey)
                        1

                | Error msg, _ ->
                    displayError (EasterCalculationError msg)
                    1

                | _, Error jsonError ->
                    displayError jsonError
                    1
