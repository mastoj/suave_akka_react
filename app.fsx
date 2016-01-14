#I "./packages/Akka/lib/net45/"
#I "./packages/Newtonsoft.Json/lib/net45/"
#I "./packages/FsPickler/lib/net45/"
#I "./packages/FSPowerPack.Linq.Community/lib/net40/"
#I "./packages/Akka.FSharp/lib/net45/"
#I "./packages/Suave/lib/net40/"
#I "./packages/FSharp.Data/lib/net40"

#r "Suave.dll"
#r "Akka.dll"
#r "FsPickler.dll"
#r "FSharp.PowerPack.Linq.dll"
#r "Akka.FSharp.dll"
#r "FSharp.Data"

#load "chat.fsx"

open Akka
open Akka.FSharp
open Akka.Actor
open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful
open Suave.RequestErrors
open Suave.Sockets
open Suave.WebSocket
open System.IO
open System.Threading
open Chat

let cancellationTokenSource = new CancellationTokenSource()
let token = cancellationTokenSource.Token
let config = { defaultConfig with cancellationToken = token }

let index() = 
    printfn "Reading index"
    File.ReadAllText(__SOURCE_DIRECTORY__ + "/content/index.html")
let file str = File.ReadAllText(__SOURCE_DIRECTORY__ + "/content/" + str)

type ContentType = 
    | JS
    | CSS
    | JSX

let parseContentType = function
    | "js" -> JS
    | "css" -> CSS
    | "jsx" -> JSX

let serveContent (filePath,fileEnding) =
    request(fun _ -> 
        let contentType = fileEnding |> parseContentType
        let mimetype = 
            match contentType with
            | JS -> "application/javascript"
            | CSS -> "text/css"
            | JSX -> "text/babel"
        Writers.setMimeType mimetype
        >=> OK (file (filePath + "." + fileEnding))
    )

module API = 
    open FSharp.Data
    type JsonTypes = JsonProvider<"""
    {
        "Users": [{"UserName": "John doe"}],
        "Notification": {
            "Message": "This is the message",
            "UserName": "John Doe"
        } 
    }
    """>

    

    // let connect roomName :WebPart=
    //     let response = JsonValue.Array(["tomas"; "Stuart"] |> List.map (fun n -> JsonTypes.User(n).JsonValue) |> List.toArray) 
    //     Writers.setMimeType "application/json"
    //     >=> OK (response.ToString())

    let getUsers roomName = 
        let response = JsonValue.Array(["tomas"; "Stuart"] |> List.map (fun n -> JsonTypes.User(n).JsonValue) |> List.toArray) 
        Writers.setMimeType "application/json"
        >=> OK (response.ToString())

    open Suave.Sockets.Control
    open Suave.Sockets.SocketOp
    let utf8Bytes (str:string) = System.Text.Encoding.UTF8.GetBytes(str)
    let utf8String (bytes:byte []) = System.Text.Encoding.UTF8.GetString(bytes)
    
    let joinRoom (chat:Chat.Chat) roomName userName (webSocket : WebSocket) cx = 
        
        printfn "trying to shake hands: %s %s" roomName userName
        let notificationFunc (Chat.Message msg, Chat.UserName name) = 
            let response = JsonTypes.Notification(msg, name).JsonValue.ToString()
            async {
                let! x = webSocket.send Opcode.Text (utf8Bytes response) true
                match x with | _ -> return ()
            } |> Async.RunSynchronously
            
        let sendMsgFunc = chat.JoinRoom (Chat.RoomName roomName) (Chat.UserName userName) notificationFunc
        
        webSocket.send Opcode.Text (utf8Bytes """ "{'msg': "Hello from socket"}" """) true |> Async.RunSynchronously |> ignore 
        printfn "before socket"
        socket {
            while true do
                let! inChoice = webSocket.read()
                match inChoice with
                | (Opcode.Text, bytes, true) ->
                    printfn "Got some message"
                    let msgStr = utf8String bytes 
                    sendMsgFunc (Chat.Message msgStr)
                | _ -> ()
        }

    let app (chat:Chat.Chat) = 
        choose 
            [
                POST >=> pathScan "/api/room/%s" (fun name -> chat.CreateRoom (Chat.RoomName name); OK ("Created " + name))
//                POST >=> pathScan "/api/room/%s/join" connect
                GET >=> pathScan "/api/room/%s/users" getUsers
                pathScan "/api/room/%s/join/%s" (fun (roomName,userName) -> handShake (joinRoom chat roomName userName))
            ]

let serveIndex : WebPart = 
    request (fun r -> 
        let s = index()
        OK s        
    )

let content = 
    choose
        [
            path "/" >=> Writers.setMimeType "text/html" >=> serveIndex
            pathScan "/%s.%s" serveContent
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
