namespace LiturgicalCalendar

open System
open System.IO
open System.Collections.Generic
open System.Text.Json

/// Module principal pour gérer les données liturgiques avec indexation optimisée
module LiturgicalData =

    // ================================================================
    // ÉTAT DU CALENDRIER AVEC INDEX
    // ================================================================

    /// Structure principale contenant toutes les données liturgiques
    type CalendarData =
        {
            /// Toutes les célébrations indexées par ID
            Celebrations: Map<CelebrationId, LiturgicalCelebration>

            /// Index optimisé pour recherche par date O(1)
            DateIndex: Map<DateKey, IndexedCelebration list>

            /// Métadonnées du calendrier
            Metadata: CalendarMetadata
        }

    and CalendarMetadata =
        {
            /// Nom du calendrier (ex: "Calendrier Romain Général")
            Name: string

            /// Région/pays (ex: "France", "Belgique", "Universal")
            Region: string

            /// Année liturgique de référence
            LiturgicalYear: int

            /// Date de dernière mise à jour
            LastUpdated: DateTime

            /// Version du calendrier
            Version: string
        }

    // ================================================================
    // ÉTAT MUTABLE DU CALENDRIER (Singleton)
    // ================================================================

    /// Instance globale du calendrier (mutable pour les mises à jour)
    let mutable private currentCalendar: CalendarData option = None

    /// Verrou pour la thread-safety
    let private calendarLock = obj ()

    // ================================================================
    // FONCTIONS DE CHARGEMENT
    // ================================================================

    /// Charge un calendrier depuis les données brutes et construit les index
    let loadCalendar
        (celebrations: Map<CelebrationId, LiturgicalCelebration>)
        (metadata: CalendarMetadata)
        : CalendarData =

        // Construction de l'index de dates
        printfn "🏗️ Construction de l'index de dates..."
        let dateIndex = DateIndex.buildDateIndex celebrations

        // Validation de l'index
        let isValid = DateIndex.validateIndex celebrations dateIndex

        if not isValid then
            failwith "❌ L'index de dates n'est pas valide"

        // Statistiques
        let (totalDates, totalCelebrations, datesWithMultiple, _, _) =
            DateIndex.getIndexStats dateIndex

        printfn
            "✅ Index construit : %d dates, %d célébrations, %d dates avec conflits"
            totalDates
            totalCelebrations
            datesWithMultiple

        { Celebrations = celebrations
          DateIndex = dateIndex
          Metadata = metadata }

    /// Initialise le calendrier global depuis un fichier JSON
    let initializeFromJson (jsonFilePath: string) (region: string) : unit =
        lock calendarLock (fun () ->
            try
                // Chargement depuis JSON (vous devrez adapter selon votre LiturgicalJsonLoader)
                printfn "📖 Chargement du calendrier depuis : %s" jsonFilePath

                // Cette partie dépend de votre LiturgicalJsonLoader
                // let rawData = LiturgicalJsonLoader.loadFromFile jsonFilePath
                // let celebrations = rawData |> convertToMap // à implémenter selon votre format

                // Pour l'instant, exemple avec données fictives
                let celebrations = Map.empty<CelebrationId, LiturgicalCelebration>

                let metadata =
                    { Name = sprintf "Calendrier Liturgique - %s" region
                      Region = region
                      LiturgicalYear = DateTime.Now.Year
                      LastUpdated = DateTime.Now
                      Version = "1.0.0" }

                let calendarData = loadCalendar celebrations metadata
                currentCalendar <- Some calendarData

                printfn "✅ Calendrier initialisé pour %s" region

            with ex ->
                printfn "❌ Erreur lors du chargement : %s" ex.Message
                reraise ())

    /// Charge plusieurs calendriers et les fusionne (ex: Général + National + Diocésain)
    let initializeFromMultipleJson (jsonFilePaths: string list) (region: string) : unit =
        lock calendarLock (fun () ->
            try
                let allCelebrations =
                    jsonFilePaths
                    |> List.fold
                        (fun acc filePath ->
                            printfn "📖 Chargement : %s" filePath

                            // VRAI chargement JSON !
                            let jsonContent = File.ReadAllText(filePath)
                            let jsonOptions = JsonSerializerOptions()
                            jsonOptions.PropertyNameCaseInsensitive <- true

                            let jsonCelebrations =
                                JsonSerializer.Deserialize<Dictionary<string, JsonCelebration>>(
                                    jsonContent,
                                    jsonOptions
                                )

                            printfn
                                "  → %d célébrations trouvées dans %s"
                                jsonCelebrations.Count
                                (Path.GetFileName(filePath))

                            // Conversion vers LiturgicalCelebration
                            jsonCelebrations
                            |> Seq.fold
                                (fun acc2 kvp ->
                                    let id = kvp.Key
                                    let jsonCeleb = kvp.Value
                                    let celebration = JsonConverters.convertCelebration id jsonCeleb
                                    Map.add id celebration acc2)
                                acc)
                        Map.empty

                printfn "🔢 TOTAL CÉLÉBRATIONS CHARGÉES : %d" allCelebrations.Count

                // CORRECTION : Syntaxe correcte pour l'initialisation du record
                let metadata =
                    { Name = sprintf "Calendrier Fusionné - %s" region
                      Region = region
                      LiturgicalYear = DateTime.Now.Year
                      LastUpdated = DateTime.Now
                      Version = "1.0.0" }

                let calendarData = loadCalendar allCelebrations metadata
                currentCalendar <- Some calendarData

                printfn "✅ Calendrier fusionné initialisé pour %s (%d sources)" region jsonFilePaths.Length

            with ex ->
                printfn "❌ Erreur lors de la fusion : %s" ex.Message
                printfn "Stack trace : %s" ex.StackTrace
                reraise ())

    // ================================================================
    // API PUBLIQUE DE RECHERCHE
    // ================================================================

    /// Obtient le calendrier actuel ou lève une exception
    let private requireCalendar () : CalendarData =
        match currentCalendar with
        | Some calendar -> calendar
        | None -> failwith "❌ Aucun calendrier n'est chargé. Appelez initializeFromJson d'abord."

    /// Recherche toutes les célébrations pour une date donnée
    /// 🚀 Performance : O(1) grâce à l'index pré-calculé
    let getCelebrationsForDate (month: int) (day: int) : IndexedCelebration list =
        let calendar = requireCalendar ()
        DateIndex.findCelebrationsForDate month day calendar.DateIndex

    /// Recherche toutes les célébrations pour une DateTime
    let getCelebrationsForDateTime (date: DateTime) : IndexedCelebration list =
        getCelebrationsForDate date.Month date.Day

    /// Recherche la célébration principale (plus haute priorité) pour une date
    let getMainCelebrationForDate (month: int) (day: int) : IndexedCelebration option =
        let calendar = requireCalendar ()
        DateIndex.findMainCelebrationForDate month day calendar.DateIndex

    /// Recherche la célébration principale pour une DateTime
    let getMainCelebrationForDateTime (date: DateTime) : IndexedCelebration option =
        getMainCelebrationForDate date.Month date.Day

    /// Recherche par couleur liturgique pour une date donnée
    let getCelebrationsByColor (month: int) (day: int) (color: LiturgicalColor) : IndexedCelebration list =
        let calendar = requireCalendar ()
        DateIndex.findCelebrationsByColor month day color calendar.DateIndex

    /// Recherche par rang liturgique pour une date donnée
    let getCelebrationsByRank (month: int) (day: int) (rank: LiturgicalRank) : IndexedCelebration list =
        let calendar = requireCalendar ()
        DateIndex.findCelebrationsByRank month day rank calendar.DateIndex

    /// Obtient une célébration par son ID
    let getCelebrationById (celebrationId: CelebrationId) : LiturgicalCelebration option =
        let calendar = requireCalendar ()
        calendar.Celebrations.TryFind celebrationId

    /// Obtient toutes les célébrations du calendrier
    let getAllCelebrations () : Map<CelebrationId, LiturgicalCelebration> =
        let calendar = requireCalendar ()
        calendar.Celebrations

    /// Obtient les métadonnées du calendrier actuel
    let getCalendarMetadata () : CalendarMetadata =
        let calendar = requireCalendar ()
        calendar.Metadata

    // ================================================================
    // FONCTIONS DE RECHERCHE AVANCÉE
    // ================================================================

    /// Recherche les célébrations dans une plage de dates
    let getCelebrationsInRange
        (startMonth: int)
        (startDay: int)
        (endMonth: int)
        (endDay: int)
        : (DateKey * IndexedCelebration list) list =
        let calendar = requireCalendar ()
        let startKey = DateIndex.formatDateKey startMonth startDay
        let endKey = DateIndex.formatDateKey endMonth endDay

        calendar.DateIndex
        |> Map.filter (fun dateKey _ -> dateKey >= startKey && dateKey <= endKey)
        |> Map.toList

    /// Recherche toutes les occurrences d'une célébration par nom (recherche partielle)
    let findCelebrationsByName (searchTerm: string) : IndexedCelebration list =
        let calendar = requireCalendar ()
        let lowerSearchTerm = searchTerm.ToLowerInvariant()

        calendar.Celebrations
        |> Map.toSeq
        |> Seq.filter (fun (id, celebration) -> celebration.Name.ToLowerInvariant().Contains(lowerSearchTerm))
        |> Seq.map (fun (id, celebration) -> (id, celebration))
        |> List.ofSeq

    /// Obtient toutes les célébrations d'un rang donné
    let getAllCelebrationsByRank (rank: LiturgicalRank) : IndexedCelebration list =
        let calendar = requireCalendar ()

        calendar.Celebrations
        |> Map.toSeq
        |> Seq.filter (fun (_, celebration) -> celebration.Rank = rank)
        |> Seq.map (fun (id, celebration) -> (id, celebration))
        |> List.ofSeq

    /// Obtient toutes les célébrations d'une couleur donnée
    let getAllCelebrationsByColor (color: LiturgicalColor) : IndexedCelebration list =
        let calendar = requireCalendar ()

        calendar.Celebrations
        |> Map.toSeq
        |> Seq.filter (fun (_, celebration) -> celebration.Color = color)
        |> Seq.map (fun (id, celebration) -> (id, celebration))
        |> List.ofSeq

    // ================================================================
    // UTILITAIRES DE DEBUGGING ET MAINTENANCE
    // ================================================================

    /// Affiche des statistiques complètes sur le calendrier chargé
    let printCalendarStats () : unit =
        match currentCalendar with
        | None -> printfn "❌ Aucun calendrier chargé"
        | Some calendar ->
            printfn "=== STATISTIQUES DU CALENDRIER ==="
            printfn "📋 Nom : %s" calendar.Metadata.Name
            printfn "🌍 Région : %s" calendar.Metadata.Region
            printfn "📅 Année liturgique : %d" calendar.Metadata.LiturgicalYear
            printfn "🔄 Dernière MAJ : %s" (calendar.Metadata.LastUpdated.ToString("yyyy-MM-dd HH:mm"))
            printfn "📦 Version : %s" calendar.Metadata.Version
            printfn ""

            // Statistiques de l'index
            DateIndex.printIndex calendar.DateIndex

    /// Valide la cohérence complète du calendrier
    let validateCalendar () : bool =
        match currentCalendar with
        | None ->
            printfn "❌ Aucun calendrier à valider"
            false
        | Some calendar ->
            printfn "🔍 Validation du calendrier en cours..."
            let isValid = DateIndex.validateIndex calendar.Celebrations calendar.DateIndex

            // Autres validations possibles
            let duplicateNames =
                calendar.Celebrations
                |> Map.toSeq
                |> Seq.groupBy (fun (_, c) -> c.Name)
                |> Seq.filter (fun (_, group) -> Seq.length group > 1)
                |> Seq.length

            if duplicateNames > 0 then
                printfn "⚠️ Attention : %d noms de célébrations en doublon" duplicateNames

            isValid && duplicateNames = 0

    /// Recharge le calendrier (utile après modification des fichiers JSON)
    let reloadCalendar () : unit =
        match currentCalendar with
        | None -> printfn "❌ Aucun calendrier à recharger"
        | Some calendar ->
            printfn "🔄 Rechargement du calendrier..."
            // Cette fonction nécessiterait de mémoriser les paramètres de chargement initial
            // Pour l'instant, juste un message
            printfn "ℹ️ Fonctionnalité à implémenter selon vos besoins"

