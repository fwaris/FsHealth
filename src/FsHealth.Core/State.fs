namespace FsHealth

open System
open System.Globalization

type Screen =
    | Overview
    | History
    | Trends
    | Settings

type HistoryFilter =
    | AllReadings
    | OnlyGlucose
    | OnlyPressure

type Period =
    | Week
    | Month
    | Quarter

type DraftField =
    | GlucoseText
    | SystolicText
    | DiastolicText
    | PulseText
    | NotesText

type GlucoseInput =
    | EnteredGlucose of string
    | ConvertedGlucose of GlucoseValue

type GlucoseDraft =
    {
        Value: GlucoseInput
        Unit: GlucoseUnit
        Context: MealContext
    }

type PressureDraft =
    {
        Systolic: string
        Diastolic: string
        Pulse: string
    }

type DraftMeasurement =
    | DraftGlucose of GlucoseDraft
    | DraftPressure of PressureDraft

type Draft =
    {
        Id: ReadingId
        Measurement: DraftMeasurement
        Date: DateTime
        Time: TimeSpan
        Offset: TimeSpan option
        Notes: string
    }

type StoreState =
    | Loading
    | Ready of Journal
    | LoadFailed of string

type Confirmation =
    | DeleteReading of ReadingId
    | ClearJournal

type Model =
    {
        Store: StoreState
        Screen: Screen
        Draft: Draft option
        Filter: HistoryFilter
        Period: Period
        HistoryLimit: int
        Confirmation: Confirmation option
        Pending: Journal option
        Exporting: bool
        Error: string option
        Notice: string option
    }

type Msg =
    | Loaded of Result<Journal, string>
    | RetryLoad
    | Navigate of Screen
    | RequestAdd of ReadingKind
    | RequestSave
    | AddReading of ReadingKind * ReadingId * DateTimeOffset
    | EditReading of ReadingId
    | ChangeField of DraftField * string
    | ChangeDate of DateTime
    | ChangeTime of TimeSpan
    | ChangeContext of MealContext
    | ChangeDraftUnit of GlucoseUnit
    | CancelDraft
    | SaveDraft of DateTimeOffset
    | Persisted of Result<unit, string>
    | SetUnit of GlucoseUnit
    | SetFilter of HistoryFilter
    | SetPeriod of Period
    | ShowMore
    | AskConfirmation of Confirmation
    | CancelConfirmation
    | Confirm
    | Export
    | Exported of Result<unit, string>

type Effect =
    | Load
    | Persist of Journal
    | ShareCsv of string
    | NewReadingTime of ReadingKind
    | SaveTime

