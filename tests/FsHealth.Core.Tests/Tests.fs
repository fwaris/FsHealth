namespace FsHealth.Tests

open System
open System.IO
open System.Security.Cryptography
open System.Text
open FsHealth
open Xunit

module Tests =
    let private unwrap =
        function
        | Ok value -> value
        | Error e -> failwithf "%A" e

    let private now = DateTimeOffset(2026, 11, 8, 15, 0, 0, TimeSpan.Zero)

    let private glucose at value =
        {
            Id = ReadingId(Guid.NewGuid())
            MeasuredAt = at
            Measurement = Glucose(GlucoseValue.create MgPerDl value |> unwrap, Fasting)
            Notes = ""
        }

    let private pressure at =
        {
            Id = ReadingId(Guid.NewGuid())
            MeasuredAt = at
            Measurement = Pressure(BloodPressure.create 122 78 (Some 65) |> unwrap)
            Notes = "Evening"
        }

    let private loaded journal =
        let m, _ = State.init () in State.update (Loaded(Ok journal)) m |> fst

    let private apply message model = State.update message model |> fst

    let private draft value =
        loaded Journal.empty
        |> apply (AddReading(GlucoseKind, ReadingId(Guid.NewGuid()), now))
        |> apply (ChangeField(GlucoseText, value))

    [<Fact>]
    let ``glucose units round trip without reinterpreting the reading`` () =
        let value = GlucoseValue.create MmolPerL 5.5m |> unwrap
        Assert.Equal(99m, GlucoseValue.inUnit MgPerDl value)
        Assert.Equal(5.5m, GlucoseValue.inUnit MmolPerL value)

    [<Theory>]
    [<InlineData(0)>]
    [<InlineData(-20)>]
    [<InlineData(2001)>]
    let ``invalid glucose is rejected`` value =
        Assert.True(GlucoseValue.create MgPerDl (decimal value) |> Result.isError)

    [<Fact>]
    let ``extreme mmol input returns validation error without overflowing`` () =
        Assert.True(GlucoseValue.create MmolPerL Decimal.MaxValue |> Result.isError)

    [<Fact>]
    let ``clearing a native text field tolerates null at the boundary`` () =
        let cleared = draft "105" |> apply (ChangeField(GlucoseText, null))
        let rejected, effects = State.update (SaveDraft now) cleared
        Assert.True rejected.Error.IsSome
        Assert.Empty effects

    [<Fact>]
    let ``unit preference changes do not modify stored values`` () =
        let original = glucose now 99m

        let model =
            loaded
                { Journal.empty with
                    Readings = [ original ]
                }

        let saved = model |> apply (SetUnit MmolPerL) |> apply (Persisted(Ok()))
        Assert.Equal<Reading list>([ original ], (State.journal saved).Readings)
        Assert.Equal("5.5", Reading.valueLabel (State.journal saved).Unit original)

    [<Fact>]
    let ``editing notes and switching units preserve original glucose precision`` () =
        let original = glucose now 100m

        let model =
            loaded
                {
                    Unit = MmolPerL
                    Readings = [ original ]
                }

        let saved =
            model
            |> apply (EditReading original.Id)
            |> apply (ChangeDraftUnit MgPerDl)
            |> apply (ChangeDraftUnit MmolPerL)
            |> apply (ChangeField(NotesText, "Updated note"))
            |> apply (SaveDraft now)
            |> apply (Persisted(Ok()))

        let reading = (State.journal saved).Readings.Head
        Assert.Equal(original.Measurement, reading.Measurement)
        Assert.Equal("Updated note", reading.Notes)

    [<Fact>]
    let ``blood pressure validates pair and optional pulse`` () =
        Assert.True(BloodPressure.create 80 120 None |> Result.isError)
        Assert.True(BloodPressure.create 120 80 (Some 0) |> Result.isError)
        Assert.True(BloodPressure.create 120 80 None |> Result.isOk)

    [<Fact>]
    let ``decimal comma is accepted and future measurements are rejected`` () =
        let model = draft "5,5" |> apply (ChangeDraftUnit MmolPerL)
        // Unit switching converts the draft. Enter a fresh mmol/L value afterward.
        let model = model |> apply (ChangeField(GlucoseText, "5,5"))
        let pending, effects = State.update (SaveDraft now) model
        Assert.Single(effects) |> ignore

        match pending.Pending.Value.Readings.Head.Measurement with
        | Glucose(v, _) -> Assert.Equal(99m, GlucoseValue.inUnit MgPerDl v)
        | _ -> failwith "Expected glucose"

        let future = model |> apply (ChangeDate(now.AddDays(1.).LocalDateTime.Date))
        let rejected, effects = State.update (SaveDraft now) future
        Assert.Empty effects
        Assert.True rejected.Error.IsSome

    [<Fact>]
    let ``failed save preserves committed journal and editable draft`` () =
        let model = draft "105"
        let saving, _ = State.update (SaveDraft now) model
        Assert.Empty (State.journal saving).Readings
        Assert.True saving.Pending.IsSome
        let failed = saving |> apply (Persisted(Error "Disk full"))
        Assert.Empty (State.journal failed).Readings
        Assert.True failed.Draft.IsSome
        Assert.True failed.Pending.IsNone
        Assert.Equal(Some "Disk full", failed.Error)
        let retry, _ = State.update (SaveDraft now) failed
        let saved = retry |> apply (Persisted(Ok()))
        Assert.Single((State.journal saved).Readings) |> ignore
        Assert.True saved.Draft.IsNone

    [<Fact>]
    let ``pending write blocks competing writes navigation and duplicate saves`` () =
        let saving = draft "105" |> apply (SaveDraft now)

        for message in [ Navigate History; SetUnit MmolPerL; SaveDraft now; Confirm; RequestSave ] do
            let next, effects = State.update message saving
            Assert.Equal(saving, next)
            Assert.Empty effects

    [<Fact>]
    let ``damaged load cannot be overwritten by an empty journal`` () =
        let initial, _ = State.init ()
        let failed = initial |> apply (Loaded(Error "Damaged"))

        for message in
            [
                SetUnit MmolPerL
                AddReading(GlucoseKind, ReadingId(Guid.NewGuid()), now)
                Confirm
                Export
            ] do
            let next, effects = State.update message failed
            Assert.Equal(failed, next)
            Assert.Empty effects

        let retry, effects = State.update RetryLoad failed
        Assert.Equal(Loading, retry.Store)
        Assert.Equal<Effect list>([ Load ], effects)

    [<Fact>]
    let ``edit replaces one reading and preserves its identity`` () =
        let first = glucose now 100m
        let second = pressure (now.AddDays(-1.))

        let model =
            loaded
                { Journal.empty with
                    Readings = [ first; second ]
                }

        let saving =
            model
            |> apply (EditReading first.Id)
            |> apply (ChangeField(GlucoseText, "115"))
            |> apply (SaveDraft now)

        let saved = saving |> apply (Persisted(Ok()))
        let readings = (State.journal saved).Readings
        Assert.Equal(2, readings.Length)
        Assert.Single(readings |> List.filter (fun r -> r.Id = first.Id)) |> ignore
        Assert.Contains(second, readings)
        Assert.Equal("115", readings |> List.find (fun r -> r.Id = first.Id) |> Reading.valueLabel MgPerDl)

    [<Fact>]
    let ``deletion requires confirmation and cancel keeps data`` () =
        let first, second = glucose now 100m, pressure now

        let model =
            loaded
                { Journal.empty with
                    Readings = [ first; second ]
                }

        Assert.Equal(model, model |> apply Confirm)
        let asked = model |> apply (AskConfirmation(DeleteReading first.Id))
        Assert.Equal(2, (State.journal asked).Readings.Length)
        let cancelled = asked |> apply CancelConfirmation
        Assert.Equal(2, (State.journal cancelled).Readings.Length)
        let removed = asked |> apply Confirm |> apply (Persisted(Ok()))
        Assert.Equal<Reading list>([ second ], (State.journal removed).Readings)

    [<Fact>]
    let ``journal serialization preserves both reading types notes units and offsets`` () =
        let note =
            { glucose (now.ToOffset(TimeSpan.FromHours(-5.))) 99m with
                Notes = "Breakfast, toast\n\"Remember\""
            }

        let journal =
            {
                Unit = MmolPerL
                Readings = [ note; pressure now ]
            }

        let decoded = JournalCodec.encode journal |> JournalCodec.decode |> unwrap
        Assert.Equal(journal, decoded)
        Assert.Equal(note.MeasuredAt.Offset, decoded.Readings.Head.MeasuredAt.Offset)

    [<Theory>]
    [<InlineData("null")>]
    [<InlineData("{}")>]
    [<InlineData("{\"Version\":2,\"Unit\":\"mg/dL\",\"Readings\":[]}")>]
    [<InlineData("{\"Version\":1,\"Unit\":\"mg/dL\",\"Readings\":[null]}")>]
    [<InlineData("not json")>]
    let ``malformed and future journal formats fail closed`` (json: string) =
        Assert.True(Encoding.UTF8.GetBytes json |> JournalCodec.decode |> Result.isError)

    [<Fact>]
    let ``duplicate identifiers cannot be loaded`` () =
        let reading = glucose now 100m

        Assert.True(
            { Journal.empty with
                Readings = [ reading; reading ]
            }
            |> JournalCodec.encode
            |> JournalCodec.decode
            |> Result.isError
        )

    [<Fact>]
    let ``encrypted payload authenticates bytes and key`` () =
        let key = RandomNumberGenerator.GetBytes 32

        let bytes =
            JournalCodec.encode
                { Journal.empty with
                    Readings = [ glucose now 100m ]
                }

        let encrypted = Encryption.encrypt key bytes
        Assert.Equal<byte array>(bytes, Encryption.decrypt key encrypted)
        Assert.NotEqual<byte array>(encrypted, Encryption.encrypt key bytes)
        encrypted[encrypted.Length - 1] <- encrypted[encrypted.Length - 1] ^^^ 1uy

        Assert.ThrowsAny<CryptographicException>(fun () -> Encryption.decrypt key encrypted |> ignore)
        |> ignore

        Assert.ThrowsAny<CryptographicException>(fun () ->
            Encryption.decrypt (RandomNumberGenerator.GetBytes 32) (Encryption.encrypt key bytes)
            |> ignore)
        |> ignore

    [<Fact>]
    let ``encrypted file survives replacement and a failed write`` () =
        task {
            let folder =
                Path.Combine(Path.GetTempPath(), "FsHealth-tests", Guid.NewGuid().ToString("N"))

            Directory.CreateDirectory folder |> ignore
            let path = Path.Combine(folder, "journal.fsh")
            let key = RandomNumberGenerator.GetBytes 32

            let original =
                { Journal.empty with
                    Readings = [ glucose now 100m ]
                }

            try
                do! JournalFile.save path key original
                let! loaded = JournalFile.load path key
                Assert.Equal(original, unwrap loaded)
                Directory.CreateDirectory(path + ".pending") |> ignore

                let! _ =
                    Assert.ThrowsAnyAsync<UnauthorizedAccessException>(fun () ->
                        JournalFile.save path key Journal.empty)

                let! preserved = JournalFile.load path key
                Assert.Equal(original, unwrap preserved)
                Directory.Delete(path + ".pending")
                do! JournalFile.save path key { original with Unit = MmolPerL }
                let! replaced = JournalFile.load path key
                Assert.Equal(MmolPerL, (unwrap replaced).Unit)
            finally
                Directory.Delete(folder, true)
        }

    [<Fact>]
    let ``CSV escapes notes mitigates formula injection and includes offsets`` () =
        let reading =
            { glucose now 99m with
                Notes = "=HYPERLINK(\"bad\")\nsecond line, comma"
            }

        let csv =
            Csv.export
                {
                    Unit = MmolPerL
                    Readings = [ reading; pressure now ]
                }

        Assert.Contains("\"5.5\",\"mmol/L\"", csv)
        Assert.Contains("\"'=HYPERLINK(\"\"bad\"\")\nsecond line, comma\"", csv)
        Assert.Contains("+00:00", csv)

    [<Fact>]
    let ``period averages separate reading kinds and include first local day`` () =
        let localNow = now.ToLocalTime()
        let first = DateTimeOffset(localNow.LocalDateTime.Date.AddDays(-6.))

        let journal =
            { Journal.empty with
                Readings =
                    [
                        glucose first 90m
                        glucose now 110m
                        glucose (first.AddTicks(-1L)) 900m
                        pressure now
                    ]
            }

        let readings = Journal.withinDays now 7 journal
        Assert.Equal(3, readings.Length)
        Assert.Equal(Some 100m, Journal.glucoseAverage MgPerDl readings)
        Assert.Equal(Some(122m, 78m), Journal.pressureAverage readings)
        Assert.Equal<decimal option>(None, Journal.glucoseAverage MgPerDl [])
