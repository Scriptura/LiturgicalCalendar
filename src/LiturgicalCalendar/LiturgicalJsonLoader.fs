namespace LiturgicalCalendar

open System
open System.IO
open System.Text.Json
open LiturgicalCalendar.DateIndex

module LiturgicalJsonLoader =

    /// Type intermédiaire pour désérialisation brute
    type LiturgicalCelebrationRaw =
        { Month: int
          Day: int
          Name: string
          Color: string
          Rank: string
          Priority: int }

    /// Conversion string -> LiturgicalColor
    let private parseColor =
        function
        | "albus" -> LiturgicalColor.Albus
        | "rubeus" -> LiturgicalColor.Rubeus
        | "viridis" -> LiturgicalColor.Viridis
        | "violaceus" -> LiturgicalColor.Violaceus
        | "roseus" -> LiturgicalColor.Roseus
        | "niger" -> LiturgicalColor.Niger
        | other -> invalidArg "Color" $"Couleur inconnue : {other}"

    /// Conversion string -> LiturgicalRank
    let private parseRank =
        function
        | "sollemnitas" -> LiturgicalRank.Sollemnitas
        | "dominica" -> LiturgicalRank.Dominica
        | "festum" -> LiturgicalRank.Festum
        | "memoria" -> LiturgicalRank.Memoria
        | "memoriaAdLibitum" -> LiturgicalRank.MemoriaAdLibitum
        | "feriaOrdinis" -> LiturgicalRank.FeriaOrdinis
        | other -> invalidArg "Rank" $"Rang inconnu : {other}"

    /// Charge un fichier JSON et retourne les célébrations typées
    let loadCelebrations (path: string) : LiturgicalCelebration list =
        let json = File.ReadAllText path
        let options = JsonSerializerOptions(PropertyNameCaseInsensitive = true)

        let dict =
            JsonSerializer.Deserialize<Map<string, LiturgicalCelebrationRaw>>(json, options)

        dict
        |> Map.toList
        |> List.map (fun (key, raw) ->
            { Id = key
              Month = raw.Month
              Day = raw.Day
              Name = raw.Name
              Color = parseColor raw.Color
              Rank = parseRank raw.Rank
              Priority = LiturgicalPrecedence.Create(raw.Priority) })
