#r "packages/FSharp.Compiler.Service/lib/net45/FSharp.Compiler.Service.dll"
#r "packages/Suave/lib/net40/Suave.dll"
#r "FakeLib.dll"

open Fake
open Suave
open Suave.Web
open System
open System.IO
open Microsoft.FSharp.Compiler.Interactive.Shell

let sbOut = new Text.StringBuilder()
let sbErr = new Text.StringBuilder()

let fsiSession =
  let inStream = new StringReader("")
  let outStream = new StringWriter(sbOut)
  let errStream = new StringWriter(sbErr)
  let fsiConfig = FsiEvaluationSession.GetDefaultConfiguration()
  let argv = Array.append [|"/fake/fsi.exe"; "--quiet"; "--noninteractive"; "-d:DO_NOT_START_SERVER"|] [||]
  FsiEvaluationSession.Create(fsiConfig, argv, inStream, outStream, errStream)

let reportFsiError (e:exn) =
  traceError "Reloading app.fsx script failed."
  traceError (sprintf "Message: %s\nError: %s" e.Message (sbErr.ToString().Trim()))
  sbErr.Clear() |> ignore

let reloadScript () =
  try
    traceImportant "Reloading app.fsx script..."
    let appFsx = __SOURCE_DIRECTORY__ @@ "app.fsx"
    fsiSession.EvalInteraction(sprintf "#load @\"%s\"" appFsx)
    fsiSession.EvalInteraction("open App")
    match fsiSession.EvalExpression("app") with
    | Some app ->
        let webPart = app.ReflectionValue :?> WebPart
        Some(webPart)
    | None -> failwith "Couldn't get 'app' value"
  with e -> reportFsiError e; None

// --------------------------------------------------------------------------------------
// Suave server that redirects all request to currently loaded version
// --------------------------------------------------------------------------------------

let currentApp = ref (fun _ -> async { return None })

let serverConfig =
  { defaultConfig with
      homeFolder = Some __SOURCE_DIRECTORY__
      logger = Logging.Loggers.saneDefaultsFor Logging.LogLevel.Debug
      bindings = [ HttpBinding.mkSimple HTTP  "127.0.0.1" 8033] }

let reloadAppServer () =
  reloadScript() |> Option.iter (fun app ->
    currentApp.Value <- app
    traceImportant "New version of app.fsx loaded!" )

Target "run" (fun _ ->
  let app ctx = currentApp.Value ctx
  let _, server = startWebServerAsync serverConfig app

  // Start Suave to host it on localhost
  reloadAppServer()
  Async.Start(server)
  // Open web browser with the loaded file
  System.Diagnostics.Process.Start("http://localhost:8033") |> ignore


  // Watch for changes & reload when app.fsx changes
  use rootWatcher =
      !! (__SOURCE_DIRECTORY__ @@ "*.*")
      -- (__SOURCE_DIRECTORY__ @@ ".*")
      -- (__SOURCE_DIRECTORY__ @@ "run.log")
    |> WatchChangesWithOptions {IncludeSubdirectories = false} (fun x -> printfn "Changes: %A" x; reloadAppServer())

  use folderWatcher =
      !! (__SOURCE_DIRECTORY__ @@ "content/*")
    |> WatchChanges (fun x -> printfn "Changes: %A" x; reloadAppServer())

  traceImportant "Waiting for app.fsx edits. Press any key to stop."
  //System.Console.ReadLine() |> ignore
  System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite)
)

// For atom
open System.Diagnostics
let runLog = __SOURCE_DIRECTORY__ @@ "run.log"
let pidFile = __SOURCE_DIRECTORY__ @@ ".pid"
Target "spawn" (fun _ ->
    if File.Exists(pidFile) then failwith "Build is running, do attach instead"

    let fakeExe = __SOURCE_DIRECTORY__ @@ "packages/FAKE/tools/FAKE.exe"
    let fakeArgs = "run --fsiargs build.fsx"
    let fileName,arguments = if isMono then "mono",(sprintf "%s %s" fakeExe fakeArgs) else fakeExe, fakeArgs

    let ps =
        ProcessStartInfo
            (
                WorkingDirectory = __SOURCE_DIRECTORY__,
                FileName = fileName, //__SOURCE_DIRECTORY__ @@ "build.sh",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            )
    use fs = new FileStream(runLog, FileMode.Create, FileAccess.ReadWrite, FileShare.Read)
    use sw = new StreamWriter(fs)
    let p = Process.Start(ps)
    p.ErrorDataReceived.Add(fun data -> printfn "%s" data.Data; sw.WriteLine(data.Data); sw.Flush())
    p.OutputDataReceived.Add(fun data -> printfn "%s" data.Data; sw.WriteLine(data.Data); sw.Flush())
    p.EnableRaisingEvents <- true
    p.BeginOutputReadLine()
    p.BeginErrorReadLine()

    File.WriteAllText(pidFile, string p.Id)
    while File.Exists(pidFile) do
        System.Threading.Thread.Sleep(500)
    trace "Killing process now"
    p.Kill()
)

Target "attach" (fun _ ->
    if not (File.Exists(pidFile)) then
        failwith "The build is not running!"
    use fs = new FileStream(runLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
    use sr = new StreamReader(fs)
    while File.Exists(pidFile) do
        let msg = sr.ReadLine()
        if not (String.IsNullOrEmpty(msg)) then
            printfn "%s" msg
        else System.Threading.Thread.Sleep(500)
)

Target "stop" (fun _ ->
    if not (File.Exists(pidFile)) then
        failwith "The build is not running!"
    File.Delete(pidFile)
)

RunTargetOrDefault "run"
