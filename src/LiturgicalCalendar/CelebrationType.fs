namespace LiturgicalCalendar

/// Types de célébrations liturgiques
type CelebrationType =
    | Sollemnitas = 1 // Solennité
    | Festum = 2 // Fête
    | Memoria = 3 // Mémoire
    | MemoriaAdLibitum = 4 // Mémoire facultative
    | FeriaOrdinis = 5 // Ferie
    | Dominica = 6 // Dimanche

module internal CelebrationTypeAlias =
    let Solemnity = CelebrationType.Sollemnitas
    let Feast = CelebrationType.Festum
    let Memorial = CelebrationType.Memoria
    let OptionalMemorial = CelebrationType.MemoriaAdLibitum
    let Ferial = CelebrationType.FeriaOrdinis
    let Sunday = CelebrationType.Dominica