module State =
    let init () : Model * Effect list =
        {
            Store = Loading
            Screen = Overview
            Draft = None
            Filter = AllReadings
            Period = Week
            HistoryLimit = 40
            Confirmation = None
            Pending = None
            Exporting = false
            Error = None
            Notice = None
        },
        [ Load ]

    let journal (model: Model) : Journal =
        match model.Store with
        | Ready value -> value
        | _ -> Journal.empty

    let busy (model: Model) : bool = model.Pending.IsSome || model.Exporting

    let days (period: Period) : int =
        match period with
        | Week -> 7
        | Month -> 30
        | Quarter -> 90

    let history (model: Model) : Reading list =
        Journal.ordered (journal model)
        |> List.filter (fun r ->
            match model.Filter with
            | AllReadings -> true
            | OnlyGlucose -> Reading.kind r = GlucoseKind
            | OnlyPressure -> Reading.kind r = BloodPressureKind)

    let private draftFromReading (unit: GlucoseUnit) (reading: Reading) : Draft =
        let measurement =
            match reading.Measurement with
            | Glucose(value, context) ->
                DraftGlucose
                    {
                        Value = ConvertedGlucose value
                        Unit = unit
                        Context = context
                    }
            | Pressure pressure ->
                DraftPressure
                    {
                        Systolic = string (BloodPressure.systolic pressure)
                        Diastolic = string (BloodPressure.diastolic pressure)
                        Pulse = BloodPressure.pulse pressure |> Option.map string |> Option.defaultValue ""
                    }

        {
            Id = reading.Id
            Measurement = measurement
            Date = reading.MeasuredAt.LocalDateTime.Date
            Time = reading.MeasuredAt.LocalDateTime.TimeOfDay
            Offset = Some(TimeZoneInfo.Local.GetUtcOffset reading.MeasuredAt)
            Notes = reading.Notes
        }

    let glucoseText (draft: GlucoseDraft) : string =
        match draft.Value with
        | EnteredGlucose text -> text
        | ConvertedGlucose value ->
            (GlucoseValue.inUnit draft.Unit value).ToString("0.####", CultureInfo.CurrentCulture)

    let private parseDecimal (text: string) =
        match
            Decimal.TryParse(
                text.Trim().Replace(',', '.'),
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture
            )
        with
        | true, value -> Ok value
        | _ -> Error "Enter a number using a decimal point or comma."

    let private parseInt name (text: string) =
        match Int32.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture) with
        | true, value -> Ok value
        | _ -> Error $"Enter a whole number for {name}."

    let private parseGlucose (draft: GlucoseDraft) =
        match draft.Value with
        | ConvertedGlucose value -> Ok value
        | EnteredGlucose text -> parseDecimal text |> Result.bind (GlucoseValue.create draft.Unit)

    let private validate now draft =
        let localTime =
            DateTime.SpecifyKind(draft.Date.Date + draft.Time, DateTimeKind.Unspecified)

        let offset =
            draft.Offset |> Option.defaultValue (TimeZoneInfo.Local.GetUtcOffset localTime)

        let recordedAt = DateTimeOffset(localTime, offset)

        let measurement =
            match draft.Measurement with
            | DraftGlucose glucose -> parseGlucose glucose |> Result.map (fun v -> Glucose(v, glucose.Context))
            | DraftPressure pressure ->
                let pulse =
                    if String.IsNullOrWhiteSpace pressure.Pulse then
                        Ok None
                    else
                        parseInt "pulse" pressure.Pulse |> Result.map Some

                match parseInt "systolic" pressure.Systolic, parseInt "diastolic" pressure.Diastolic, pulse with
                | Ok s, Ok d, Ok p -> BloodPressure.create s d p |> Result.map Pressure
                | Error e, _, _
                | _, Error e, _
                | _, _, Error e -> Error e

        if TimeZoneInfo.Local.IsInvalidTime localTime then
            Error "This local time does not exist because the clock changed. Choose another time."
        elif recordedAt > now then
            Error "A reading cannot be dated in the future."
        elif draft.Notes.Length > 2000 then
            Error "Keep notes within 2000 characters."
        else
            measurement
            |> Result.map (fun value ->
                {
                    Id = draft.Id
                    MeasuredAt = recordedAt
                    Measurement = value
                    Notes = draft.Notes.Trim()
                })

    let private persist journal model =
        { model with
            Pending = Some journal
            Error = None
            Notice = None
        },
        [ Persist journal ]

    let update (msg: Msg) (model: Model) : Model * Effect list =
        let changeDraft fn =
            { model with
                Draft = model.Draft |> Option.map fn
                Error = None
                Notice = None
            },
            []

        match msg with
        | Loaded result when model.Store = Loading ->
            { model with
                Store =
                    (match result with
                     | Ok j -> Ready j
                     | Error e -> LoadFailed e)
            },
            []
        | RetryLoad when
            (match model.Store with
             | LoadFailed _ -> true
             | _ -> false)
            ->
            { model with
                Store = Loading
                Error = None
            },
            [ Load ]
        | Persisted result when model.Pending.IsSome ->
            match result, model.Pending with
            | Ok(), Some value ->
                { model with
                    Store = Ready value
                    Pending = None
                    Draft = None
                    Confirmation = None
                    Notice = Some "Saved on this device."
                    Error = None
                },
                []
            | Error e, _ ->
                { model with
                    Pending = None
                    Error = Some e
                },
                []
            | _ -> model, []
        | Exported result ->
            { model with
                Exporting = false
                Error =
                    (match result with
                     | Error e -> Some e
                     | _ -> None)
            },
            []
        | _ when busy model -> model, []
        | _ when
            (match model.Store with
             | Ready _ -> false
             | _ -> true)
            ->
            model, []
        | Navigate screen ->
            { model with
                Screen = screen
                Draft = None
                Confirmation = None
                Error = None
                Notice = None
            },
            []
        | RequestAdd kind -> model, [ NewReadingTime kind ]
        | RequestSave -> model, [ SaveTime ]
        | AddReading(kind, id, now) ->
            let measurement =
                match kind with
                | GlucoseKind ->
                    DraftGlucose
                        {
                            Value = EnteredGlucose ""
                            Unit = (journal model).Unit
                            Context = Unspecified
                        }
                | BloodPressureKind ->
                    DraftPressure
                        {
                            Systolic = ""
                            Diastolic = ""
                            Pulse = ""
                        }

            { model with
                Draft =
                    Some
                        {
                            Id = id
                            Measurement = measurement
                            Date = now.LocalDateTime.Date
                            Time = TimeSpan(now.LocalDateTime.Hour, now.LocalDateTime.Minute, 0)
                            Offset = Some(now.ToLocalTime().Offset)
                            Notes = ""
                        }
                Error = None
                Notice = None
            },
            []
        | EditReading id ->
            { model with
                Draft =
                    (journal model).Readings
                    |> List.tryFind (fun r -> r.Id = id)
                    |> Option.map (draftFromReading (journal model).Unit)
                Error = None
                Notice = None
            },
            []
        | ChangeField(field, text) ->
            let text = Option.ofObj text |> Option.defaultValue ""

            changeDraft (fun d ->
                match field, d.Measurement with
                | GlucoseText, DraftGlucose glucose ->
                    { d with
                        Measurement =
                            DraftGlucose
                                { glucose with
                                    Value = EnteredGlucose text
                                }
                    }
                | SystolicText, DraftPressure pressure ->
                    { d with
                        Measurement = DraftPressure { pressure with Systolic = text }
                    }
                | DiastolicText, DraftPressure pressure ->
                    { d with
                        Measurement = DraftPressure { pressure with Diastolic = text }
                    }
                | PulseText, DraftPressure pressure ->
                    { d with
                        Measurement = DraftPressure { pressure with Pulse = text }
                    }
                | NotesText, _ -> { d with Notes = text }
                | _ -> d)
        | ChangeDate date -> changeDraft (fun d -> { d with Date = date; Offset = None })
        | ChangeTime time -> changeDraft (fun d -> { d with Time = time; Offset = None })
        | ChangeContext context ->
            changeDraft (fun d ->
                match d.Measurement with
                | DraftGlucose glucose ->
                    { d with
                        Measurement = DraftGlucose { glucose with Context = context }
                    }
                | DraftPressure _ -> d)
        | ChangeDraftUnit unit ->
            changeDraft (fun d ->
                match d.Measurement with
                | DraftGlucose glucose ->
                    let value =
                        parseGlucose glucose
                        |> Result.map ConvertedGlucose
                        |> Result.defaultValue glucose.Value

                    { d with
                        Measurement =
                            DraftGlucose
                                { glucose with
                                    Unit = unit
                                    Value = value
                                }
                    }
                | DraftPressure _ -> d)
        | CancelDraft ->
            { model with
                Draft = None
                Error = None
            },
            []
        | SaveDraft now ->
            match model.Draft with
            | None -> model, []
            | Some draft ->
                match validate now draft with
                | Error e -> { model with Error = Some e }, []
                | Ok reading ->
                    persist
                        { journal model with
                            Readings =
                                reading
                                :: ((journal model).Readings |> List.filter (fun r -> r.Id <> reading.Id))
                        }
                        model
        | SetUnit unit -> persist { journal model with Unit = unit } model
        | SetFilter filter ->
            { model with
                Filter = filter
                HistoryLimit = 40
            },
            []
        | SetPeriod period -> { model with Period = period }, []
        | ShowMore ->
            { model with
                HistoryLimit = model.HistoryLimit + 40
            },
            []
        | AskConfirmation confirmation ->
            { model with
                Confirmation = Some confirmation
                Error = None
                Notice = None
            },
            []
        | CancelConfirmation ->
            { model with
                Confirmation = None
                Error = None
            },
            []
        | Confirm ->
            match model.Confirmation with
            | Some(DeleteReading id) ->
                persist
                    { journal model with
                        Readings = (journal model).Readings |> List.filter (fun r -> r.Id <> id)
                    }
                    model
            | Some ClearJournal -> persist { journal model with Readings = [] } model
            | None -> model, []
        | Export when not (List.isEmpty (journal model).Readings) ->
            { model with
                Exporting = true
                Error = None
            },
            [ ShareCsv(Csv.export (journal model)) ]
        | _ -> model, []
