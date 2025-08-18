namespace LiturgicalCalendar

module MovableFeasts =

    open System

    // Helper functions
    let AddDays days (date: DateTime) = date.AddDays(float days)
    let AddYears years (date: DateTime) = date.AddYears(years)

    let StartOfWeek (date: DateTime) =
        date.AddDays(float -(int date.DayOfWeek))

    let EndOfWeek (date: DateTime) =
        date.AddDays(float (7 - int date.DayOfWeek))

// Calcul de toutes les fêtes mobiles pour une année donnée
let calculateMovableFeasts (year: int) (easter: DateTime) (date: DateTime) =

    // Fêtes fixes de base
    let Christmas = DateTime(year, 12, 25)
    let December8 = DateTime(year, 12, 8)
    let March19 = DateTime(year, 3, 19)
    let March25 = DateTime(year, 3, 25)

    // Calculs à partir de Noël
    let SundayBeforeChristmas = Christmas |> StartOfWeek
    let ChristKingOfTheUniverse = SundayBeforeChristmas |> AddDays -29
    let FirstAdventSunday = SundayBeforeChristmas |> AddDays -22
    let SecondAdventSunday = SundayBeforeChristmas |> AddDays -15
    let ThirdAdventSunday = SundayBeforeChristmas |> AddDays -8
    let FourthAdventSunday = SundayBeforeChristmas |> AddDays -1

    // Immaculée Conception (8 décembre ou lundi si dimanche)
    let ImmaculateConception =
        match December8.DayOfWeek with
        | DayOfWeek.Sunday -> December8 |> AddDays 1
        | _ -> December8

    // Sainte Famille
    let HolyFamily =
        match Christmas.DayOfWeek with
        | DayOfWeek.Sunday -> DateTime(year, 12, 30)
        | _ -> Christmas |> EndOfWeek

    // Épiphanie (dimanche entre le 2 et le 8 janvier)
    let Epiphany = DateTime(year + 1, 1, 2) |> EndOfWeek

    // Baptême du Seigneur
    let BaptismOfTheLord =
        match Epiphany > DateTime(year + 1, 1, 7) with
        | true -> Epiphany |> AddDays 1
        | false -> DateTime(year + 1, 1, 8) |> EndOfWeek

    // Calculs à partir de Pâques
    let Easter = easter
    let AshWednesday = Easter |> AddDays -46
    let FirstLentSunday = Easter |> AddDays -42
    let SecondLentSunday = Easter |> AddDays -35
    let ThirdLentSunday = Easter |> AddDays -28
    let FourthLentSunday = Easter |> AddDays -21
    let FiveLentSunday = Easter |> AddDays -14
    let PalmSunday = Easter |> AddDays -7

    // Semaine Sainte
    let HolyMonday = Easter |> AddDays -6
    let HolyTuesday = Easter |> AddDays -5
    let HolyWednesday = Easter |> AddDays -4
    let HolyThursday = Easter |> AddDays -3
    let GoodFriday = Easter |> AddDays -2
    let HolySaturday = Easter |> AddDays -1

    // Temps pascal
    let EasterMonday = Easter |> AddDays 1
    let EasterTuesday = Easter |> AddDays 2
    let EasterWednesday = Easter |> AddDays 3
    let EasterThursday = Easter |> AddDays 4
    let EasterFriday = Easter |> AddDays 5
    let EasterSaturday = Easter |> AddDays 6
    let DivineMercySunday = Easter |> AddDays 7
    let ThirdSundayEaster = Easter |> AddDays 14
    let FourthSundayEaster = Easter |> AddDays 21
    let FiveSundayEaster = Easter |> AddDays 28
    let SixSundayEaster = Easter |> AddDays 35
    let Ascension = Easter |> AddDays 39
    let SevenSundayEaster = Easter |> AddDays 42
    let Pentecost = Easter |> AddDays 49
    let MaryMotherOfTheChurch = Easter |> AddDays 50
    let HolyTrinity = Easter |> AddDays 56
    let CorpusChristi = Easter |> AddDays 63 // France (Concordat)
    let SacredHeart = Easter |> AddDays 68
    let ImmaculateHeartOfMary = Easter |> AddDays 69

    // Détection des périodes
    let HolyWeekContains (date: DateTime) = date >= PalmSunday && date < Easter
    let LentContains (date: DateTime) = date >= AshWednesday && date < Easter

    // Conditions pour les fêtes déplacées
    let March19InHolyWeek = HolyWeekContains March19
    let March19InLentSunday = LentContains date && March19.DayOfWeek = DayOfWeek.Sunday

    // Saint Joseph
    let JosephHusbandOfMary =
        match March19InHolyWeek, March19InLentSunday with
        | true, _ -> PalmSunday |> AddDays -1
        | false, true -> March19 |> AddDays 1
        | false, false -> March19

    // Annonciation
    let Annunciation =
        match HolyWeekContains March25, March25.DayOfWeek with
        | true, _ -> DivineMercySunday |> AddDays 1
        | false, DayOfWeek.Sunday -> March25 |> AddDays 1
        | false, _ -> March25

    // Pierre et Paul (évite le conflit avec le Sacré-Cœur)
    let PeterAndPaul =
        match SacredHeart.Month = 6 && SacredHeart.Day = 29 with
        | true -> DateTime(year, 6, 30)
        | false -> DateTime(year, 6, 29)

    // Nativité de Jean-Baptiste (évite les conflits)
    let NativityOfJohnTheBaptist =
        match SacredHeart.Month = 6 && SacredHeart.Day = 24, CorpusChristi.Month = 6 && CorpusChristi.Day = 24 with
        | true, _ -> DateTime(year, 6, 23)
        | false, true -> DateTime(year, 6, 25)
        | false, false -> DateTime(year, 6, 24)

    // Retour de toutes les fêtes calculées (à adapter selon vos besoins)
    {| Christmas = Christmas
       Easter = Easter
       PalmSunday = PalmSunday
       JosephHusbandOfMary = JosephHusbandOfMary
       Annunciation = Annunciation
    // ... toutes les autres fêtes
    |}
