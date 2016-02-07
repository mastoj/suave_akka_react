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

let index() = 
    printfn "Reading index"
    File.ReadAllText(__SOURCE_DIRECTORY__ + "/content/index.html")
let file str = File.ReadAllText(__SOURCE_DIRECTORY__ + "/content/" + str)

type ContentType = 
    | JS
    | CSS
    | JSX

let parseContentType = function
    | "js" -> Some JS
    | "css" -> Some CSS
    | "jsx" -> Some JSX
    | _ -> None

let serveContent (filePath,fileEnding) =
    request(fun _ ->
        printfn "File %A" (filePath,fileEnding) 
        let contentType = fileEnding |> parseContentType
        let mimetype = 
            match contentType with
            | Some JS -> "application/javascript"
            | Some CSS -> "text/css"
            | Some JSX -> "text/babel"
            | None -> raise (exn "Not supported content type")
        Writers.setMimeType mimetype
        >=> OK (file (filePath + "." + fileEnding))
    )

module API = 
    open FSharp.Data
    open Chat
    type JsonTypes = JsonProvider<"""
    {
        "ChatReceived": {
            "_type": "ChatReceived",
            "Message": "This is the message",
            "UserName": "John Doe",
            "RoomName": "This is a room2"
        },
        "RoomCreated": {
            "_type": "RoomCreated",
            "RoomName": "This is a room2"
        },
        "RoomInfo": {
            "_type": "RoomInfo",
            "RoomName": "Name",
            "Users": [{"UserName": "John doe"}]
        },
        "RoomList": {
            "_type": "RoomList",
            "Rooms": [{"RoomName": "Name"}]
        }
    }
    """>

    type SayCommand = JsonProvider<"""
        {
            "_type": "Say",
            "Message": "This is a message",
            "RoomName": "InRoom"
        }
    """>
    
    type CreateRoomCommand = JsonProvider<"""
        {
            "_type": "CreateRoom",
            "RoomName": "This is the name"
        }
    """>
    
    let (|Say|_|) (str:string) =
        let command = SayCommand.Parse(str)
        match command.Type with
        | "Say" -> Some (Say command)
        | _ -> None
    
    let (|CreateRoom|_|) (str:string) =
        let command = CreateRoomCommand.Parse(str)
        match command.Type with
        | "CreateRoom" -> Some (CreateRoom command)
        | _ -> None
    
    let getUsers roomName = 
        let response = JsonValue.Array(["tomas"; "Stuart"] |> List.map (fun n -> JsonTypes.User(n).JsonValue) |> List.toArray) 
        Writers.setMimeType "application/json"
        >=> OK (response.ToString())

    open Suave.Sockets.Control
    open Suave.Sockets.SocketOp
    let utf8Bytes (str:string) = System.Text.Encoding.UTF8.GetBytes(str)
    let utf8String (bytes:byte []) = System.Text.Encoding.UTF8.GetString(bytes)

    let sendTextOnSocket (socket: WebSocket) text =
        async {
            printfn "Sending reponse: %A" text
            let! x = socket.send Opcode.Text (utf8Bytes text) true
            match x with | _ -> return ()
        } |> Async.StartImmediate
         

    let connect (chat:Chat.ChatServer) userName (webSocket : WebSocket) cx = 
        printfn "trying to shake hands: %A" userName
        let notificationHandler = function
            | UserSaid (UserName userName,Message message,RoomName roomName) -> 
                JsonTypes.ChatReceived("ChatReceived", message, userName, roomName).JsonValue.ToString() // JsonTypes..JsonValue.ToString()
                |> sendTextOnSocket webSocket
            | RoomCreated (RoomName name) ->
                JsonTypes.RoomCreated("RoomCreated", name).JsonValue.ToString()
                |> sendTextOnSocket webSocket
            
        let connection = Chat.createConnection chat userName notificationHandler
        let roomNames = connection.GetRoomList() |> List.map (fun (RoomName n) -> JsonTypes.Room(n)) |> List.toArray
        JsonTypes.RoomList("RoomList", roomNames).ToString()
        |> sendTextOnSocket webSocket
        
        printfn "Created connection"
        socket {
            while true do
                let! inChoice = webSocket.read()
                match inChoice with
                | (Opcode.Text, bytes, true) ->
                    let msgStr = utf8String bytes 
                    printfn "Got some message %A" msgStr
                    
                    match msgStr with
                    | Say command -> 
                        connection.Say ((Chat.Message command.Message), RoomName command.RoomName)
                    | CreateRoom command -> 
                        printfn "Creating room %A" command
                        connection.CreateRoom (RoomName command.RoomName)
                        printfn "Create room done"
                    | _ -> raise (exn "unsupported command")
                    
                    printfn "Parsed %A" msgStr
                | _ -> ()
        }

    let app (chat:Chat.ChatServer) = 
        choose 
            [
                pathScan "/_socket/connect/%s" (fun (userName) -> handShake (connect chat (UserName userName)))
//                POST >=> pathScan "/api/room/%s" (fun name -> chat.CreateRoom (Chat.RoomName name); OK ("Created " + name))
//                POST >=> pathScan "/api/room/%s/join" connect
                // GET >=> pathScan "/api/room/%s/users" getUsers
                // pathScan "/api/room/%s/join/%s" (fun (roomName,userName) -> handShake (joinRoom chat roomName userName))
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

let app = 
    choose 
        [
            path "/favicon.ico" >=> NOT_FOUND "No favicon"
            (API.app (Chat.createChatServer()))
            content
            NOT_FOUND "Uhoh"
        ]
