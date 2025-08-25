namespace LiturgicalCalendar.Tests

module MovableFeastsTests =

    open System
    open Xunit
    open LiturgicalCalendar

    [<Fact>]
    let ``Premier dimanche de l'Avent le 29 novembre 2020`` () =
        let date = DateTime(2020, 11, 29)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("firstAdventSunday", result.Key)

    [<Fact>]
    let ``Deuxième dimanche de l'Avent le 6 décembre 2020`` () =
        let date = DateTime(2020, 12, 6)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("secondAdventSunday", result.Key)

    [<Fact>]
    let ``Immaculée Conception le 8 décembre 2020`` () =
        let date = DateTime(2020, 12, 8)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("immaculateConception", result.Key)

    [<Fact>]
    let ``Immaculée Conception le 9 décembre 2019`` () =
        let date = DateTime(2019, 12, 9)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("immaculateConception", result.Key)

    [<Fact>]
    let ``Troisième dimanche de l'Avent le 13 décembre 2020`` () =
        let date = DateTime(2020, 12, 13)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("thirdAdventSunday", result.Key)

    [<Fact>]
    let ``Quatrième dimanche de l'Avent le 20 décembre 2020`` () =
        let date = DateTime(2020, 12, 20)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("fourthAdventSunday", result.Key)

    [<Fact>]
    let ``Noël le 25 décembre 2020`` () =
        let date = DateTime(2020, 12, 25)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("christmas", result.Key)

    [<Fact>]
    let ``Sainte Famille le dimanche qui suit Noël, le 27 décembre 2020`` () =
        let date = DateTime(2020, 12, 27)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("holyFamily", result.Key)

    [<Fact>]
    let ``Sainte Famille le 30 décembre 2022 car Noël un dimanche`` () =
        let date = DateTime(2022, 12, 30)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("holyFamily", result.Key)

    [<Fact>]
    let ``Sainte Marie, Mère de Dieu, le 1er janvier 2021`` () =
        let date = DateTime(2021, 1, 1)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("maryMotherOfGod", result.Key)

    [<Fact>]
    let ``Saint Nom de Jésus ou Sainte Geneviève, le 3 janvier 2020`` () =
        let date = DateTime(2020, 1, 3)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("mostHolyNameOfJesusOrGenevieveOfParis", result.Key)

    [<Fact>]
    let ``Épiphanie le dimanche après le premier janvier pour la France`` () =
        let date = DateTime(2021, 1, 3)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("epiphany", result.Key)

    [<Fact>]
    let ``Informations de l'Épiphanie le 6 janvier 2021 écrasées pour la France au profit de la férie`` () =
        let date = DateTime(2021, 1, 6)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("defaultKey", result.Key)

    [<Fact>]
    let ``Baptême du Seigneur le 10 janvier 2021`` () =
        let date = DateTime(2021, 1, 10)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("baptismOfTheLord", result.Key)

    [<Fact>]
    let ``Fête de St Joseph le 19 mars 2020`` () =
        let date = DateTime(2020, 3, 19)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("josephHusbandOfMary", result.Key)

    [<Fact>]
    let ``19 mars 2035 en Semaine Sainte, alors fête de St Joseph reportée au samedi avant les Rameaux, le 15 mars``
        ()
        =
        let date = DateTime(2035, 3, 17)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("josephHusbandOfMary", result.Key)

    [<Fact>]
    let ``Annonciation le 25 mars 2021`` () =
        let date = DateTime(2021, 3, 25)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("annunciation", result.Key)

    [<Fact>]
    let ``25 mars 2012 un dimanche, alors Annonciation le 26 mars`` () =
        let date = DateTime(2012, 3, 26)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("annunciation", result.Key)

    [<Fact>]
    let ``25 mars 2024 pendant la Semaine Sainte, alors Annonciation le 8 avril`` () =
        let date = DateTime(2024, 4, 8)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("annunciation", result.Key)

    [<Fact>]
    let ``Mercredi des Cendres le 26 février 2020`` () =
        let date = DateTime(2020, 2, 26)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("ashWednesday", result.Key)

    [<Fact>]
    let ``Premier dimanche de Carême le 1er mars 2020`` () =
        let date = DateTime(2020, 3, 1)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("firstLentSunday", result.Key)

    [<Fact>]
    let ``Deuxième dimanche de Carême`` () =
        let date = DateTime(2020, 3, 8)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("secondLentSunday", result.Key)

    [<Fact>]
    let ``Troisième dimanche de Carême`` () =
        let date = DateTime(2020, 3, 15)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("thirdLentSunday", result.Key)

    [<Fact>]
    let ``Quatrième dimanche de Carême`` () =
        let date = DateTime(2020, 3, 22)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("fourthLentSunday", result.Key)

    [<Fact>]
    let ``Cinquième dimanche de Carême`` () =
        let date = DateTime(2020, 3, 29)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("fiveLentSunday", result.Key)

    [<Fact>]
    let ``Dimanche des Rameaux`` () =
        let date = DateTime(2020, 4, 5)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("palmSunday", result.Key)

    [<Fact>]
    let ``Lundi Saint le 6 avril 2020`` () =
        let date = DateTime(2020, 4, 6)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("holyMonday", result.Key)

    [<Fact>]
    let ``Mardi Saint le 7 avril 2020`` () =
        let date = DateTime(2020, 4, 7)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("holyTuesday", result.Key)

    [<Fact>]
    let ``Mercredi Saint le 8 avril 2020`` () =
        let date = DateTime(2020, 4, 8)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("holyWednesday", result.Key)

    [<Fact>]
    let ``Jeudi Saint le 9 avril 2020`` () =
        let date = DateTime(2020, 4, 9)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("holyThursday", result.Key)

    [<Fact>]
    let ``Vendredi Saint le 10 avril 2020`` () =
        let date = DateTime(2020, 4, 10)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("goodFriday", result.Key)

    [<Fact>]
    let ``Samedi Saint le 11 avril 2020`` () =
        let date = DateTime(2020, 4, 11)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("holySaturday", result.Key)

    [<Fact>]
    let ``Pâques le 12 avril 2020`` () =
        let date = DateTime(2020, 4, 12)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("easter", result.Key)

    [<Fact>]
    let ``Lundi dans l'Octave de Pâques le 13 avril 2020`` () =
        let date = DateTime(2020, 4, 13)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("easterMonday", result.Key)

    [<Fact>]
    let ``Mardi dans l'Octave de Pâques le 14 avril 2020`` () =
        let date = DateTime(2020, 4, 14)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("easterTuesday", result.Key)

    [<Fact>]
    let ``Mercredi dans l'Octave de Pâques le 15 avril 2020`` () =
        let date = DateTime(2020, 4, 15)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("easterWednesday", result.Key)

    [<Fact>]
    let ``Jeudi dans l'Octave de Pâques le 16 avril 2020`` () =
        let date = DateTime(2020, 4, 16)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("easterThursday", result.Key)

    [<Fact>]
    let ``Vendredi dans l'Octave de Pâques le 17 avril 2020`` () =
        let date = DateTime(2020, 4, 17)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("easterFriday", result.Key)

    [<Fact>]
    let ``Samedi dans l'Octave de Pâques le 18 avril 2020`` () =
        let date = DateTime(2020, 4, 18)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("easterSaturday", result.Key)

    [<Fact>]
    let ``Dimanche de la Divine Miséricorde le 19 avril 2020`` () =
        let date = DateTime(2020, 4, 19)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("divineMercySunday", result.Key)

    [<Fact>]
    let ``Troisième dimanche du Temps Pascal le 26 avril 2020`` () =
        let date = DateTime(2020, 4, 26)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("thirdSundayEaster", result.Key)

    [<Fact>]
    let ``Quatrième dimanche du Temps Pascal le 3 mai 2020`` () =
        let date = DateTime(2020, 5, 3)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("fourthSundayEaster", result.Key)

    [<Fact>]
    let ``Cinquième dimanche du Temps Pascal le 10 mai 2020`` () =
        let date = DateTime(2020, 5, 10)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("fiveSundayEaster", result.Key)

    [<Fact>]
    let ``Sixième dimanche du Temps Pascal le 17 mai 2020`` () =
        let date = DateTime(2020, 5, 17)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("sixSundayEaster", result.Key)

    [<Fact>]
    let ``Septième dimanche du Temps Pascal le 24 mai 2020`` () =
        let date = DateTime(2020, 5, 24)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("sevenSundayEaster", result.Key)

    [<Fact>]
    let ``Ascension le 30 mai 2019, à la place de Sainte Jeanne d'Arc`` () =
        let date = DateTime(2019, 5, 30)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("ascension", result.Key)

    [<Fact>]
    let ``Ascension le 21 mai 2020`` () =
        let date = DateTime(2020, 5, 21)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("ascension", result.Key)

    [<Fact>]
    let ``Visitation de la Vierge Marie le 31 mai 2019`` () =
        let date = DateTime(2019, 5, 31)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("visitation", result.Key)

    [<Fact>]
    let ``Pentecôte le 31 mai 2020, remplace la fête de la Visitation`` () =
        let date = DateTime(2020, 5, 31)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("pentecost", result.Key)

    [<Fact>]
    let ``Marie, Mère de l'Église le 1er juin 2020`` () =
        let date = DateTime(2020, 6, 1)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("maryMotherOfTheChurch", result.Key)

    [<Fact>]
    let ``Sainte Trinité le 7 juin 2020`` () =
        let date = DateTime(2020, 6, 7)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("holyTrinity", result.Key)

    [<Fact>]
    let ``Saint Sacrement (Fête Dieu) le 14 juin 2020`` () =
        let date = DateTime(2020, 6, 14)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("corpusChristi", result.Key)

    [<Fact>]
    let ``Sacré-Cœur de Jésus le 19 juin 2020`` () =
        let date = DateTime(2020, 6, 19)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("sacredHeart", result.Key)

    [<Fact>]
    let ``Sacré-Cœur de Jésus le 2 juillet 2038`` () =
        let date = DateTime(2038, 7, 2)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("sacredHeart", result.Key)

    [<Fact>]
    let ``Cœur Immaculé de Marie le 20 juin 2020`` () =
        let date = DateTime(2020, 6, 20)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("immaculateHeartOfMary", result.Key)

    [<Fact>]
    let ``Nativité de Saint Jean-Baptiste le 24 juin 2020`` () =
        let date = DateTime(2020, 6, 24)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("nativityOfJohnTheBaptist", result.Key)

    [<Fact>]
    let ``Nativité de Saint Jean-Baptiste le 23 juin si Sacré-Coeur le 24 juin`` () =
        let date = DateTime(2022, 6, 23)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("nativityOfJohnTheBaptist", result.Key)

    [<Fact>]
    let ``Nativité de Saint Jean-Baptiste le 25 juin 2057 si Saint Sacrement le 24`` () =
        let date = DateTime(2057, 6, 25)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("nativityOfJohnTheBaptist", result.Key)

    [<Fact>]
    let ``Saints Pierre et Paul le 29 juin 2019`` () =
        let date = DateTime(2019, 6, 29)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("peterAndPaul", result.Key)

    [<Fact>]
    let ``Saints Pierre et Paul le 29 juin 2020`` () =
        let date = DateTime(2020, 6, 29)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("peterAndPaul", result.Key)

    [<Fact>]
    let ``Sainte Marthe, Sainte Marie et Saint Lazare le 29 juillet 2021`` () =
        let date = DateTime(2021, 7, 29)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("marthaAndMaryAndLazarusOfBethany", result.Key)

    [<Fact>]
    let ``Christ Roi le 22 novembre 2020`` () =
        let date = DateTime(2020, 11, 22)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("christKingOfTheUniverse", result.Key)

    [<Fact>]
    let ``Christ Roi le 21 novembre 2021, à la place de la Présentation de Marie`` () =
        let date = DateTime(2021, 11, 21)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("christKingOfTheUniverse", result.Key)

    [<Fact>]
    let ``Noël le 25 décembre 2020`` () =
        let date = DateTime(2020, 12, 25)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("christmas", result.Key)

    [<Fact>]
    let ``13ème dimanche du temps ordinaire`` () =
        let date = DateTime(2022, 6, 26)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("ordinaryTimeSunday13", result.Key)

    [<Fact>]
    let ``21ème dimanche du temps ordinaire`` () =
        let date = DateTime(2022, 8, 21)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("ordinaryTimeSunday21", result.Key)

    [<Fact>]
    let ``32ème dimanche du temps ordinaire`` () =
        let date = DateTime(2022, 11, 6)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("ordinaryTimeSunday32", result.Key)

    [<Fact>]
    let ``33ème dimanche du temps ordinaire`` () =
        let date = DateTime(2022, 11, 13)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("ordinaryTimeSunday33", result.Key)

    [<Fact>]
    let ``Sortie du type de célébration en langage humain`` () =
        let dateNoel = DateTime(2020, 12, 25)
        let resultNoel = LiturgicalCalendar dateNoel "france"
        Assert.Equal("Solennité", toHumanType resultNoel.Type)

        let dateBapteme = DateTime(2021, 1, 10)
        let resultBapteme = LiturgicalCalendar dateBapteme "france"
        Assert.Equal("Fête", toHumanType resultBapteme.Type)

        let dateMemoireObligatoire = DateTime(2020, 12, 14)
        let resultMemoireObligatoire = LiturgicalCalendar dateMemoireObligatoire "france"
        Assert.Equal("Mémoire obligatoire", toHumanType resultMemoireObligatoire.Type)

        let dateMemoireFacultative = DateTime(2021, 1, 13)
        let resultMemoireFacultative = LiturgicalCalendar dateMemoireFacultative "france"
        Assert.Equal("Mémoire facultative", toHumanType resultMemoireFacultative.Type)

    [<Fact>]
    let ``Propre pour la Belgique : Saint Père Damien le 10 mai 2021`` () =
        let date = DateTime(2021, 5, 10)
        let result = LiturgicalCalendar date "belgium"
        Assert.Equal("damienDeVeuster", result.Key)

    [<Fact>]
    let ``Propre pour la Belgique : Sainte Julienne du Mont-Cornillon le 7 août 2020`` () =
        let date = DateTime(2020, 8, 7)
        let result = LiturgicalCalendar date "belgium"
        Assert.Equal("julianaOfLiege", result.Key)

    [<Fact>]
    let ``Propre pour la Belgique : Marie, Médiatrice de toute grâce le 31 août 2020`` () =
        let date = DateTime(2020, 8, 31)
        let result = LiturgicalCalendar date "belgium"
        Assert.Equal("ourLadyMediatrixOfAllGrace", result.Key)

    [<Fact>]
    let ``Propre pour la Belgique : Saint Hubert, évêque de Liège le 3 novembre 2020`` () =
        let date = DateTime(2020, 11, 3)
        let result = LiturgicalCalendar date "belgium"
        Assert.Equal("hubertOfLiege", result.Key)

    [<Fact>]
    let ``Propre pour la France : Sainte Jeanne d'Arc le 30 mai 2020`` () =
        let date = DateTime(2020, 5, 30)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("joanOfArc", result.Key)

    [<Fact>]
    let ``Propre pour la France : Sainte Thérèse de l'enfant Jésus le 1 octobre 2020`` () =
        let date = DateTime(2020, 10, 1)
        let result = LiturgicalCalendar date "france"
        Assert.Equal("thereseOfTheChildJesusAndTheHolyFace", result.Key)
