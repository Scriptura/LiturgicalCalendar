namespace LiturgicalCalendar

/// Structure représentant une célébration liturgique
type LiturgicalCelebration =
    { Id: string
      Name: string
      Color: LiturgicalColor
      Type: CelebrationType
      Rank: LiturgicalRank }
