open System
open System.Diagnostics
open System.IO

let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))

let run arguments =
    let start =
        ProcessStartInfo("dotnet", WorkingDirectory = root, UseShellExecute = false)

    arguments |> List.iter start.ArgumentList.Add
    use childProcess = Process.Start start
    childProcess.WaitForExit()

    if childProcess.ExitCode <> 0 then
        failwithf "dotnet %s exited with %d" (String.concat " " arguments) childProcess.ExitCode

let app = "src/FsHealth.App/FsHealth.App.fsproj"
let tests = "tests/FsHealth.Core.Tests/FsHealth.Core.Tests.fsproj"
let action = fsi.CommandLineArgs |> Array.tryItem 1 |> Option.defaultValue "check"
let requestedRuntime = fsi.CommandLineArgs |> Array.tryItem 2

match action with
| "check" ->
    run [ "tool"; "restore" ]
    run [ "fantomas"; "src"; "tests"; "scripts"; "--check" ]
    run [ "test"; tests; "-c"; "Release" ]
| "format" ->
    run [ "tool"; "restore" ]
    run [ "fantomas"; "src"; "tests"; "scripts" ]
| "android" ->
    run
        [
            "build"
            app
            "-c"
            "Debug"
            "-f"
            "net10.0-android"
            "-p:FsHealthAndroidOnly=true"
        ]
| "ios" ->
    if not (OperatingSystem.IsMacOS()) then
        failwith "Building iOS requires a Mac with the workload-compatible Xcode."

    run
        [
            "build"
            app
            "-c"
            "Debug"
            "-f"
            "net10.0-ios"
            "-r"
            requestedRuntime |> Option.defaultValue "iossimulator-arm64"
            "-p:FsHealthIosOnly=true"
        ]
| "maccatalyst" ->
    if not (OperatingSystem.IsMacOS()) then
        failwith "Building Mac Catalyst requires a Mac with the workload-compatible Xcode."

    let runtime =
        requestedRuntime
        |> Option.defaultValue (
            if
                System.Runtime.InteropServices.RuntimeInformation.OSArchitecture =
                    System.Runtime.InteropServices.Architecture.Arm64
            then
                "maccatalyst-arm64"
            else
                "maccatalyst-x64"
        )

    run
        [
            "build"
            app
            "-c"
            "Debug"
            "-f"
            "net10.0-maccatalyst"
            "-r"
            runtime
            "-p:FsHealthMacCatalystOnly=true"
        ]
| value -> failwithf "Unknown action '%s'. Use check, format, android, ios, or maccatalyst." value
