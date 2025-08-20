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
    let NativitatisDomini = DateTime(year, 12, 25)
    let December8 = DateTime(year, 12, 8)
    let March19 = DateTime(year, 3, 19)
    let March25 = DateTime(year, 3, 25)

    // Calculs à partir de Noël
    let SundayBeforeChristmas = NativitatisDomini |> StartOfWeek
    let DominiNostroIesuChristiRegisUniversi = SundayBeforeChristmas |> AddDays -29
    let DominicaPrimaAdventus = SundayBeforeChristmas |> AddDays -22
    let DominicaSecundaAdventus = SundayBeforeChristmas |> AddDays -15
    let DominicaTertiaAdventus = SundayBeforeChristmas |> AddDays -8
    let DominicaQuartaAdventus = SundayBeforeChristmas |> AddDays -1

    // Immaculée Conception (8 décembre ou lundi si dimanche)
    let ImmaculataConceptio =
        match December8.DayOfWeek with
        | DayOfWeek.Sunday -> December8 |> AddDays 1
        | _ -> December8

    // Sainte Famille
    let SanctaFamilia =
        match NativitatisDomini.DayOfWeek with
        | DayOfWeek.Sunday -> DateTime(year, 12, 30)
        | _ -> NativitatisDomini |> EndOfWeek

    // Épiphanie (dimanche entre le 2 et le 8 janvier)
    let EpiphaniaDomini = DateTime(year + 1, 1, 2) |> EndOfWeek

    // Baptême du Seigneur
    let BaptismaDomini =
        match EpiphaniaDomini > DateTime(year + 1, 1, 7) with
        | true -> EpiphaniaDomini |> AddDays 1
        | false -> DateTime(year + 1, 1, 8) |> EndOfWeek

    // Calculs à partir de Pâques
    let DominicaResurrectionis = easter
    let FeriaQuartaCinerum = DominicaResurrectionis |> AddDays -46
    let DominicaPrimaQuadragesimae = DominicaResurrectionis |> AddDays -42
    let DominicaSecundaQuadragesimae = DominicaResurrectionis |> AddDays -35
    let DominicaTertiaQuadragesimae = DominicaResurrectionis |> AddDays -28
    let DominicaQuartaQuadragesimae = DominicaResurrectionis |> AddDays -21
    let DominicaQuintaQuadragesimae = DominicaResurrectionis |> AddDays -14
    let DominicaPalmarum = DominicaResurrectionis |> AddDays -7

    // Semaine Sainte
    let FeriaSecundaHebdomadaeSanctae = DominicaResurrectionis |> AddDays -6
    let FeriaTertiaHebdomadaeSanctae = DominicaResurrectionis |> AddDays -5
    let FeriaQuartaHebdomadaeSanctae = DominicaResurrectionis |> AddDays -4
    let FeriaQuintaInCoenaDomini = DominicaResurrectionis |> AddDays -3
    let FeriaSextaInPassioneDomini = DominicaResurrectionis |> AddDays -2
    let SabbatumSanctum = DominicaResurrectionis |> AddDays -1

    // Temps pascal
    let FeriaSecundaInOctavaPaschae = DominicaResurrectionis |> AddDays 1
    let FeriaTertiaInOctavaPaschae = DominicaResurrectionis |> AddDays 2
    let FeriaQuartaInOctavaPaschae = DominicaResurrectionis |> AddDays 3
    let FeriaQuintaInOctavaPaschae = DominicaResurrectionis |> AddDays 4
    let FeriaSextaInOctavaPaschae = DominicaResurrectionis |> AddDays 5
    let SabbatumInOctavaPaschae = DominicaResurrectionis |> AddDays 6
    let DominicaDivinaeMisericordiae = DominicaResurrectionis |> AddDays 7
    let DominicaTertiaTemporisParschalis = DominicaResurrectionis |> AddDays 14
    let DominicaQuartaTemporisParschalis = DominicaResurrectionis |> AddDays 21
    let DominicaQuintaTemporisParschalis = DominicaResurrectionis |> AddDays 28
    let DominicaSextaTemporisParschalis = DominicaResurrectionis |> AddDays 35
    let AscensioDomini = DominicaResurrectionis |> AddDays 39
    let DominicaSeptimaTemporisParschalis = DominicaResurrectionis |> AddDays 42
    let DominicaPentecostes = DominicaResurrectionis |> AddDays 49
    let BeataMariaMaterEcclesiae = DominicaResurrectionis |> AddDays 50
    let SanctissimaTrinitas = DominicaResurrectionis |> AddDays 56
    let SanctissimumCorpusEtSanguinemChristi = DominicaResurrectionis |> AddDays 63 // France (Concordat)
    let SacratissimumCorIesu = DominicaResurrectionis |> AddDays 68
    let ImmaculatumCorMariaeVirginis = DominicaResurrectionis |> AddDays 69

    // Détection des périodes
    let HolyWeekContains (date: DateTime) =
        date >= DominicaPalmarum && date < DominicaResurrectionis

    let LentContains (date: DateTime) =
        date >= FeriaQuartaCinerum && date < DominicaResurrectionis

    // Conditions pour les fêtes déplacées
    let March19InHolyWeek = HolyWeekContains March19
    let March19InLentSunday = LentContains date && March19.DayOfWeek = DayOfWeek.Sunday

    // Saint Joseph
    let IosephSponsiMariaeVirginis =
        match March19InHolyWeek, March19InLentSunday with
        | true, _ -> DominicaPalmarum |> AddDays -1
        | false, true -> March19 |> AddDays 1
        | false, false -> March19

    // Annonciation
    let AnnuntiatioDomini =
        match HolyWeekContains March25, March25.DayOfWeek with
        | true, _ -> DominicaDivinaeMisericordiae |> AddDays 1
        | false, DayOfWeek.Sunday -> March25 |> AddDays 1
        | false, _ -> March25

    // Pierre et Paul (évite le conflit avec le Sacré-Cœur)
    let SanctiPetriEtPauliApostolorum =
        match SacratissimumCorIesu.Month = 6 && SacratissimumCorIesu.Day = 29 with
        | true -> DateTime(year, 6, 30)
        | false -> DateTime(year, 6, 29)

    // Nativité de Jean-Baptiste (évite les conflits)
    let NativitasSanctiIoannisBaptistae =
        match
            SacratissimumCorIesu.Month = 6 && SacratissimumCorIesu.Day = 24,
            SanctissimumCorpusEtSanguinemChristi.Month = 6
            && SanctissimumCorpusEtSanguinemChristi.Day = 24
        with
        | true, _ -> DateTime(year, 6, 23)
        | false, true -> DateTime(year, 6, 25)
        | false, false -> DateTime(year, 6, 24)

    // Retour de toutes les fêtes calculées (à adapter selon vos besoins)
    {| NativitatisDomini = NativitatisDomini
       DominicaResurrectionis = DominicaResurrectionis
       DominicaPalmarum = DominicaPalmarum
       IosephSponsiMariaeVirginis = IosephSponsiMariaeVirginis
       AnnuntiatioDomini = AnnuntiatioDomini
    // ... toutes les autres fêtes
    |}