// ================================================================
// EXEMPLES D'UTILISATION
// ================================================================

module Examples =

    /// Exemple d'utilisation complète de l'API
    let demonstrateApi () =
        printfn "🚀 DÉMONSTRATION DE L'API LITURGIQUE"
        printfn ""

        try
            // 1. Initialisation (à adapter selon vos fichiers JSON)
            // LiturgicalData.initializeFromJson "src/Ressources/calendars/generalRomanCalendar.json" "Universal"

            printfn "📅 RECHERCHES PAR DATE :"

            // 2. Recherche pour une date spécifique
            let today = DateTime.Today
            let celebrationsToday = LiturgicalData.getCelebrationsForDateTime today
            printfn "Célébrations aujourd'hui (%s) : %d" (today.ToString("dd/MM")) celebrationsToday.Length

            // 3. Recherche de la célébration principale
            let mainCelebration = LiturgicalData.getMainCelebrationForDateTime today

            match mainCelebration with
            | Some(id, celebration) -> printfn "👑 Célébration principale : %s" celebration.Name
            | None -> printfn "📅 Aucune célébration principale aujourd'hui"

            // 4. Recherche par couleur
            let redCelebrations =
                LiturgicalData.getCelebrationsByColor today.Month today.Day Rubeus

            printfn "🔴 Célébrations rouges aujourd'hui : %d" redCelebrations.Length

            // 5. Recherche par nom
            let marieCelebrations = LiturgicalData.findCelebrationsByName "Marie"
            printfn "👸 Célébrations de Marie : %d trouvées" marieCelebrations.Length

            // 6. Statistiques
            LiturgicalData.printCalendarStats ()

        with ex ->
            printfn "❌ Erreur dans la démonstration : %s" ex.Message
