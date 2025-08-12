namespace LiturgicalCalendar

/// Structure représentant une célébration liturgique
type LiturgicalCelebration =
    { LiturgicalId: string
      Name: string
      Color: LiturgicalColor
      Type: CelebrationType
      Rank: LiturgicalRank }
