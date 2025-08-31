namespace LiturgicalCalendar

open System.IO
open System.Text.Json
open System.Text.Json.Serialization
open FsToolkit.ErrorHandling

module LiturgicalJsonLoader =

    // Configuration du désérialiseur JSON pour le support F# et la conversion du nommage
    let private jsonOptions =
        let options = JsonSerializerOptions()
        options.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
        options.PropertyNameCaseInsensitive <- true
        options.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull
        options

    // Fonction pour lire le contenu d'un fichier en Result

    let private readFile (path: string) : Result<string, LiturgicalError> =
        try
            match File.Exists(path) with
            | true -> Ok(File.ReadAllText(path))
            | false -> Error(FileNotFound path)
        with ex ->
            Error(InvalidJson $"Erreur de lecture: {ex.Message}")

    // Fonction pour parser une chaîne JSON en Result
    let private parseJson (jsonString: string) : Result<LiturgicalData, LiturgicalError> =
        try
            let data = JsonSerializer.Deserialize<LiturgicalData>(jsonString, jsonOptions)
            Ok data
        with ex ->
            Error(InvalidJson $"JSON invalide: {ex.Message}")

    // Validation des données liturgiques
    let private validateLiturgicalData (data: LiturgicalData) : Result<LiturgicalData, LiturgicalError> =
        let invalidColors =
            data
            |> Map.toSeq
            |> Seq.choose (fun (key, info) ->
                match LiturgicalColor.FromLatin(info.Color) with
                | None -> Some(key, info.Color)
                | Some _ -> None)
            |> Seq.toList

        let invalidRanks =
            data
            |> Map.toSeq
            |> Seq.choose (fun (key, info) ->
                match info.Rank with
                | Some rank ->
                    match LiturgicalRank.FromLatin(rank) with
                    | None -> Some(key, rank)
                    | Some _ -> None
                | None -> None // On accepte les rangs null/None comme valides
            )
            |> Seq.toList

        match invalidColors, invalidRanks with
        | [], [] -> Ok data
        | (_, color) :: _, _ -> Error(UnknownLiturgicalColor color)
        | _, (_, rank) :: _ -> Error(UnknownLiturgicalRank rank)

    // Fonction principale pour charger un fichier JSON de manière sécurisée
    let loadJsonFile (path: string) : Result<LiturgicalData, LiturgicalError> =
        result {
            let! jsonString = readFile path
            let! data = parseJson jsonString
            let! validatedData = validateLiturgicalData data
            return validatedData
        }
