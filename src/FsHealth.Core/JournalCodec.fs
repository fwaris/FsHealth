namespace FsHealth

open System
open System.IO
open System.Text.Json

module JournalCodec =
    let private contextCode =
        function
        | Fasting -> "fasting"
        | BeforeMeal -> "before"
        | AfterMeal -> "after"
        | Bedtime -> "bedtime"
        | Unspecified -> "unspecified"

    // Explicit JSON keeps the mobile release builds independent of reflection metadata.
    let encode (journal: Journal) : byte array =
        use output = new MemoryStream()
        use writer = new Utf8JsonWriter(output)
        writer.WriteStartObject()
        writer.WriteNumber("Version", 1)
        writer.WriteString("Unit", GlucoseValue.unitLabel journal.Unit)
        writer.WriteStartArray("Readings")

        for reading in journal.Readings do
            let (ReadingId id) = reading.Id
            writer.WriteStartObject()
            writer.WriteString("Id", id)
            writer.WriteString("MeasuredAt", reading.MeasuredAt)
            writer.WriteString("Notes", reading.Notes)

            match reading.Measurement with
            | Glucose(value, context) ->
                writer.WriteString("Kind", "glucose")
                writer.WriteNumber("GlucoseMgDl", GlucoseValue.inUnit MgPerDl value)
                writer.WriteString("Context", contextCode context)
            | Pressure pressure ->
                writer.WriteString("Kind", "pressure")
                writer.WriteNumber("Systolic", BloodPressure.systolic pressure)
                writer.WriteNumber("Diastolic", BloodPressure.diastolic pressure)
                writer.WriteNumber("Pulse", BloodPressure.pulse pressure |> Option.defaultValue 0)

            writer.WriteEndObject()

        writer.WriteEndArray()
        writer.WriteEndObject()
        writer.Flush()
        output.ToArray()

    let private readReading (element: JsonElement) : Result<Reading, string> =
        let property (name: string) = element.GetProperty name
        let id = (property "Id").GetGuid()
        let measuredAt = (property "MeasuredAt").GetDateTimeOffset()
        let notes = (property "Notes").GetString()

        let measurement =
            match (property "Kind").GetString() with
            | "glucose" ->
                let context =
                    match (property "Context").GetString() with
                    | "fasting" -> Ok Fasting
                    | "before" -> Ok BeforeMeal
                    | "after" -> Ok AfterMeal
                    | "bedtime" -> Ok Bedtime
                    | "unspecified" -> Ok Unspecified
                    | _ -> Error "Unknown meal context."

                match GlucoseValue.create MgPerDl ((property "GlucoseMgDl").GetDecimal()), context with
                | Ok value, Ok meal -> Ok(Glucose(value, meal))
                | Error e, _
                | _, Error e -> Error e
            | "pressure" ->
                let pulse = (property "Pulse").GetInt32()

                BloodPressure.create
                    ((property "Systolic").GetInt32())
                    ((property "Diastolic").GetInt32())
                    (if pulse = 0 then None else Some pulse)
                |> Result.map Pressure
            | _ -> Error "Unknown reading type."

        if
            id = Guid.Empty
            || measuredAt = DateTimeOffset.MinValue
            || isNull notes
            || notes.Length > 2000
        then
            Error "Invalid saved reading."
        else
            measurement
            |> Result.map (fun value ->
                {
                    Id = ReadingId id
                    MeasuredAt = measuredAt
                    Measurement = value
                    Notes = notes
                })

    let decode (bytes: byte array) : Result<Journal, string> =
        try
            use document = JsonDocument.Parse(ReadOnlyMemory bytes)
            let root = document.RootElement

            if
                root.ValueKind <> JsonValueKind.Object
                || root.GetProperty("Version").GetInt32() <> 1
            then
                Error "Unsupported or damaged journal."
            else
                let unit =
                    match root.GetProperty("Unit").GetString() with
                    | "mg/dL" -> Ok MgPerDl
                    | "mmol/L" -> Ok MmolPerL
                    | _ -> Error "Unknown glucose unit."

                let readings =
                    root.GetProperty("Readings").EnumerateArray()
                    |> Seq.fold
                        (fun state row ->
                            match state with
                            | Error e -> Error e
                            | Ok values -> readReading row |> Result.map (fun value -> value :: values))
                        (Ok [])

                match unit, readings with
                | Ok u, Ok values when (values |> List.map (fun r -> r.Id) |> Set.ofList |> Set.count) = values.Length ->
                    Ok { Unit = u; Readings = List.rev values }
                | Ok _, Ok _ -> Error "Duplicate saved reading identifiers."
                | Error e, _
                | _, Error e -> Error e
        with
        | :? JsonException
        | :? FormatException
        | :? Collections.Generic.KeyNotFoundException
        | :? InvalidOperationException
        | :? OverflowException -> Error "The saved journal could not be read."
