namespace FsHealth.App

open System
open FsHealth
open Microsoft.Maui.Graphics

module Charts =
    let private teal = Color.FromArgb "#167D8D"
    let private coral = Color.FromArgb "#CB6B52"

    let drawable
        (kind: ReadingKind)
        (unit: GlucoseUnit)
        (now: DateTimeOffset)
        (days: int)
        (readings: Reading list)
        : IDrawable =
        let points selector =
            readings
            |> List.choose (fun reading ->
                selector reading.Measurement
                |> Option.map (fun value -> reading.MeasuredAt, value))
            |> List.sortBy fst

        let series =
            match kind with
            | GlucoseKind ->
                [
                    teal,
                    points (function
                        | Glucose(v, _) -> Some(float (GlucoseValue.inUnit unit v))
                        | _ -> None)
                ]
            | BloodPressureKind ->
                [
                    teal,
                    points (function
                        | Pressure p -> Some(float (BloodPressure.systolic p))
                        | _ -> None)
                    coral,
                    points (function
                        | Pressure p -> Some(float (BloodPressure.diastolic p))
                        | _ -> None)
                ]

        { new IDrawable with
            member _.Draw(canvas, bounds) =
                canvas.SaveState()

                try
                    canvas.Antialias <- true
                    let values = series |> List.collect (snd >> List.map snd)

                    if not (List.isEmpty values) then
                        let minimum = List.min values
                        let maximum = List.max values
                        let padding = max 5. ((maximum - minimum) * 0.18)
                        let low, high = max 0. (minimum - padding), maximum + padding
                        let left, top = bounds.X + 42.f, bounds.Y + 12.f
                        let width, height = max 1.f (bounds.Width - 54.f), max 1.f (bounds.Height - 42.f)

                        let first =
                            DateTimeOffset(now.LocalDateTime.Date.AddDays(float (1 - days))).ToUnixTimeSeconds()
                            |> float

                        let last = float (now.ToUnixTimeSeconds())

                        let x (time: DateTimeOffset) =
                            left
                            + float32 ((float (time.ToUnixTimeSeconds()) - first) / max 1. (last - first))
                              * width

                        let y value =
                            top + height - float32 ((value - low) / (high - low)) * height

                        canvas.FontSize <- 10.f
                        canvas.FontColor <- Color.FromArgb "#657A7C"

                        for i in 0..3 do
                            let value = low + (high - low) * float i / 3.
                            canvas.StrokeColor <- Color.FromArgb "#E6EDEB"
                            canvas.StrokeSize <- 1.f
                            canvas.DrawLine(left, y value, left + width, y value)

                            canvas.DrawString(
                                value.ToString(if unit = MmolPerL && kind = GlucoseKind then "0.0" else "0"),
                                bounds.X,
                                y value - 8.f,
                                34.f,
                                18.f,
                                HorizontalAlignment.Right,
                                VerticalAlignment.Center
                            )

                        for color, points in series do
                            canvas.StrokeColor <- color
                            canvas.StrokeSize <- 2.5f

                            points
                            |> List.pairwise
                            |> List.iter (fun ((t1, v1), (t2, v2)) -> canvas.DrawLine(x t1, y v1, x t2, y v2))

                            canvas.FillColor <- color

                            for time, value in points do
                                canvas.FillCircle(x time, y value, 3.5f)

                        canvas.DrawString(
                            DateTimeOffset.FromUnixTimeSeconds(int64 first).ToLocalTime().ToString("MMM d"),
                            left,
                            top + height + 8.f,
                            width / 2.f,
                            18.f,
                            HorizontalAlignment.Left,
                            VerticalAlignment.Center
                        )

                        canvas.DrawString(
                            now.ToString("MMM d"),
                            left + width / 2.f,
                            top + height + 8.f,
                            width / 2.f,
                            18.f,
                            HorizontalAlignment.Right,
                            VerticalAlignment.Center
                        )
                finally
                    canvas.RestoreState() |> ignore
        }
