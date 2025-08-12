namespace LiturgicalCalendar

open System
open System.Collections.Generic

module LiturgicalData =

    // Calendrier romain général (immuable)
    let private generalRomanCalendar =
        [ (FixedDate.Create(1, 1),
           { LiturgicalId = "mariaeMatrisDei"
             Name = "Sainte Marie, Mère de Dieu"
             Color = LiturgicalColor.Albus
             Type = CelebrationType.Sollemnitas
             Rank = LiturgicalRank.Create(3) })

          (FixedDate.Create(1, 2),
           { LiturgicalId = "basiliusMagnusEtGregoriusNazianzenus"
             Name =
               "Saint Basile le Grand, évêque de Césarée, docteur de l'Église et Saint Grégoire de Naziance, évêque de Constantinople, docteur de l'Église"
             Color = LiturgicalColor.Albus
             Type = CelebrationType.Memoria
             Rank = LiturgicalRank.Create(10) }) ]
        |> Map.ofList

    // Calendriers régionaux
    let private europeRomanCalendar =
        [ (FixedDate.Create(7, 11),
           { LiturgicalId = "benedictusDeNursia"
             Name = "Saint Benoît de Nursie, abbé, patron de l'Europe"
             Color = LiturgicalColor.Albus
             Type = CelebrationType.Festum
             Rank = LiturgicalRank.Create(5) }) ]
        |> Map.ofList

    let private frenchRomanCalendar =
        [ (FixedDate.Create(1, 3),
           { LiturgicalId = "genovefaParisiensis"
             Name = "Sainte Geneviève, vierge, patronne de Paris"
             Color = LiturgicalColor.Albus
             Type = CelebrationType.Memoria
             Rank = LiturgicalRank.Create(8) }) ]
        |> Map.ofList

    // Fusion de calendriers (priorité au plus spécifique)
    let private mergeCalendars baseCalendar overrideCalendar =
        Map.fold (fun acc key value -> Map.add key value acc) baseCalendar overrideCalendar

    // Types de calendriers disponibles
    type ParticularCalendar =
        | General
        | Europe
        | France

    // Obtenir un calendrier spécifique
    let getCalendar territory =
        match territory with
        | General -> generalRomanCalendar
        | Europe -> mergeCalendars generalRomanCalendar europeRomanCalendar
        | France ->
            generalRomanCalendar
            |> mergeCalendars europeRomanCalendar
            |> mergeCalendars frenchRomanCalendar

    // Rechercher une célébration à une date donnée
    let getLiturgicalCelebration calendar date =
        let fixedDate = FixedDate.Create(date.Month, date.Day)
        Map.tryFind fixedDate calendar

    // Obtenir toutes les célébrations d'un mois
    let getCelebrationsInMonth calendar month =
        calendar |> Map.toList |> List.filter (fun (fd, _) -> fd.Month = month)
