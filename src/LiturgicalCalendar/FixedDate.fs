namespace LiturgicalCalendar

open System

/// Structure pour une date fixe (mois, jour)
[<Struct>]
type FixedDate =
    { Month: int
      Day: int }

    static member Create(month: int, day: int) =
        if month < 1 || month > 12 then
            invalidArg "month" "Le mois doit être entre 1 et 12"

        if day < 1 || day > 31 then
            invalidArg "day" "Le jour doit être entre 1 et 31"

        { Month = month; Day = day }

    member this.ToDateOnly(year: int) = DateOnly(year, this.Month, this.Day)

    override this.ToString() = sprintf "%02d%02d" this.Month this.Day
