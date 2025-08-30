namespace LiturgicalCalendar

open System
open System.Text.Json.Serialization

/// Couleurs liturgiques en latin (forme canonique)
type LiturgicalColor =
    | Albus // Blanc
    | Rubeus // Rouge
    | Viridis // Vert
    | Violaceus // Violet/Pourpre
    | Roseus // Rose
    | Niger // Noir

    // Conversion depuis string latin
    static member FromLatin(colorLatin: string) =
        match colorLatin.ToLower().Trim() with
        | "albus" -> Some Albus
        | "rubeus" -> Some Rubeus
        | "viridis" -> Some Viridis
        | "violaceus" -> Some Violaceus
        | "roseus" -> Some Roseus
        | "niger" -> Some Niger
        | _ -> None

    // Conversion vers string latin
    member this.ToLatin() =
        match this with
        | Albus -> "albus"
        | Rubeus -> "rubeus"
        | Viridis -> "viridis"
        | Violaceus -> "violaceus"
        | Roseus -> "roseus"
        | Niger -> "niger"

    // Nom français pour l'affichage
    member this.ToFrench() =
        match this with
        | Albus -> "Blanc"
        | Rubeus -> "Rouge"
        | Viridis -> "Vert"
        | Violaceus -> "Violet"
        | Roseus -> "Rose"
        | Niger -> "Noir"

/// Types de célébrations liturgiques
type LiturgicalRank =
    | Sollemnitas // Solennité
    | Dominica // Dimanche
    | Festum // Fête
    | Memoria // Mémoire
    | MemoriaAdLibitum // Mémoire libre
    | FeriaOrdinis // Férie ordinaire

    static member FromLatin(rankLatin: string) =
        match rankLatin.ToLower().Trim() with
        | "sollemnitas" -> Some Sollemnitas
        | "dominica" -> Some Dominica
        | "festum" -> Some Festum
        | "memoria" -> Some Memoria
        | "memoria ad libitum" -> Some MemoriaAdLibitum
        | "feria ordinis"
        | "feria" -> Some FeriaOrdinis
        | _ -> None

    member this.ToLatin() =
        match this with
        | Sollemnitas -> "sollemnitas"
        | Dominica -> "dominica"
        | Festum -> "festum"
        | Memoria -> "memoria"
        | MemoriaAdLibitum -> "memoria ad libitum"
        | FeriaOrdinis -> "feria ordinis"

    member this.ToFrench() =
        match this with
        | Sollemnitas -> "Solennité"
        | Dominica -> "Dimanche"
        | Festum -> "Fête"
        | Memoria -> "Mémoire"
        | MemoriaAdLibitum -> "Mémoire libre"
        | FeriaOrdinis -> "Férie"

// Type pour les informations liturgiques (gardant les strings pour la sérialisation JSON)
[<CLIMutable>]
type LiturgicalInfo =
    { Name: string
      Extra: string option
      Color: string // Format latin du JSON
      Rank: string option } // Format latin du JSON
    // Méthodes de conversion vers les types typés
    member this.GetColor() = LiturgicalColor.FromLatin(this.Color)

    member this.GetRank() =
        this.Rank |> Option.bind LiturgicalRank.FromLatin

    // Méthodes utilitaires pour l'affichage
    member this.GetColorFrench() =
        this.GetColor()
        |> Option.map (fun c -> c.ToFrench())
        |> Option.defaultValue this.Color

    member this.GetRankFrench() =
        this.Rank
        |> Option.bind LiturgicalRank.FromLatin
        |> Option.map (fun r -> r.ToFrench())
        |> Option.defaultValue (this.Rank |> Option.defaultValue "Non spécifié")

// Alias de type pour l'ensemble des données liturgiques
type LiturgicalData = Map<string, LiturgicalInfo>

// Type pour une date liturgique (avec l'année)
type LiturgicalDate =
    { Info: LiturgicalInfo
      Date: DateTime
      Year: int
      Key: string }
    // Propriétés calculées pour un accès facile aux types typés
    member this.Color = this.Info.GetColor()
    member this.Rank = this.Info.GetRank()
    member this.ColorFrench = this.Info.GetColorFrench()
    member this.RankFrench = this.Info.GetRankFrench()

// Types d'erreurs spécifiques au domaine liturgique
type LiturgicalError =
    | FileNotFound of path: string
    | InvalidJson of message: string
    | MissingKey of key: string
    | EasterCalculationError of message: string
    | UnknownLiturgicalColor of color: string
    | UnknownLiturgicalRank of rank: string
