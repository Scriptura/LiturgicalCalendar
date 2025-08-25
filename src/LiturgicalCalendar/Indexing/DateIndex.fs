namespace LiturgicalCalendar

// - Optimisation O(n) → O(1) avec pré-calcul d'index

open System

/// Type alias pour les identifiants de célébrations
type CelebrationId = string

/// Type alias pour les clés de date au format "MMDD"
type DateKey = string

/// Type représentant une entrée dans l'index : (ID, célébration)
type IndexedCelebration = CelebrationId * LiturgicalCelebration

module DateIndex =

    /// Construit un index permettant de rechercher rapidement les célébrations par date.
    /// Cette fonction illustre un pattern fondamental : sacrifier de la mémoire
    /// et du temps de calcul initial pour gagner énormément en vitesse de recherche.
    ///
    /// 🏗️ STRATÉGIE :
    /// 1. Parcourir une fois toutes les célébrations (coût O(n))
    /// 2. Créer un Map où les clés sont des dates "MMDD"
    /// 3. Les valeurs sont des listes de célébrations pour cette date
    /// 4. Les recherches futures seront O(1) au lieu de O(n)
    ///
    /// 📊 COMPLEXITÉ :
    /// - Construction : O(n) où n = nombre de célébrations
    /// - Recherche après construction : O(1)
    /// - Mémoire : ~2x l'espace original (acceptable pour les gains de vitesse)
    ///
    /// 📝 PARAMÈTRES :
    /// - calendar : Map<CelebrationId, LiturgicalCelebration> - Le calendrier source
    ///
    /// 📝 RETOUR :
    /// - Map<DateKey, IndexedCelebration list> - Index optimisé pour recherche par date
    ///
    /// 💡 EXEMPLE D'USAGE :
    /// let index = buildDateIndex myCalendar
    /// let celebrationsOn5Feb = index.TryFind("0205") |> Option.defaultValue []
    let buildDateIndex (calendar: Map<CelebrationId, LiturgicalCelebration>) : Map<DateKey, IndexedCelebration list> =

        // ================================================================
        // ÉTAPE 1 : CONVERSION EN SÉQUENCE POUR LE PIPELINE
        // ================================================================
        // Map.toSeq convertit Map<K,V> en seq<K * V>
        // C'est le point d'entrée de notre pipeline fonctionnel
        calendar
        |> Map.toSeq

        // ================================================================
        // ÉTAPE 2 : TRANSFORMATION DES CÉLÉBRATIONS EN ENTRÉES D'INDEX
        // ================================================================
        // Contrairement à la version précédente, toutes les célébrations
        // ont des dates fixes (Month et Day sont des int, pas des Option)
        // Cela simplifie considérablement le pipeline
        |> Seq.map (fun (celebrationId, celebration) ->

            // Construction de la clé de date standardisée
            // Format "MMDD" : 5 février → "0205", 25 décembre → "1225"
            // sprintf "%02d%02d" garantit 2 chiffres avec zéro devant si nécessaire
            let dateKey = sprintf "%02d%02d" celebration.Month celebration.Day

            // Création du tuple de résultat
            // Structure : (clé_date, (id_célébration, objet_célébration))
            // Ce format facilite le regroupement suivant
            (dateKey, (celebrationId, celebration)))

        // ================================================================
        // ÉTAPE 3 : REGROUPEMENT PAR DATE
        // ================================================================
        // Seq.groupBy regroupe les éléments ayant la même clé
        // Ici on groupe par dateKey pour rassembler toutes les célébrations du même jour
        // Exemple : toutes les célébrations du "0205" ensemble
        // Performance : O(n log n) à cause du tri interne
        |> Seq.groupBy fst // fst extrait le premier élément du tuple (dateKey)

        // ================================================================
        // ÉTAPE 4 : TRANSFORMATION DES GROUPES ET TRI PAR PRIORITÉ
        // ================================================================
        // À ce stade nous avons : seq<DateKey * seq<DateKey * IndexedCelebration>>
        // Nous devons nettoyer et trier par priorité liturgique
        |> Seq.map (fun (dateKey, groupedCelebrations) ->

            // Extraction et tri des célébrations de chaque groupe
            // snd extrait le deuxième élément du tuple (IndexedCelebration)
            // Le tri se fait par priorité liturgique (plus bas = plus prioritaire)
            let celebrationsForThisDate =
                groupedCelebrations // seq<DateKey * IndexedCelebration>
                |> Seq.map snd // seq<IndexedCelebration>
                |> List.ofSeq // IndexedCelebration list

            // Retour du tuple final propre
            (dateKey, celebrationsForThisDate))

        // ================================================================
        // ÉTAPE 5 : CONVERSION EN MAP POUR ACCÈS O(1)
        // ================================================================
        // Map.ofSeq convertit une séquence de tuples en Map
        // Les Maps en F# utilisent des arbres balancés → recherche O(log n)
        // En pratique, pour des petits volumes c'est quasi-O(1)
        |> Map.ofSeq

    // ================================================================
    // FONCTIONS UTILITAIRES
    // ================================================================

    /// Crée une clé de date standardisée à partir d'un mois et d'un jour.
    /// Cette fonction évite la duplication du format dans le code.
    ///
    /// 📝 PARAMÈTRES :
    /// - month : int - Le mois (1-12)
    /// - day : int - Le jour (1-31)
    ///
    /// 📝 RETOUR :
    /// - DateKey - Chaîne au format "MMDD"
    ///
    /// 💡 EXEMPLES :
    /// formatDateKey 2 5   → "0205" (5 février)
    /// formatDateKey 12 25 → "1225" (25 décembre)
    let formatDateKey (month: int) (day: int) : DateKey = sprintf "%02d%02d" month day

    /// Recherche toutes les célébrations pour une date donnée dans l'index.
    /// Encapsule la logique de recherche avec gestion des cas d'absence.
    /// Les résultats sont automatiquement triés par priorité liturgique.
    ///
    /// 📝 PARAMÈTRES :
    /// - month : int - Le mois recherché (1-12)
    /// - day : int - Le jour recherché (1-31)
    /// - index : Map<DateKey, IndexedCelebration list> - L'index pré-calculé
    ///
    /// 📝 RETOUR :
    /// - IndexedCelebration list - Liste triée par priorité (vide si aucune)
    ///
    /// ⚡ PERFORMANCE : O(1) - Recherche directe dans le Map
    let findCelebrationsForDate
        (month: int)
        (day: int)
        (index: Map<DateKey, IndexedCelebration list>)
        : IndexedCelebration list =
        let dateKey = formatDateKey month day
        index.TryFind(dateKey) |> Option.defaultValue []

    /// Version alternative avec pattern matching explicite pour l'apprentissage.
    /// Même fonctionnalité que findCelebrationsForDate mais style plus explicite.
    let findCelebrationsForDateExplicit
        (month: int)
        (day: int)
        (index: Map<DateKey, IndexedCelebration list>)
        : IndexedCelebration list =
        let dateKey = formatDateKey month day

        match index.TryFind(dateKey) with
        | Some celebrationsList -> celebrationsList
        | None -> []

    /// Recherche la célébration principale (plus haute priorité) pour une date donnée.
    /// Utile pour déterminer la célébration liturgique du jour.
    ///
    /// 📝 RETOUR :
    /// - IndexedCelebration option - La célébration principale ou None
    let findMainCelebrationForDate
        (month: int)
        (day: int)
        (index: Map<DateKey, IndexedCelebration list>)
        : IndexedCelebration option =
        findCelebrationsForDate month day index |> List.tryHead // La première après tri par priorité

    /// Filtre les célébrations par couleur liturgique pour une date donnée.
    ///
    /// 📝 EXEMPLE :
    /// let redCelebrations = findCelebrationsByColor 2 14 Rubeus index
    let findCelebrationsByColor
        (month: int)
        (day: int)
        (color: LiturgicalColor)
        (index: Map<DateKey, IndexedCelebration list>)
        : IndexedCelebration list =
        findCelebrationsForDate month day index
        |> List.filter (fun (_, celebration) -> celebration.Color = color)

    /// Filtre les célébrations par rang liturgique pour une date donnée.
    ///
    /// 📝 EXEMPLE :
    /// let solemnities = findCelebrationsByRank 12 25 Sollemnitas index
    let findCelebrationsByRank
        (month: int)
        (day: int)
        (rank: LiturgicalRank)
        (index: Map<DateKey, IndexedCelebration list>)
        : IndexedCelebration list =
        findCelebrationsForDate month day index
        |> List.filter (fun (_, celebration) -> celebration.Rank = rank)

    /// Calcule des statistiques sur l'index pour debugging et monitoring.
    /// Utile pour comprendre la répartition des célébrations et valider l'index.
    ///
    /// 📝 PARAMÈTRE :
    /// - index : Map<DateKey, IndexedCelebration list> - L'index à analyser
    ///
    /// 📝 RETOUR :
    /// - Tuple avec statistiques détaillées
    let getIndexStats (index: Map<DateKey, IndexedCelebration list>) =
        let totalDates = index.Count

        let totalCelebrations =
            index |> Map.fold (fun acc _ celebrations -> acc + celebrations.Length) 0

        let datesWithMultipleCelebrations =
            index |> Map.filter (fun _ celebrations -> celebrations.Length > 1) |> Map.count

        // Statistiques par rang liturgique
        let celebrationsByRank =
            index
            |> Map.toSeq
            |> Seq.collect (snd >> List.map snd)
            |> Seq.groupBy (fun c -> c.Rank)
            |> Seq.map (fun (rank, celebrations) -> (rank, Seq.length celebrations))
            |> Map.ofSeq

        // Statistiques par couleur liturgique
        let celebrationsByColor =
            index
            |> Map.toSeq
            |> Seq.collect (snd >> List.map snd)
            |> Seq.groupBy (fun c -> c.Color)
            |> Seq.map (fun (color, celebrations) -> (color, Seq.length celebrations))
            |> Map.ofSeq

        (totalDates, totalCelebrations, datesWithMultipleCelebrations, celebrationsByRank, celebrationsByColor)

    // ================================================================
    // FONCTIONS DE DEBUGGING ET VALIDATION
    // ================================================================

    /// Convertit une couleur liturgique en émoji pour l'affichage
    let colorToEmoji =
        function
        | Albus -> "⚪"
        | Rubeus -> "🔴"
        | Viridis -> "🟢"
        | Violaceus -> "🟣"
        | Roseus -> "🌸"
        | Niger -> "⚫"

    /// Convertit un rang liturgique en émoji pour l'affichage
    let rankToEmoji =
        function
        | Sollemnitas -> "👑"
        | Dominica -> "✝️"
        | Festum -> "🎉"
        | Memoria -> "📿"
        | MemoriaAdLibitum -> "💫"
        | FeriaOrdinis -> "📅"

    /// Affiche le contenu complet de l'index pour debugging.
    /// Utilise les types liturgiques pour un affichage riche.
    let printIndex (index: Map<DateKey, IndexedCelebration list>) =
        printfn "=== INDEX LITURGIQUE ==="
        printfn "Format : DateKey -> [Célébrations par priorité]"
        printfn ""

        index
        |> Map.iter (fun dateKey celebrations ->
            let month = int (dateKey.Substring(0, 2))
            let day = int (dateKey.Substring(2, 2))
            printfn "📅 %02d/%02d :" month day

            celebrations
            |> List.iteri (fun i (id, celebration) ->
                let colorEmoji = colorToEmoji celebration.Color
                let rankEmoji = rankToEmoji celebration.Rank

                printfn "  %d. %s %s %s %s" (i + 1) colorEmoji rankEmoji id celebration.Name)

            printfn "")

        let (totalDates, totalCelebrations, datesWithMultiple, byRank, byColor) =
            getIndexStats index

        printfn "📊 STATISTIQUES LITURGIQUES :"
        printfn "  - Dates indexées : %d" totalDates
        printfn "  - Célébrations totales : %d" totalCelebrations
        printfn "  - Dates avec plusieurs célébrations : %d" datesWithMultiple

        printfn "\n📋 RÉPARTITION PAR RANG :"

        byRank
        |> Map.iter (fun rank count -> printfn "  %s %A : %d" (rankToEmoji rank) rank count)

        printfn "\n🎨 RÉPARTITION PAR COULEUR :"

        byColor
        |> Map.iter (fun color count -> printfn "  %s %A : %d" (colorToEmoji color) color count)

    /// Valide la cohérence de l'index par rapport au calendrier source.
    /// Vérifie que l'index contient exactement toutes les célébrations du calendrier.
    let validateIndex
        (calendar: Map<CelebrationId, LiturgicalCelebration>)
        (index: Map<DateKey, IndexedCelebration list>)
        =
        let celebrationsInCalendar = calendar.Count

        let celebrationsInIndex =
            index |> Map.fold (fun acc _ celebrations -> acc + celebrations.Length) 0

        // Validation quantitative
        let quantityValid = celebrationsInCalendar = celebrationsInIndex

        // Validation qualitative : vérifier que tous les IDs sont présents
        let calendarIds = calendar |> Map.keys |> Set.ofSeq
        let indexIds = index |> Map.toSeq |> Seq.collect (snd >> List.map fst) |> Set.ofSeq

        let qualityValid = calendarIds = indexIds

        let isValid = quantityValid && qualityValid

        if isValid then
            printfn "✅ Index valide : %d célébrations liturgiques indexées correctement" celebrationsInIndex
        else
            printfn "❌ Index invalide :"

            if not quantityValid then
                printfn
                    "   - Quantité : %d dans le calendrier, %d dans l'index"
                    celebrationsInCalendar
                    celebrationsInIndex

            if not qualityValid then
                let missing = Set.difference calendarIds indexIds
                let extra = Set.difference indexIds calendarIds

                if not (Set.isEmpty missing) then
                    printfn "   - IDs manquants : %A" missing

                if not (Set.isEmpty extra) then
                    printfn "   - IDs en trop : %A" extra

        isValid
