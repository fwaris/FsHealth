namespace FsHealth

open System
open System.Globalization

type GlucoseUnit =
    | MgPerDl
    | MmolPerL

type MealContext =
    | Fasting
    | BeforeMeal
    | AfterMeal
    | Bedtime
    | Unspecified

type ReadingKind =
    | GlucoseKind
    | BloodPressureKind

type GlucoseValue = private GlucoseValue of decimal

type BloodPressure =
    private
        {
            Systolic: int
            Diastolic: int
            Pulse: int option
        }

type Measurement =
    | Glucose of GlucoseValue * MealContext
    | Pressure of BloodPressure

type ReadingId = ReadingId of Guid

type Reading =
    {
        Id: ReadingId
        MeasuredAt: DateTimeOffset
        Measurement: Measurement
        Notes: string
    }

type Journal =
    {
        Readings: Reading list
        Unit: GlucoseUnit
    }

module GlucoseValue =
    let create (unit: GlucoseUnit) (value: decimal) : Result<GlucoseValue, string> =
        let maximum = if unit = MgPerDl then 2000m else 2000m / 18m

        if value <= 0m || value > maximum then
            Error "Enter a positive glucose value up to 2000 mg/dL (111.1 mmol/L)."
        else
            Ok(GlucoseValue(if unit = MgPerDl then value else value * 18m))

    let inUnit (unit: GlucoseUnit) (GlucoseValue value) : decimal =
        if unit = MgPerDl then value else value / 18m

    let unitLabel (unit: GlucoseUnit) : string =
        match unit with
        | MgPerDl -> "mg/dL"
        | MmolPerL -> "mmol/L"

    let format (unit: GlucoseUnit) (value: GlucoseValue) : string =
        (inUnit unit value).ToString((if unit = MgPerDl then "0.#" else "0.0"), CultureInfo.CurrentCulture)

module BloodPressure =
    let create (systolic: int) (diastolic: int) (pulse: int option) : Result<BloodPressure, string> =
        if systolic <= 0 || systolic > 400 || diastolic <= 0 || diastolic > 300 then
            Error "Enter positive pressure values: systolic up to 400 and diastolic up to 300 mmHg."
        elif systolic <= diastolic then
            Error "Systolic must be greater than diastolic. Check the entered values."
        elif pulse |> Option.exists (fun p -> p <= 0 || p > 350) then
            Error "Enter a pulse from 1 to 350 bpm, or leave it blank."
        else
            Ok
                {
                    Systolic = systolic
                    Diastolic = diastolic
                    Pulse = pulse
                }

    let systolic (value: BloodPressure) : int = value.Systolic
    let diastolic (value: BloodPressure) : int = value.Diastolic
    let pulse (value: BloodPressure) : int option = value.Pulse

module Reading =
    let kind (reading: Reading) : ReadingKind =
        match reading.Measurement with
        | Glucose _ -> GlucoseKind
        | Pressure _ -> BloodPressureKind

    let contextLabel (context: MealContext) : string =
        match context with
        | Fasting -> "Fasting"
        | BeforeMeal -> "Before meal"
        | AfterMeal -> "After meal"
        | Bedtime -> "Bedtime"
        | Unspecified -> "Unspecified"

    let valueLabel (unit: GlucoseUnit) (reading: Reading) : string =
        match reading.Measurement with
        | Glucose(value, _) -> GlucoseValue.format unit value
        | Pressure pressure -> $"{BloodPressure.systolic pressure}/{BloodPressure.diastolic pressure}"

    let detailLabel (reading: Reading) : string =
        match reading.Measurement with
        | Glucose(_, context) -> contextLabel context
        | Pressure pressure ->
            BloodPressure.pulse pressure
            |> Option.map (fun p -> $"Pulse {p} bpm")
            |> Option.defaultValue "Blood pressure"

module Journal =
    let empty: Journal = { Readings = []; Unit = MgPerDl }

    let ordered (journal: Journal) : Reading list =
        journal.Readings |> List.sortByDescending (fun r -> r.MeasuredAt, r.Id)

    let withinDays (now: DateTimeOffset) (days: int) (journal: Journal) : Reading list =
        let firstDay = now.LocalDateTime.Date.AddDays(float (1 - days))

        ordered journal
        |> List.filter (fun r -> r.MeasuredAt.LocalDateTime >= firstDay && r.MeasuredAt <= now)

    let glucoseAverage (unit: GlucoseUnit) (readings: Reading list) : decimal option =
        let values =
            readings
            |> List.choose (fun r ->
                match r.Measurement with
                | Glucose(v, _) -> Some(GlucoseValue.inUnit unit v)
                | _ -> None)

        if List.isEmpty values then
            None
        else
            Some(List.average values)

    let pressureAverage (readings: Reading list) : (decimal * decimal) option =
        let values =
            readings
            |> List.choose (fun r ->
                match r.Measurement with
                | Pressure p -> Some p
                | _ -> None)

        if List.isEmpty values then
            None
        else
            Some(
                values |> List.averageBy (BloodPressure.systolic >> decimal),
                values |> List.averageBy (BloodPressure.diastolic >> decimal)
            )

module Csv =
    let private cell (value: string) =
        // Prevent spreadsheet formulas when an exported note is opened in Excel.
        let safe =
            if
                value.TrimStart().StartsWith("=")
                || value.TrimStart().StartsWith("+")
                || value.TrimStart().StartsWith("-")
                || value.TrimStart().StartsWith("@")
                || value.StartsWith("\t")
                || value.StartsWith("\r")
            then
                "'" + value
            else
                value

        "\"" + safe.Replace("\"", "\"\"") + "\""

    let export (journal: Journal) : string =
        let number (value: decimal) =
            value.ToString("0.####", CultureInfo.InvariantCulture)

        let rows =
            Journal.ordered journal
            |> List.rev
            |> List.map (fun reading ->
                let (ReadingId id) = reading.Id

                let data =
                    match reading.Measurement with
                    | Glucose(v, context) ->
                        [
                            "Glucose"
                            number (GlucoseValue.inUnit journal.Unit v)
                            GlucoseValue.unitLabel journal.Unit
                            ""
                            ""
                            ""
                            Reading.contextLabel context
                        ]
                    | Pressure p ->
                        [
                            "Blood pressure"
                            ""
                            "mmHg"
                            string (BloodPressure.systolic p)
                            string (BloodPressure.diastolic p)
                            BloodPressure.pulse p |> Option.map string |> Option.defaultValue ""
                            ""
                        ]

                [ id.ToString(); reading.MeasuredAt.ToString("O"); yield! data; reading.Notes ]
                |> List.map cell
                |> String.concat ",")

        String.concat "\r\n" ("Id,MeasuredAt,Type,Glucose,Unit,Systolic,Diastolic,Pulse,Context,Notes" :: rows)
        + "\r\n"
