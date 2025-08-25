namespace LiturgicalCalendar

open System.Text.Json.Serialization

/// Couleurs liturgiques en latin (forme canonique)
type LiturgicalColor =
    | Albus
    | Rubeus
    | Viridis
    | Violaceus
    | Roseus
    | Niger

/// Types de célébrations liturgiques
type LiturgicalRank =
    | Sollemnitas
    | Dominica
    | Festum
    | Memoria
    | MemoriaAdLibitum
    | FeriaOrdinis

/// Structure représentant une célébration liturgique
type LiturgicalCelebration =
    { Id: string
      Month: int
      Day: int
      Name: string
      Color: LiturgicalColor
      Rank: LiturgicalRank }


//////////////////////////// TEST : ////////////////////////////

/// Type temporaire pour parser le JSON brut (minuscules comme dans vos JSON)
type JsonCelebration =
    { [<JsonPropertyName("month")>]
      Month: int
      [<JsonPropertyName("day")>]
      Day: int
      [<JsonPropertyName("name")>]
      Name: string
      [<JsonPropertyName("color")>]
      Color: string
      [<JsonPropertyName("rank")>]
      Rank: string }

/// Fonctions de conversion JSON -> Types liturgiques
module JsonConverters =

    /// Convertit une couleur JSON vers LiturgicalColor
    let parseColor (colorStr: string) : LiturgicalColor =
        match colorStr.ToLowerInvariant() with
        | "albus" -> Albus
        | "rubeus" -> Rubeus
        | "viridis" -> Viridis
        | "violaceus" -> Violaceus
        | "roseus" -> Roseus
        | "niger" -> Niger
        | _ -> Albus // Par défaut

    /// Convertit un rang JSON vers LiturgicalRank
    let parseRank (rankStr: string) : LiturgicalRank =
        match rankStr.ToLowerInvariant() with
        | "sollemnitas" -> Sollemnitas
        | "dominica" -> Dominica
        | "festum" -> Festum
        | "memoria" -> Memoria
        | "memoriaadlibitum" -> MemoriaAdLibitum
        | _ -> Memoria // Par défaut

    /// Convertit JsonCelebration vers LiturgicalCelebration
    let convertCelebration (id: string) (json: JsonCelebration) : LiturgicalCelebration =
        { Id = id
          Month = json.Month
          Day = json.Day
          Name = json.Name
          Color = parseColor json.Color
          Rank = parseRank json.Rank }
