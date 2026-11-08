namespace FsHealth.App

open System
open Fabulous
open Fabulous.Maui
open FsHealth
open Microsoft.Maui
open Microsoft.Maui.Controls
open Microsoft.Maui.Graphics

open type Fabulous.Maui.View

module App =
    let private ink = Color.FromArgb "#153E42"
    let private muted = Color.FromArgb "#657A7C"
    let private teal = Color.FromArgb "#167D8D"
    let private background = Color.FromArgb "#F4F6F2"
    let private line = Color.FromArgb "#E0E7E3"
    let private coral = Color.FromArgb "#AD4936"

    let private command effect =
        match effect with
        | Load ->
            async { let! result = DeviceServices.load () in return Loaded result }
            |> Cmd.OfAsync.msg
        | Persist journal ->
            async { let! result = DeviceServices.save journal in return Persisted result }
            |> Cmd.OfAsync.msg
        | ShareCsv csv ->
            async { let! result = DeviceServices.share csv in return Exported result }
            |> Cmd.OfAsync.msg
        | NewReadingTime kind ->
            Cmd.ofEffect (fun dispatch -> dispatch (AddReading(kind, ReadingId(Guid.NewGuid()), DateTimeOffset.Now)))
        | SaveTime -> Cmd.ofEffect (fun dispatch -> dispatch (SaveDraft DateTimeOffset.Now))

    let private text value =
        Label(value).font(size = 14., fontFamily = "OpenSansRegular").textColor(ink)

    let private hint value =
        Label(value).font(size = 12., fontFamily = "OpenSansRegular").textColor(muted)

    let private heading value =
        Label(value).font(size = 25., fontFamily = "OpenSansSemibold").textColor(ink)

    let private title value =
        Label(value).font(size = 16., fontFamily = "OpenSansSemibold").textColor(ink)

    let private primary (value: string) (message: Msg) =
        Button(value, message)
            .font(size = 14., fontFamily = "OpenSansSemibold")
            .background(SolidColorBrush(teal))
            .textColor(Colors.White)
            .cornerRadius(14)
            .minimumHeight(50.)

    let private secondary (value: string) (message: Msg) =
        Button(value, message)
            .font(size = 12., fontFamily = "OpenSansSemibold")
            .background(SolidColorBrush(Colors.White))
            .textColor(ink)
            .borderColor(line)
            .borderWidth(1.)
            .cornerRadius(12)
            .minimumHeight(46.)

    let private chip (value: string) selected (message: Msg) =
        Button(value, message)
            .font(size = 12., fontFamily = "OpenSansSemibold")
            .background(SolidColorBrush(if selected then ink else Colors.White))
            .textColor(if selected then Colors.White else ink)
            .cornerRadius(12)
            .minimumHeight(44.)
            .padding(Thickness(12., 4.))

    let private card content =
        Border(content)
            .stroke(SolidColorBrush(line))
            .strokeThickness(1.)
            .strokeShape(RoundRectangle(CornerRadius(20.)))
            .background(SolidColorBrush(Colors.White))
            .padding(18.)

    let private readingCard unit (reading: Reading) =
        card (
            VStack(spacing = 9.) {
                hint (
                    if Reading.kind reading = GlucoseKind then
                        "GLUCOSE"
                    else
                        "BLOOD PRESSURE"
                )

                hint (reading.MeasuredAt.LocalDateTime.ToString("MMM d · h:mm tt"))

                HStack(spacing = 8.) {
                    Label(Reading.valueLabel unit reading)
                        .font(size = 29., fontFamily = "OpenSansSemibold")
                        .textColor(ink)

                    hint (
                        if Reading.kind reading = GlucoseKind then
                            GlucoseValue.unitLabel unit
                        else
                            "mmHg"
                    )
                }

                hint (Reading.detailLabel reading)

                if not (String.IsNullOrWhiteSpace reading.Notes) then
                    text reading.Notes

                HStack(spacing = 8.) {
                    secondary "Edit" (EditReading reading.Id)
                    secondary "Delete" (AskConfirmation(DeleteReading reading.Id))
                }
            }
        )

    let private periodSelector model =
        HStack(spacing = 8.) {
            chip "7 days" (model.Period = Week) (SetPeriod Week)
            chip "30 days" (model.Period = Month) (SetPeriod Month)
            chip "90 days" (model.Period = Quarter) (SetPeriod Quarter)
        }

    let private chart model kind now =
        let journal = State.journal model

        let readings =
            Journal.withinDays now (State.days model.Period) journal
            |> List.filter (fun r -> Reading.kind r = kind)

        card (
            VStack(spacing = 12.) {
                title (
                    if kind = GlucoseKind then
                        "Glucose trend"
                    else
                        "Blood pressure trend"
                )

                hint (
                    if kind = GlucoseKind then
                        GlucoseValue.unitLabel journal.Unit
                    else
                        "mmHg · Teal: systolic · Coral: diastolic"
                )

                if List.isEmpty readings then
                    text "Your trend starts with a reading."
                    hint "Add readings in this period to see them here."
                else
                    GraphicsView(Charts.drawable kind journal.Unit now (State.days model.Period) readings)
                        .height(190.)
                        .semantics(
                            description =
                                $"{readings.Length} measurements in this period. Individual values are available in the journal."
                        )

                    hint $"{readings.Length} readings · Points show individual measurements."
            }
        )

    let private overview model (now: DateTimeOffset) =
        let journal = State.journal model

        let latest kind =
            Journal.ordered journal |> List.tryFind (fun r -> Reading.kind r = kind)

        VStack(spacing = 18.) {
            hint (now.LocalDateTime.ToString("dddd, MMMM d").ToUpperInvariant())
            heading "A little clarity, every day."
            text "Your glucose and blood pressure, together."

            card (
                VStack(spacing = 12.) {
                    hint "YOUR LATEST READINGS"

                    for kind, label in [ GlucoseKind, "Glucose"; BloodPressureKind, "Blood pressure" ] do
                        title label

                        match latest kind with
                        | Some reading ->
                            HStack(spacing = 8.) {
                                Label(Reading.valueLabel journal.Unit reading)
                                    .font(size = 32., fontFamily = "OpenSansSemibold")
                                    .textColor(ink)

                                hint (
                                    if kind = GlucoseKind then
                                        GlucoseValue.unitLabel journal.Unit
                                    else
                                        "mmHg"
                                )
                            }

                            hint (
                                reading.MeasuredAt.LocalDateTime.ToString("MMM d · h:mm tt")
                                + " · "
                                + Reading.detailLabel reading
                            )
                        | None -> hint "No readings yet"

                    primary "+ Log glucose" (RequestAdd GlucoseKind)
                    secondary "+ Log blood pressure" (RequestAdd BloodPressureKind)
                }
            )

            title "Your recent rhythm"
            periodSelector model
            chart model GlucoseKind now
            chart model BloodPressureKind now
            title "Recent entries"

            if List.isEmpty journal.Readings then
                hint "A quiet beginning. Add your first measurement above."
            else
                for reading in Journal.ordered journal |> List.truncate 3 do
                    readingCard journal.Unit reading

                secondary "View all entries" (Navigate History)

            hint
                "A personal record of measurements from your meter and cuff. This app does not measure, diagnose, or recommend treatment."
        }

    let private history model =
        let readings = State.history model

        VStack(spacing = 16.) {
            heading "Your journal"
            hint $"{readings.Length} entries · Most recent first"

            HStack(spacing = 8.) {
                chip "All" (model.Filter = AllReadings) (SetFilter AllReadings)
                chip "Glucose" (model.Filter = OnlyGlucose) (SetFilter OnlyGlucose)
                chip "Pressure" (model.Filter = OnlyPressure) (SetFilter OnlyPressure)
            }

            if List.isEmpty readings then
                card (
                    VStack(spacing = 10.) {
                        title "Room for your routine"
                        text "Log a reading and it will appear here."
                    }
                )
            else
                for reading in readings |> List.truncate model.HistoryLimit do
                    readingCard (State.journal model).Unit reading

                if readings.Length > model.HistoryLimit then
                    secondary "Show 40 more" ShowMore

            primary "+ Log glucose" (RequestAdd GlucoseKind)
            secondary "+ Log blood pressure" (RequestAdd BloodPressureKind)
        }

    let private trends model now =
        let journal = State.journal model
        let readings = Journal.withinDays now (State.days model.Period) journal

        let glucose =
            Journal.glucoseAverage journal.Unit readings
            |> Option.map (fun v -> v.ToString(if journal.Unit = MgPerDl then "0.#" else "0.0"))
            |> Option.defaultValue "—"

        let pressure =
            Journal.pressureAverage readings
            |> Option.map (fun (s, d) -> s.ToString("0") + "/" + d.ToString("0"))
            |> Option.defaultValue "—"

        VStack(spacing = 16.) {
            heading "The bigger picture"
            text "See how your recorded measurements change over time."
            periodSelector model

            card (
                VStack(spacing = 10.) {
                    hint "AVERAGES FOR THIS PERIOD"
                    title ($"Glucose  {glucose} " + GlucoseValue.unitLabel journal.Unit)
                    title $"Blood pressure  {pressure} mmHg"

                    hint
                        "Arithmetic averages of logged readings. Meal timing and measurement frequency can affect comparisons."
                }
            )

            chart model GlucoseKind now
            chart model BloodPressureKind now
        }

    let private settings model =
        let journal = State.journal model

        VStack(spacing = 18.) {
            heading "Make it yours"

            card (
                VStack(spacing = 12.) {
                    title "Glucose units"
                    text "Changing units converts your saved readings."

                    HStack(spacing = 8.) {
                        chip "mg/dL" (journal.Unit = MgPerDl) (SetUnit MgPerDl)
                        chip "mmol/L" (journal.Unit = MmolPerL) (SetUnit MmolPerL)
                    }
                }
            )

            card (
                VStack(spacing = 12.) {
                    title "Your data stays with you"

                    text
                        "Readings are encrypted on this device. The key is held in the operating system’s secure storage. There is no account, analytics, or cloud sync."

                    text
                        "Export a CSV for your own records or to share with your care team. The exported file contains unencrypted health information."

                    (primary "Export & share CSV" Export).isEnabled(not (List.isEmpty journal.Readings))
                    hint $"{journal.Readings.Length} saved readings · Export includes the whole journal."

                    hint
                        "Export regularly: uninstalling the app, losing your device, or losing its encryption key can make the journal unrecoverable. The last CSV remains in the app cache until your next export or an OS cache cleanup."
                }
            )

            card (
                VStack(spacing = 10.) {
                    title "About FsHealth"

                    text
                        "Enter readings taken with your glucose meter and blood pressure cuff. Automatic sensor connections and Apple Health / Health Connect are not included."

                    hint
                        "Input limits catch typing mistakes; they are not clinical target ranges. Use targets and care instructions agreed with your clinician."

                    hint "FsHealth 1.0 · Built with F# and Fabulous for .NET MAUI 10"
                }
            )

            (secondary "Delete all readings…" (AskConfirmation ClearJournal))
                .textColor(coral)
                .isEnabled(not (List.isEmpty journal.Readings))
        }

    let private input label value field keyboard =
        VStack(spacing = 4.) {
            hint label

            Entry(value, fun value -> ChangeField(field, value))
                .keyboard(keyboard)
                .font(size = 22., fontFamily = "OpenSansRegular")
                .textColor(ink)
                .placeholder(label)
                .minimumHeight(54.)
                .isTextPredictionEnabled(false)
        }

    let private editor model (draft: Draft) =
        let existing =
            (State.journal model).Readings |> List.exists (fun r -> r.Id = draft.Id)

        VStack(spacing = 16.) {
            heading (
                if existing then
                    "Edit your reading"
                else
                    "A moment for your health"
            )

            text (
                match draft.Measurement with
                | DraftGlucose _ -> "Log glucose"
                | DraftPressure _ -> "Log blood pressure"
            )

            card (
                VStack(spacing = 14.) {
                    match draft.Measurement with
                    | DraftGlucose glucose ->
                        HStack(spacing = 8.) {
                            chip "mg/dL" (glucose.Unit = MgPerDl) (ChangeDraftUnit MgPerDl)
                            chip "mmol/L" (glucose.Unit = MmolPerL) (ChangeDraftUnit MmolPerL)
                        }

                        input
                            ("Glucose · " + GlucoseValue.unitLabel glucose.Unit)
                            (State.glucoseText glucose)
                            GlucoseText
                            Keyboard.Numeric

                        hint "MEAL CONTEXT"

                        HStack(spacing = 6.) {
                            chip "Fasting" (glucose.Context = Fasting) (ChangeContext Fasting)
                            chip "Before meal" (glucose.Context = BeforeMeal) (ChangeContext BeforeMeal)
                        }

                        HStack(spacing = 6.) {
                            chip "After meal" (glucose.Context = AfterMeal) (ChangeContext AfterMeal)
                            chip "Bedtime" (glucose.Context = Bedtime) (ChangeContext Bedtime)
                            chip "Other" (glucose.Context = Unspecified) (ChangeContext Unspecified)
                        }
                    | DraftPressure pressure ->
                        input "Systolic · mmHg" pressure.Systolic SystolicText Keyboard.Numeric
                        input "Diastolic · mmHg" pressure.Diastolic DiastolicText Keyboard.Numeric
                        input "Pulse · bpm (optional)" pressure.Pulse PulseText Keyboard.Numeric

                    hint "DATE & TIME"

                    DatePicker(DateTime(1900, 1, 1), DateTime.Today, draft.Date, ChangeDate)
                        .textColor(ink)
                        .format("ddd, MMM d, yyyy")

                    TimePicker(draft.Time, ChangeTime).textColor(ink)
                    hint "NOTES (OPTIONAL)"

                    Editor(draft.Notes, fun value -> ChangeField(NotesText, value))
                        .placeholder("Anything you’d like to remember")
                        .height(100.)
                        .textColor(ink)
                        .maxLength(2000)

                    hint "Use the value shown by your meter or cuff."
                }
            )

            primary (if model.Pending.IsSome then "Saving…" else "Save reading") RequestSave
            secondary "Cancel" CancelDraft
        }

    let private confirmation model confirmation =
        card (
            VStack(spacing = 14.) {
                title (
                    if confirmation = ClearJournal then
                        "Delete every reading?"
                    else
                        "Delete this reading?"
                )

                text "This permanently removes the selected data from this device. Exported copies are unaffected."
                (primary "Delete permanently" Confirm).background(SolidColorBrush(coral))
                secondary "Keep my readings" CancelConfirmation

                if model.Pending.IsSome then
                    hint "Saving…"
            }
        )

    let view model =
        let now = DateTimeOffset.Now

        Application(
            ContentPage(
                Grid(coldefs = [ Star ], rowdefs = [ Auto; Star; Auto ]) {
                    (Grid(coldefs = [ Star; Auto ], rowdefs = [ Auto ]) {
                        Label("fshealth").font(size = 24., fontFamily = "OpenSansSemibold").textColor(ink)

                        Label("YOUR DAILY CHECK-IN")
                            .font(size = 9., fontFamily = "OpenSansSemibold")
                            .textColor(teal)
                            .gridColumn(1)
                            .centerVertical()
                    })
                        .padding(22., 16., 22., 12.)
                        .gridRow(0)

                    ScrollView(
                        (VStack(spacing = 16.) {
                            match model.Store with
                            | Loading -> heading "Opening your journal…"
                            | LoadFailed error ->
                                heading "Your journal is protected"
                                text error
                                primary "Retry opening" RetryLoad
                            | Ready _ ->
                                match model.Draft, model.Confirmation with
                                | _, Some value -> confirmation model value
                                | Some draft, None -> editor model draft
                                | None, None ->
                                    match model.Screen with
                                    | Overview -> overview model now
                                    | History -> history model
                                    | Trends -> trends model now
                                    | Settings -> settings model

                            match model.Error with
                            | Some error -> Label(error).textColor(coral).font(size = 14.)
                            | None -> ()

                            match model.Notice with
                            | Some notice -> Label(notice).textColor(teal).font(size = 13.)
                            | None -> ()

                            if State.busy model then
                                hint (
                                    if model.Exporting then
                                        "Opening share sheet…"
                                    else
                                        "Saving securely…"
                                )
                        })
                            .padding(22., 8., 22., 28.)
                            .isEnabled(not (State.busy model))
                    )
                        .gridRow(1)

                    (Grid(coldefs = [ Star; Star; Star; Star ], rowdefs = [ Auto ]) {
                        for index, screen, label in
                            [
                                0, Overview, "Today"
                                1, History, "Journal"
                                2, Trends, "Trends"
                                3, Settings, "Settings"
                            ] do
                            Button(label, Navigate screen)
                                .font(size = 12., fontFamily = "OpenSansSemibold")
                                .textColor(if model.Screen = screen then teal else muted)
                                .background(SolidColorBrush(Colors.White))
                                .minimumHeight(56.)
                                .gridColumn(index)
                    })
                        .background(SolidColorBrush(Colors.White))
                        .gridRow(2)
                        .isEnabled(not (State.busy model) && model.Draft.IsNone && model.Confirmation.IsNone)
                }
            )
                .background(SolidColorBrush(background))
        )

    let program =
        Program.statefulWithCmdMsg State.init State.update command
        |> Program.withView view
