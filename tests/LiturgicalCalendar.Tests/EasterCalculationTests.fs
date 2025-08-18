namespace LiturgicalCalendar.Tests

module EasterCalculationTests =

    open System
    open Xunit
    open LiturgicalCalendar

    // La classe de test est implicitement reconnue par xUnit,
    // pas besoin d'attribut [TestFixture].
    type EasterCalculationTests() =

        [<Theory>]
        [<InlineData(2024, 3, 31)>]
        [<InlineData(2025, 4, 20)>]
        [<InlineData(2026, 4, 5)>]
        [<InlineData(2027, 3, 28)>]
        [<InlineData(2028, 4, 16)>]
        member _.``Gregorian Easter dates should be calculated correctly``
            (year: int, expectedMonth: int, expectedDay: int)
            =
            // Arrange & Act
            let result = EasterCalculation.calculateEaster Gregorian year

            // Assert
            match result with
            | Ok date ->
                Assert.Equal(expectedMonth, date.Month)
                Assert.Equal(expectedDay, date.Day)
                Assert.Equal(year, date.Year)
            | Error msg -> Assert.Fail($"Expected success but got error: {msg}")

        [<Theory>]
        [<InlineData(2024, 4, 13)>]
        [<InlineData(2025, 5, 3)>]
        [<InlineData(2026, 4, 18)>]
        member _.``Julian Easter dates should be calculated correctly with Gregorian conversion``
            (year: int, expectedMonth: int, expectedDay: int)
            =
            // Arrange & Act
            let result = EasterCalculation.calculateEaster Julian year

            // Assert
            match result with
            | Ok date ->
                Assert.Equal(expectedMonth, date.Month)
                Assert.Equal(expectedDay, date.Day)
                Assert.Equal(year, date.Year)
            | Error msg -> Assert.Fail($"Expected success but got error: {msg}")

        [<Theory>]
        [<InlineData(2024, 5, 5)>]
        [<InlineData(2025, 4, 20)>]
        [<InlineData(2026, 4, 12)>]
        member _.``Orthodox Easter dates should be calculated correctly``
            (year: int, expectedMonth: int, expectedDay: int)
            =
            // Arrange & Act
            let result = EasterCalculation.calculateEaster Orthodox year

            // Assert
            match result with
            | Ok date ->
                Assert.Equal(expectedMonth, date.Month)
                Assert.Equal(expectedDay, date.Day)
                Assert.Equal(year, date.Year)
            | Error msg -> Assert.Fail($"Expected success but got error: {msg}")

        [<Fact>]
        member _.``Should reject negative years``() =
            // Arrange & Act
            let result = EasterCalculation.calculateEaster Gregorian -1

            // Assert
            match result with
            | Error msg -> Assert.True(msg.Contains("positive"))
            | Ok _ -> Assert.Fail("Expected error for negative year")

        [<Fact>]
        member _.``Should reject Gregorian years before 1583``() =
            // Arrange & Act
            let result = EasterCalculation.calculateEaster Gregorian 1500

            // Assert
            match result with
            | Error msg -> Assert.True(msg.Contains("1583"))
            | Ok _ -> Assert.Fail("Expected error for Gregorian year before 1583")

        [<Fact>]
        member _.``Should reject Orthodox years before 1900``() =
            // Arrange & Act
            let result = EasterCalculation.calculateEaster Orthodox 1800

            // Assert
            match result with
            | Error msg -> Assert.True(msg.Contains("1900"))
            | Ok _ -> Assert.Fail("Expected error for Orthodox year before 1900")

        [<Theory>]
        [<InlineData(1583)>]
        [<InlineData(2000)>]
        [<InlineData(2100)>]
        member _.``Should accept valid Gregorian years``(year: int) =
            // Arrange & Act
            let result = EasterCalculation.calculateEaster Gregorian year

            // Assert
            match result with
            | Ok date ->
                Assert.Equal(year, date.Year)
                Assert.True(date.Month >= 3 && date.Month <= 5)
            | Error msg -> Assert.Fail($"Expected success for valid year {year}, got error: {msg}")

        [<Theory>]
        [<InlineData(1900)>]
        [<InlineData(2024)>]
        [<InlineData(2100)>]
        member _.``Should accept valid Orthodox years``(year: int) =
            // Arrange & Act
            let result = EasterCalculation.calculateEaster Orthodox year

            // Assert
            match result with
            | Ok date ->
                Assert.Equal(year, date.Year)
                Assert.True(date.Month >= 3 && date.Month <= 6)
            | Error msg -> Assert.Fail($"Expected success for valid Orthodox year {year}, got error: {msg}")

        [<Fact>]
        member _.``All calendar types should produce valid dates for year 2024``() =
            // Arrange
            let year = 2024
            let calendarTypes = [ Gregorian; Julian; Orthodox ]

            // Act & Assert
            for calendarType in calendarTypes do
                let result = EasterCalculation.calculateEaster calendarType year

                match result with
                | Ok date ->
                    Assert.Equal(year, date.Year)
                    Assert.True(date.Month >= 3 && date.Month <= 6)
                | Error msg -> Assert.Fail($"Expected success for {calendarType} {year}, got error: {msg}")

        [<Fact>]
        member _.``Easter dates should be different between calendar types for same year, except Julian and Orthodox can coincide``
            ()
            =
            // Arrange
            let year = 2024

            // Act
            let gregorian = EasterCalculation.calculateEaster Gregorian year
            let julian = EasterCalculation.calculateEaster Julian year
            let orthodox = EasterCalculation.calculateEaster Orthodox year

            // Assert
            match gregorian, julian, orthodox with
            | Ok gregDate, Ok julDate, Ok ortDate ->
                Assert.NotEqual(gregDate, julDate)
                Assert.NotEqual(gregDate, ortDate)
            // Julian et Orthodox peuvent être égaux, on ne fait plus d'assertion ici
            | _ -> Assert.Fail("All calendar types should succeed for year 2024")


        [<Fact>]
        member _.``Zero year should be rejected for all calendar types``() =
            // Arrange
            let calendarTypes = [ Gregorian; Julian; Orthodox ]

            // Act & Assert
            for calendarType in calendarTypes do
                let result = EasterCalculation.calculateEaster calendarType 0

                match result with
                | Error msg -> Assert.True(msg.Contains("positive"))
                | Ok _ -> Assert.Fail($"Expected error for year 0 with {calendarType}")
