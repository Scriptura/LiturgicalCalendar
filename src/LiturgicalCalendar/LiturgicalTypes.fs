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

/// Rang liturgique selon les normes romaines (1-13)
[<Struct>]
type LiturgicalPrecedence =
    private
    | LiturgicalPrecedence of int

    /// Crée un rang liturgique valide (1 à 13)
    static member Create(value: int) =
        if value < 1 || value > 13 then
            invalidArg (nameof value) "Le rang liturgique doit être compris entre 1 et 13"

        LiturgicalPrecedence value

    member this.Value =
        let (LiturgicalPrecedence v) = this
        v

    static member op_Implicit(priority: LiturgicalPrecedence) = priority.Value

    override this.ToString() = string this.Value

/// Structure représentant une célébration liturgique
type LiturgicalCelebration =
    { Id: string
      Month: int
      Day: int
      Name: string
      Color: LiturgicalColor
      Rank: LiturgicalRank
      Priority: LiturgicalPrecedence }


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
      Rank: string
      [<JsonPropertyName("priority")>]
      Priority: int }

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
          Rank = parseRank json.Rank
          Priority = LiturgicalPrecedence.Create(json.Priority) }
