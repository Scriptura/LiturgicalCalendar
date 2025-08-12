namespace LiturgicalCalendar

/// Rang liturgique selon les normes romaines (1-13)
[<Struct>]
type LiturgicalRank =
    private
    | LiturgicalRank of int

    static member Create(value: int) =
        if value < 1 || value > 13 then
            invalidArg "value" "Le rang liturgique doit être entre 1 et 13"

        LiturgicalRank value

    member this.Value =
        let (LiturgicalRank v) = this
        v

    static member op_Implicit(rank: LiturgicalRank) = rank.Value

    override this.ToString() = this.Value.ToString()
