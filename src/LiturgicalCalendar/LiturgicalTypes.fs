namespace LiturgicalCalendar

open System
open System.IO
open System.Text.Json

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
