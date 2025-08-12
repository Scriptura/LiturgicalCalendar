namespace LiturgicalCalendar

/// Couleurs liturgiques en latin (forme canonique)
type LiturgicalColor =
    | Albus = 1 // Blanc
    | Rubeus = 2 // Rouge
    | Viridis = 3 // Vert
    | Violaceus = 4 // Violet
    | Roseus = 5 // Rose
    | Niger = 6 // Noir

/// Alias anglais pour compatibilité/lisibilité
module internal LiturgicalColorAlias =
    let White = LiturgicalColor.Albus
    let Red = LiturgicalColor.Rubeus
    let Green = LiturgicalColor.Viridis
    let Violet = LiturgicalColor.Violaceus
    let Purple = LiturgicalColor.Violaceus // même que Violet
    let Rose = LiturgicalColor.Roseus
    let Black = LiturgicalColor.Niger
