#I "./packages/Akka/lib/net45/"
#I "./packages/Newtonsoft.Json/lib/net45/"
#I "./packages/FsPickler/lib/net45/"
#I "./packages/FSPowerPack.Linq.Community/lib/net40/"
#I "./packages/Akka.FSharp/lib/net45/"
#I "./packages/Suave/lib/net40/"

#r "Suave.dll"
#r "Akka.dll"
#r "FsPickler.dll"
#r "FSharp.PowerPack.Linq.dll"
#r "Akka.FSharp.dll"

#load "chat.fsx"

open Akka
open Akka.FSharp
open Akka.Actor
open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful
open Suave.RequestErrors
open System.IO
open System.Threading
open Chat

let cancellationTokenSource = new CancellationTokenSource()
let token = cancellationTokenSource.Token
let config = { defaultConfig with cancellationToken = token }

let index = File.ReadAllText(__SOURCE_DIRECTORY__ + "/content/index.html")
let file str = File.ReadAllText(__SOURCE_DIRECTORY__ + "/content/" + str)

type ContentType = 
    | JS
    | CSS

let parseContentType = function
    | "js" -> JS
    | "css" -> CSS

let serveContent (contentTypeStr,str) =
    let contentType = contentTypeStr |> parseContentType
    let (mimetype,relPath) = 
        match contentType with
        | JS -> "application/javascript","js/"
        | CSS -> "text/css","css/"
    Writers.setMimeType mimetype
    >=> OK (file (relPath + str))

module API = 
    let app (chat:Chat.Chat) = 
        choose 
            [
                POST >=> pathScan "/api/room/%s" (fun name -> chat.CreateRoom (Chat.RoomName name); OK ("Created " + name))
                path "/api/connect" >=> 
                    GET >=> OK "Hello"
            ]

let content = 
    choose
        [
            path "/" >=> Writers.setMimeType "text/html" >=> OK index
            pathScan "/%s/%s" serveContent
        ]

let app() = 
    choose 
        [
            (API.app (Chat.createChat()))
            content
            NOT_FOUND "Uhoh"
        ]
     
let _, server = startWebServerAsync config (app())
Async.Start(server, token)
printfn "Started"
//cancellationTokenSource.Cancel()

System.Console.ReadLine()