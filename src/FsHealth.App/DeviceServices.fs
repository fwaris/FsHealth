namespace FsHealth.App

open System
open System.IO
open System.Security.Cryptography
open FsHealth
open Microsoft.Maui.ApplicationModel
open Microsoft.Maui.ApplicationModel.DataTransfer
open Microsoft.Maui.Storage

module DeviceServices =
    let private journalPath () =
        Path.Combine(FileSystem.AppDataDirectory, "journal.fsh")

    let private keyName = "fshealth.journal.key.v1"

    let private key () =
        task {
            let! stored = SecureStorage.Default.GetAsync keyName

            match Option.ofObj stored with
            | Some value -> return Convert.FromBase64String value
            | None when File.Exists(journalPath ()) ->
                return
                    raise (
                        InvalidOperationException
                            "The device encryption key is unavailable. The existing journal has been preserved."
                    )
            | None ->
                let value = RandomNumberGenerator.GetBytes 32
                do! SecureStorage.Default.SetAsync(keyName, Convert.ToBase64String value)
                return value
        }

    let load () =
        async {
            try
                let! secret = key () |> Async.AwaitTask
                return! JournalFile.load (journalPath ()) secret |> Async.AwaitTask
            with _ ->
                return
                    Error
                        "The encrypted journal could not be opened. Check device security and retry. Existing data has been preserved."
        }

    let save (journal: Journal) =
        async {
            try
                let! secret = key () |> Async.AwaitTask
                do! JournalFile.save (journalPath ()) secret journal |> Async.AwaitTask
                return Ok()
            with _ ->
                return
                    Error
                        "The reading could not be saved on this device. Your previous journal is unchanged; please retry."
        }

    let share (csv: string) =
        async {
            try
                let folder = Path.Combine(FileSystem.CacheDirectory, "exports")
                Directory.CreateDirectory folder |> ignore
                // The recipient owns its shared copy. Remove older exports from our cache.
                for previous in Directory.EnumerateFiles(folder, "*.csv") do
                    File.Delete previous

                let timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss")
                let path = Path.Combine(folder, $"FsHealth-{timestamp}.csv")

                do!
                    File.WriteAllTextAsync(path, csv, System.Text.UTF8Encoding(true))
                    |> Async.AwaitTask

                do!
                    MainThread.InvokeOnMainThreadAsync(
                        Func<System.Threading.Tasks.Task>(fun () ->
                            Share.Default.RequestAsync(
                                ShareFileRequest(Title = "Export FsHealth journal", File = ShareFile(path, "text/csv"))
                            ))
                    )
                    |> Async.AwaitTask

                return Ok()
            with _ ->
                return Error "The export could not be shared. Please try again."
        }
